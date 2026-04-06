using Microsoft.Extensions.Logging;
using OpenClawManager.Common.Install;
using OpenClawManager.Common.ProcessClass;
using OpenClawManager.Common.Uninstall;
using System.Diagnostics;

namespace OpenClawManager.Core.Services;

public class SimpleProcessManager
{
    //Process 进程
    private Process? _process;
    private readonly ILogger<SimpleProcessManager> _logger;
    private readonly OpenClawUninstaller _uninstaller;
    private readonly OpenClawInstall _install;
    private readonly LogTrackerService _logTracker;
    //状态锁
    private readonly object _lock = new object();
    private readonly ManualResetEventSlim _exitEvent = new ManualResetEventSlim(false);
    private bool _disposed;
    //是否尝试优雅关闭
    public bool EnableGracefulShutdown { get; set; } = true;
    //判断是否允许中，判断条件 _process不为空 并且 没有退出
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process != null && !_process.HasExited;
            }

        }
    }

    public SimpleProcessManager(
        ILogger<SimpleProcessManager> logger,
        OpenClawUninstaller uninstaller,
        OpenClawInstall install,
        LogTrackerService logTracker)
    {
        _logger = logger;
        _uninstaller = uninstaller;
        _install = install;
        _logTracker = logTracker;
    }

    /// <summary>
    /// 启动进程
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public bool Start(int port = 18789, bool isOpenWeb = true)
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                _logger.LogWarning("尝试启动，但实例已在运行");
                return false;
            }
            try
            {
                string? openClawPath = ExecutableFinder.FindInPath("openclaw");
                if (string.IsNullOrEmpty(openClawPath))
                {
                    _logger.LogError("未在系统的 PATH 中找到 OpenClaw 可执行文件。" +
                        "请确保 OpenClaw 已正确安装，并将其所在目录添加到 PATH 环境变量中。");
                    return false;
                }
                //ProcessStartInfo 进程启动信息
                var psi = new ProcessStartInfo
                {
                    FileName = openClawPath,
                    Arguments = $"gateway --port {port}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                _exitEvent.Reset();

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                // 订阅事件（使用命名方法方便取消）
                _process.OutputDataReceived += OnOutputDataReceived;
                _process.ErrorDataReceived += OnErrorDataReceived;
                _process.Exited += OnExited;

                if (!_process.Start())
                {
                    _logger.LogError("进程启动失败");
                    return false;
                }
                //开始读取输出行
                _process.BeginOutputReadLine();
                //开始读取错误行
                _process.BeginErrorReadLine();

                //默认浏览器打开网站
                if (isOpenWeb)
                {
                    bool isWebNormal = BrowserLauncher.Open($"http://127.0.0.1:{port}");
                    if (!isWebNormal)
                    {
                        _logger.LogInformation($"自动打开浏览器失败，请手动访问: http://127.0.0.1:{port}");
                    }
                }

                _logger.LogInformation("OpenClaw启动成功，端口: {Port}", port);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 open claw 失败");
                return false;
            }
        }
    }

    /// <summary>
    /// 停止进程
    /// </summary>
    public bool Stop()
    {
        lock (_lock)
        {
            if (_process == null || _process.HasExited) return false;
            _logger.LogInformation("正在停止 OpenClaw...");
        }
        string? openClawPath = ExecutableFinder.FindInPath("openclaw");
        try
        {
            // 调用 openclaw gateway stop
            var stopInfo = new ProcessStartInfo
            {
                FileName = openClawPath, // 确保能找到 openclaw 命令
                Arguments = "gateway stop",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var stopProcess = Process.Start(stopInfo);
            stopProcess?.WaitForExit(); // 等待命令执行完成
            _logger.LogInformation("已停止");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止失败");
            return false;
        }
        finally
        {
            CleanupProcess();
        }
    }
    //public void Stop()
    //{
    //    Process? p;
    //    lock (_lock)
    //    {
    //        if (_process == null || _process.HasExited) return;
    //        p = _process;
    //    }

    //    try
    //    {
    //        _logger.LogWarning("终止 PID {pid}", p.Id);

    //        p.Kill(true);
    //    }
    //    finally
    //    {
    //        lock (_lock)
    //        {
    //            CleanupProcess();
    //        }
    //    }
    //}

    /// <summary>
    /// 卸载openclaw
    /// </summary>
    public async Task<UninstallResult> UninstallAsync(bool removeWorkspace = true, int stopTimeoutMs = 5000)
    {
        _logger.LogInformation("开始执行 openclaw 卸载流程");

        // 如果正在运行，先停止
        if (IsRunning)
        {
            _logger.LogInformation("检测到 openclaw 在运行，正在停止");
            Stop();
            await Task.Delay(1000); // 给进程一点时间完全退出
        }

        // 委托给卸载器
        return await _uninstaller.UninstallAsync(removeWorkspace);
    }

    /// <summary>
    /// 安装openclaw
    /// </summary>
    public async Task<InstallResult> InstallAsync(string? packageManager = null)
    {
        _logger.LogInformation("开始安装openclaw，检查安装状态中");
        if (IsRunning)
        {
            _logger.LogInformation("OpenClaw 正在运行，先停止...");
            Stop();
            await Task.Delay(1000);
        }
        _install.KillNodeProcesses();
        //检查openclaw是否安装
        string? openClawPath = ExecutableFinder.FindInPath("openclaw");
        if (!string.IsNullOrEmpty(openClawPath))
        {
            _logger.LogInformation("openclaw已安装，暂时不需要额外安装");
            return InstallResult.AlreadyInstalled;
        }
        //检查node环境
        if (!await _install.CheckNodeJsVersionAsync())
        {
            _logger.LogWarning("node未安装或者版本不符合要求，尝试自动安装");
            //下载node
            if (!await _install.InstallNodeJsAsync())
            {
                _logger.LogError("Node.js 自动安装失败，请手动安装 Node.js 22 或更高版本。");
                return InstallResult.NodeJsInstallFailed;
            }
        }
        // 检测包管理器并安装
        _logger.LogInformation("node已正常安装，检测包管理器...");
        var pm = packageManager ?? (await _install.FindPackageManagerAsync());
        if (string.IsNullOrEmpty(pm))
        {
            _logger.LogError("未找到 npm 或 pnpm，请手动安装。");
            return InstallResult.PackageManagerNotFound;
        }
        _logger.LogInformation("使用包管理器: {PackageManager}", pm);

        //配置国内镜像源并安装 OpenClaw
        _logger.LogInformation("配置国内镜像源以加速下载...");
        if (!await _install.ConfigureRegistryAsync(pm))
        {
            _logger.LogWarning("镜像源配置失败，将继续使用默认源尝试安装。");
        }

        _logger.LogInformation("开始安装 OpenClaw...预计耗时3分钟，请耐心等待");
        var (success, error) = await _install.InstallOpenClawAsync(pm);
        if (!success)
        {
            _logger.LogError("OpenClaw 安装失败: {Error}", error);
            return InstallResult.InstallFailed;
        }

        if (Start(18789, false))
        {
            await _install.StartAndOpenDashboardAsync();//注册token
        }


        _logger.LogInformation("OpenClaw 安装成功！");
        return InstallResult.Success;
    }

    // 清理资源
    private void CleanupProcess()
    {
        if (_process != null)
        {
            _process.OutputDataReceived -= OnOutputDataReceived;
            _process.ErrorDataReceived -= OnErrorDataReceived;
            _process.Exited -= OnExited;
            _process.Dispose();
            _process = null;
        }
        _exitEvent.Reset(); // 复位，以便下次启动
    }

    private void OnExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_process == null) return;
            var exitCode = _process.ExitCode;
            _logger.LogInformation("OpenClaw 进程退出，代码：{exitCode}", exitCode);
            _exitEvent.Set();//通知等待者
        }
    }

    // 事件处理方法（提取为私有方法，以方便取消订阅）
    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
            _logger.LogInformation("[OpenClaw] {Output}", e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
            _logger.LogError("[OpenClaw] {Error}", e.Data);
    }

}

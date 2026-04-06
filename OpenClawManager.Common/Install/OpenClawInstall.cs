using Microsoft.Extensions.Logging;
using OpenClawManager.Common.ProcessClass;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClawManager.Common.Install;

public class OpenClawInstall
{
    private readonly ILogger<OpenClawInstall> _logger;

    public OpenClawInstall(ILogger<OpenClawInstall> logger)
    {
        _logger = logger;
    }

    //检查node版本是否符合要求
    public async Task<bool> CheckNodeJsVersionAsync()
    {
        var result = await ExecutableFinder.RunAsync("node", "--version", _logger);

        if (!result.IsSuccess)
        {
            _logger.LogDebug("Node检查失败: {Error}", result.StdErr);
            return false;
        }

        var versionStr = result.StdOut.Trim().TrimStart('v', 'V').TrimEnd('\r', '\n');

        if (Version.TryParse(versionStr, out var ver))
        {
            _logger.LogInformation("Node.js版本: {Version}", ver);
            return ver >= new Version(22, 0);
        }

        _logger.LogWarning("无法解析版本: {Raw}", result.StdOut);
        return false;
    }
    //下载node
    public async Task<bool> InstallNodeJsAsync()
    {
        _logger.LogInformation("尝试自动安装node");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string nodeVersion = "22.12.0";
            string[] mirrors =
            [
                "https://npmmirror.com/mirrors/node",//阿里云
                "https://mirrors.cloud.tencent.com/nodejs", // 腾讯云
                "https://mirrors.huaweicloud.com/nodejs"    // 华为云
            ];

            string? downloadedMsiPath = null;
            Exception? lastException = null;

            //依次尝试每个镜像源
            foreach (var mirror in mirrors)
            {
                string msiUrl = $"{mirror}/v{nodeVersion}/node-v{nodeVersion}-x64.msi";
                string msiPath = Path.Combine(Path.Combine(Path.GetTempPath(), $"node-v{nodeVersion}-{Guid.NewGuid()}.msi"));
                _logger.LogInformation($"尝试从镜像源下载：{mirror}");
                try
                {
                    // 使用 HttpClient 并设置2 分钟超时
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    using var response = await client.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    //异步下载文件
                    await using var fs = new FileStream(msiPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                    await fs.FlushAsync();

                    _logger.LogInformation($"下载成功：{msiUrl}");
                    downloadedMsiPath = msiPath;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, $"从镜像源:\"{mirror}\"下载失败");
                    lastException = ex;
                    if (File.Exists(msiPath)) File.Delete(msiPath);
                }
            }
            if (downloadedMsiPath == null)
            {
                _logger.LogInformation(lastException, "所有镜像均下载失败，无法安装node");
                return false;
            }

            //静默安装node
            _logger.LogInformation("开始静默安装node.js ...");
            try
            {
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = $"/i \"{downloadedMsiPath}\" /quiet /norestart",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                installProcess.Start();
                await installProcess.WaitForExitAsync();

                if (installProcess.ExitCode != 0)
                {
                    _logger.LogError("msiexec 安装失败，退出代码: {ExitCode}", installProcess.ExitCode);
                    return false;
                }

                _logger.LogInformation("Node.js 安装成功。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行安装程序时发生异常。");
                return false;
            }
            finally
            {
                // 无论成功与否，清理临时安装文件
                if (File.Exists(downloadedMsiPath)) File.Delete(downloadedMsiPath);
            }

            // 配置 npm 国内镜像源
            //_logger.LogInformation("配置 npm 国内镜像源...");
            //var npmPath = ExecutableFinder.FindInPath("npm");
            //var npnmPath = ExecutableFinder.FindInPath("npnm");
            //if (npmPath != null)
            //{
            //    await ConfigureRegistryAsync(npmPath);
            //} 
            //else if (npnmPath!=null)
            //{
            //    await ConfigureRegistryAsync(npnmPath);
            //}
            //else
            //{
            //    _logger.LogWarning("未找到 npm或npnm，跳过镜像配置");
            //}


            // 最终验证
            return await CheckNodeJsVersionAsync();

        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: 可以使用 NodeSource 脚本安装
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = "-c \"curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - && sudo apt-get install -y nodejs\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Linux 自动安装 Node.js 失败");
                return false;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: 可以尝试使用 Homebrew 安装
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "brew",
                    Arguments = "install node@22",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                _logger.LogWarning("未找到 Homebrew，请手动安装 Node.js。");
                return false;
            }
        }
        return false;
    }
    //查找包管理器 
    public async Task<string?> FindPackageManagerAsync()
    {
        var npmPath = ExecutableFinder.FindInPath("npm");
        if (npmPath != null)
        {
            _logger.LogInformation("找到npm: {Path}", npmPath);
            return npmPath;
        }

        var pnpmPath = ExecutableFinder.FindInPath("pnpm");
        if (pnpmPath != null)
        {
            _logger.LogInformation("找到pnpm: {Path}", pnpmPath);
            return pnpmPath;
        }
        _logger.LogWarning("未找到npm或pnpm");
        return null;
    }
    //配置国内镜像源
    public async Task<bool> ConfigureRegistryAsync(string packageManagerPath)
    {
        var result = await ExecutableFinder.RunWithPathAsync(
            packageManagerPath,
            "config set registry https://registry.npmmirror.com",
            _logger);

        if (result.IsSuccess)
        {
            _logger.LogInformation("镜像源配置成功");
            return true;
        }

        _logger.LogError("镜像源配置失败: {Error}", result.StdErr);
        return false;
    }
    //安装openclaw
    public async Task<(bool Success, string Error)> InstallOpenClawAsync(string packageManagerPath)
    {
        //执行全局安装
        var result = await ExecutableFinder.RunWithPathAsync(
            packageManagerPath,
            "install -g openclaw@latest",
            _logger);
        if (!result.IsSuccess)
            return (false, result.StdErr);
        //找到安装目录，写入最小配置
        var openclawDir = await FindOpenClawGlobalDirAsync(packageManagerPath);
        if (openclawDir != null)
        {
            InjectMinimalConfig();
        }
        return (true, "");
    }
    //查找 OpenClaw 全局安装目录
    private async Task<string?> FindOpenClawGlobalDirAsync(string pmPath)
    {
        // npm/pnpm root -g 获取全局安装根目录
        var result = await ExecutableFinder.RunWithPathAsync(pmPath, "root -g", _logger);
        if (!result.IsSuccess) return null;

        var globalDir = result.StdOut.Trim();

        // OpenClaw 实际目录
        var openclawDir = Path.Combine(globalDir, "openclaw");

        return Directory.Exists(openclawDir) ? openclawDir : null;
    }
    // 注入最小配置
    private void InjectMinimalConfig()
    {
        //var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".openclaw");
        var configPath = Path.Combine(configDir, "openclaw.json");

        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        if (File.Exists(configPath) && IsValidConfig(configPath))
        {
            _logger.LogInformation("配置已存在，跳过注入");
            return;
        }

        var config = new
        {
            gateway = new
            {
                mode = "local",
                port = 18789,
                bind = "127.0.0.1"
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(configPath, json);
        _logger.LogInformation("注入最小配置（无默认模型）: {Path}", configPath);
    }
    //判断是否有配置文件
    private bool IsValidConfig(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(content) && content.Contains("\"gateway\"");
        }
        catch
        {
            return false;
        }
    }
    //终止可能锁定的node进程
    public void KillNodeProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("node");
            foreach (var proc in processes)
            {
                // 可根据需要过滤（例如只结束包含 openclaw 的进程）
                // 简单起见，结束所有 node 进程（注意：可能会影响其他 Node.js 应用）
                proc.Kill();
                proc.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "尝试结束 Node.js 进程时出错");
        }
    }
    //注册OpenClawToken
    public async Task<bool> StartAndOpenDashboardAsync()
    {
        try
        {
            //生成 Token 自动打开浏览器
            var result = await ExecutableFinder.RunAsync(
                "openclaw",
                "dashboard",  
                _logger);

            return result.IsSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动并打开 Dashboard 失败");
            return false;
        }
    }
}
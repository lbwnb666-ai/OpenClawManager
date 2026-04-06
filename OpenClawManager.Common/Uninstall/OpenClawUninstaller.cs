using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenClawManager.Common.ProcessClass;

namespace OpenClawManager.Common.Uninstall;

public class OpenClawUninstaller
{
    private readonly ILogger<OpenClawUninstaller> _logger;

    public OpenClawUninstaller(ILogger<OpenClawUninstaller> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 执行完整的卸载流程
    /// </summary>
    /// <param name="removeWorkspace">是否删除工作区（workspace）目录</param>
    /// <returns>卸载结果枚举</returns>
    public async Task<UninstallResult> UninstallAsync(bool removeWorkspace = true)
    {

        // 1. 检查 CLI 是否存在
        bool cliExists = await CheckOpenClawCliExistsAsync();
        int officialUninstallExitCode = -1;

        // 2. 如果 CLI 存在，执行官方卸载
        if (cliExists)
        {
            _logger.LogInformation("找到 openclaw 命令，尝试执行官方卸载");
            officialUninstallExitCode = await RunOfficialUninstallAsync(removeWorkspace);
            if (officialUninstallExitCode != 0)
                _logger.LogError($"官方卸载命令执行失败，退出码: {officialUninstallExitCode}");
            else
                _logger.LogInformation("官方卸载执行成功");
        }
        else
        {
            _logger.LogInformation("未找到 openclaw 命令，将尝试手动清理残留");
        }

        // 3. 尝试移除 openclaw 命令本身（npm/pnpm/二进制）
        _logger.LogInformation("执行命令清理，删除 openclaw 命令");
        bool commandRemoved = await UninstallOpenClawCommandAsync();
        if (!commandRemoved)
            _logger.LogError("命令清理失败");

        // 4. 深度清理配置和数据目录
        _logger.LogInformation("执行深度清理，删除配置和数据目录");
        bool cleanupOk = await CleanupRemainingDirsAsync(removeWorkspace);
        if (!cleanupOk)
            _logger.LogError("深度清理失败，可能残留了部分文件");

        // 5. Windows 计划任务清理
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogInformation("清理 Windows 计划任务");
            bool taskOk = await RemoveScheduledTaskAsync();
            if (!taskOk)
                _logger.LogWarning("无 Windows 计划任务");
        }

        // 6. 根据执行情况组合最终结果
        if (!cliExists && !cleanupOk)
            return UninstallResult.CliNotFoundAndCleanupFailed;
        if (!cliExists && cleanupOk)
            return UninstallResult.CliNotFoundButCleaned;
        if (cliExists && officialUninstallExitCode != 0)
            return UninstallResult.OfficialUninstallFailed;
        if (cleanupOk && (!cliExists || officialUninstallExitCode == 0))
            return UninstallResult.Success;

        return UninstallResult.Partial;
    }

    //检查openclaw命令是否存在
    private async Task<bool> CheckOpenClawCliExistsAsync()
    {
        try
        {
            string? path = ExecutableFinder.FindInPath("openclaw");
            return !string.IsNullOrEmpty(path);
        }
        catch
        {
            return false;
        }
    }
    //运行官方卸载程序
    private async Task<int> RunOfficialUninstallAsync(bool removeWorkspace)
    {
        string? openClawPath = ExecutableFinder.FindInPath("openclaw");
        if (string.IsNullOrEmpty(openClawPath))
        {
            _logger.LogError("未找到 openclaw 可执行文件，无法执行官方卸载");
            return -1;
        }

        var arguments = removeWorkspace ? "uninstall --all --yes" : "uninstall --yes";

        var uninstallProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = openClawPath,   // 使用完整路径
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            uninstallProcess.Start();
            await uninstallProcess.StandardInput.WriteLineAsync("");
            uninstallProcess.StandardInput.Close();  // 关闭输入流
            string output = await uninstallProcess.StandardOutput.ReadToEndAsync();
            string error = await uninstallProcess.StandardError.ReadToEndAsync();
            await uninstallProcess.WaitForExitAsync();

            if (!string.IsNullOrEmpty(output))
                _logger.LogDebug("卸载命令输出: {Output}", output);
            if (!string.IsNullOrEmpty(error))
                _logger.LogError("卸载命令错误: {Error}", error);

            return uninstallProcess.ExitCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行卸载命令时发生异常");
            return -1;
        }
    }
    //卸载OpenClaw命令
    private async Task<bool> UninstallOpenClawCommandAsync()
    {
        var source = await DetectOpenClawInstallSourceAsync();
        switch (source)
        {
            case OpenClawInstallSource.Npm:
                _logger.LogInformation("检测到 openclaw 由 npm 全局安装，尝试执行 npm uninstall -g openclaw");
                return await RunCommandAsync("npm", "uninstall -g openclaw", 30000);

            case OpenClawInstallSource.Pnpm:
                _logger.LogInformation("检测到 openclaw 由 pnpm 全局安装，尝试执行 pnpm remove -g openclaw");
                return await RunCommandAsync("pnpm", "remove -g openclaw", 30000);

            case OpenClawInstallSource.Yarn:
                _logger.LogInformation("检测到 openclaw 由 yarn 全局安装，尝试执行 yarn global remove openclaw");
                return await RunCommandAsync("yarn", "global remove openclaw", 30000);

            case OpenClawInstallSource.Binary:
                _logger.LogInformation("检测到 openclaw 为独立二进制文件，尝试直接删除文件");
                string? path = ExecutableFinder.FindInPath("openclaw");
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        File.Delete(path);
                        _logger.LogInformation("已删除二进制文件: {Path}", path);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "删除二进制文件失败: {Path}", path);
                        return false;
                    }
                }
                return false;

            default:
                _logger.LogWarning("无法确定 openclaw 的安装来源，请手动卸载。");
                return false;
        }
    }
    //清理剩余目录异步操作
    private async Task<bool> CleanupRemainingDirsAsync(bool removeWorkspace)
    {
        try
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var openClawDir = Path.Combine(userHome, ".openclaw");
            var clawDir = Path.Combine(userHome, ".clawbot");

            var tasks = new List<Task>();

            //清理主目录
            if (Directory.Exists(openClawDir))
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        Directory.Delete(openClawDir, true);
                        _logger.LogInformation($"已删除目录{openClawDir}");
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, $"删除目录失败：{openClawDir}"); }
                }));
            }

            // 清理工作区 (如果要求删除)
            if (removeWorkspace)
            {
                var workspaceDir = Path.Combine(userHome, ".openclaw", "workspace");
                if (Directory.Exists(workspaceDir))
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            Directory.Delete(workspaceDir, true);
                            _logger.LogInformation("已删除工作区: {Dir}", workspaceDir);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "删除工作区失败: {Dir}", workspaceDir); }
                    }));
                }
            }
            await Task.WhenAll(tasks);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "深度清理过程中发生为预期的错误");
            return false;
        }
    }
    //清理 Windows 计划任务 
    private async Task<bool> RemoveScheduledTaskAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = "/Delete /F /TN \"OpenClaw Gateway\"",
                UseShellExecute = true,      // 必须为 true 才能使用 Verb
                Verb = "runas",               // 以管理员身份运行
                CreateNoWindow = true
            };

            // 注意：Verb = "runas" 时不能重定向输出，因此需要改用 UseShellExecute = true
            // 此时无法读取标准输出和错误，但可以通过进程退出码判断
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "尝试删除计划任务时发生异常");
            return false;
        }
    }
    //异步运行命令
    private async Task<bool> RunCommandAsync(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            using var cts = new CancellationTokenSource(timeoutMs);
            await process.WaitForExitAsync(cts.Token);
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            if (!string.IsNullOrEmpty(output)) _logger.LogDebug("{Output}", output);
            if (!string.IsNullOrEmpty(error)) _logger.LogWarning("{Error}", error);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("命令 {FileName} {Arguments} 执行超时", fileName, arguments);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行命令 {FileName} {Arguments} 时发生异常", fileName, arguments);
            return false;
        }
    }
    //检测OpenClaw安装源异步操作
    private async Task<OpenClawInstallSource> DetectOpenClawInstallSourceAsync()
    {
        try
        {
            string? openClawPath = ExecutableFinder.FindInPath("openclaw");
            if (string.IsNullOrEmpty(openClawPath))
                return OpenClawInstallSource.Unknown;

            // 获取 npm 全局路径
            string npmPrefix = await GetNpmPrefixAsync();
            if (!string.IsNullOrEmpty(npmPrefix) && openClawPath.StartsWith(npmPrefix, StringComparison.OrdinalIgnoreCase))
                return OpenClawInstallSource.Npm;

            // 获取 pnpm 全局路径
            string pnpmPrefix = await GetPnpmPrefixAsync();
            if (!string.IsNullOrEmpty(pnpmPrefix) && openClawPath.StartsWith(pnpmPrefix, StringComparison.OrdinalIgnoreCase))
                return OpenClawInstallSource.Pnpm;

            // 获取 yarn 全局路径（通常与 npm 类似）
            string yarnPrefix = await GetYarnPrefixAsync();
            if (!string.IsNullOrEmpty(yarnPrefix) && openClawPath.StartsWith(yarnPrefix, StringComparison.OrdinalIgnoreCase))
                return OpenClawInstallSource.Yarn;

            return OpenClawInstallSource.Binary;
        }
        catch
        {
            return OpenClawInstallSource.Unknown;
        }
    }
    //获取 npm 前缀
    private async Task<string> GetNpmPrefixAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "npm",
                    Arguments = "prefix -g",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string prefix = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();
            return prefix;
        }
        catch
        {
            return "";
        }
    }
    //获取 pnpm 前缀
    private async Task<string> GetPnpmPrefixAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pnpm",
                    Arguments = "root -g",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string root = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();
            return root;
        }
        catch
        {
            return "";
        }
    }
    //获取 Yarn 前缀
    private async Task<string> GetYarnPrefixAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yarn",
                    Arguments = "global bin",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string bin = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();
            return bin;
        }
        catch
        {
            return "";
        }
    }
}

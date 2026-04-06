using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClawManager.Common.ProcessClass;

/// <summary>
/// 提供在系统PATH 中找可执行文件的工具方法
/// </summary>
public class ExecutableFinder
{
    private ILogger<ExecutableFinder> _logger;
    public ExecutableFinder(ILogger<ExecutableFinder> logger)
    {
        _logger = logger;
    }
    /// <summary>
    /// 在系统的 PATH 环境变量在查找可执行文件
    /// </summary>
    /// <param name="fileName">要查找的文件名（不带拓展名，如“openclaw”）</param>
    /// <returns>找到的完整路径，如未找到返回null</returns>
    public static string? FindInPath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentNullException("文件名不能为空", nameof(fileName));

        //获取 PATH 环境变量（合并系统和用户变量）
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        //StringSplitOptions.RemoveEmptyEntries 分割后去除空字符串
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        //根据操作系统确定可执行文件拓展名规则
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindInPathWindows(fileName, paths);
        }
        else //linux / mac
        {
            return FindInPathUnix(fileName, paths);
        }
    }

    private static string? FindInPathWindows(string fileName, string[] paths)
    {
        //获取 PATHEXT 环境变量，例如.exe;cmd;bat;.com
        string? pathext = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrEmpty(pathext))
        {
            pathext = ".exe;.cmd;.bat;.com";
        }

        var extensions = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            //Directory.Exists()
            //检查指定的目录路径是否存在（返回布尔值）
            //—— 存在返回 true，不存在 / 路径无效返回 false
            if (!Directory.Exists(dir)) continue;

            foreach (var ext in extensions)
            {
                //Path.Combine() 智能拼接多个路径片段，自动处理路径分隔符（\ 或 /）
                string fullPath = Path.Combine(dir, fileName + ext);

                //File.Exists()
                //检查指定的文件路径是否存在真实的文件（返回布尔值）
                //—— 存在返回 true，不存在 / 路径无效 / 是目录而非文件返回 false
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        return null;
    }

    private static string? FindInPathUnix(string fileName, string[] paths)
    {
        foreach (var dir in paths)
        {
            //如果目录不存在
            if (!Directory.Exists(dir)) continue;

            string fullPath = Path.Combine(dir, fileName);

            //检测文件是否有执行权限
            if (File.Exists(fullPath) && IsExecutable(fullPath))
            {
                return fullPath;
            }
        }
        return null;
    }

    private static bool IsExecutable(string filePath)
    {
        // 在 Unix 系统上，可以通过检查文件权限来判断是否可执行
        // 这里使用 Mono.Posix.NETStandard 或简单的 try-catch 方法
        // 简单实现：尝试获取 Unix 文件权限（如果可用）
        try
        {
            var monoPosix = Type.GetType("Mono.Unix.UnixFileSystemInfo, Mono.Posix.NETStandard");
            if (monoPosix != null)
            {
                dynamic? fileInfo = monoPosix.GetMethod("GetFileSystemEntry")?.Invoke(null, new object[] { filePath });
                if (fileInfo != null)
                {
                    // 检查用户、组或其他执行权限
                    var permissions = (int)fileInfo.FileAccessPermissions;
                    return (permissions & 0x49) != 0; // 0x49 = 用户执行 (0x40) | 组执行 (0x08) | 其他执行 (0x01)
                }
            }
        }
        catch
        {
            // 忽略异常，回退到简单存在性检查
        }

        // 回退：如果无法获取权限，至少确保文件存在（不够严谨，但比没有好）
        return File.Exists(filePath);
    }

    /// <summary>
    /// 查找并执行命令
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string command,
        string arguments,
        ILogger? logger = null)
    {
        var executable = FindInPath(command);

        if (executable == null)
        {
            logger?.LogWarning("命令未找到: {Command}", command);
            return new ProcessResult(-1, "", $"命令未找到: {command}");
        }

        return await RunWithPathAsync(executable, arguments, logger);
    }

    /// <summary>
    /// 使用已知路径执行
    /// </summary>
    public static async Task<ProcessResult> RunWithPathAsync(
        string executablePath,
        string arguments,
        ILogger? logger = null)
    {
        logger?.LogDebug("[EXEC] {Path} {Args}", executablePath, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            return new ProcessResult(-1, "", "无法启动进程");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (!string.IsNullOrEmpty(stderr) && stderr.Trim().Length > 0)
        {
            logger?.LogDebug("[STDERR] {Stderr}", stderr.Trim());
        }

        return new ProcessResult(proc.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// 执行并只关心是否成功
    /// </summary>
    public static async Task<bool> RunSilentAsync(string command, string arguments)
    {
        var result = await RunAsync(command, arguments);
        return result.IsSuccess;
    }

    public record ProcessResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool IsSuccess => ExitCode == 0;
    }
}

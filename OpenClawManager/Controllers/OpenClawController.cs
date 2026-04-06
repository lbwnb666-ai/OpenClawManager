using Microsoft.AspNetCore.Mvc;
using OpenClawManager.Core.Services;
using System.IO;
using OpenClawManager.Common.Uninstall;
using OpenClawManager.Common.Install;
using OpenClawManager.Common.ProcessClass;

namespace OpenClawManager.Api.Controllers;

[ApiController]
[Route("api")]
public class OpenClawController : ControllerBase
{
    private readonly SimpleProcessManager _manager;
    private readonly ILogger<OpenClawController> _logger;
    private readonly ILogStreamService _logStream;
    //private static readonly List<string> _logs = new();

    public OpenClawController(
        SimpleProcessManager manger, 
        ILogger<OpenClawController> logger,
        ILogStreamService logStream)
    {
        _manager = manger;
        _logger = logger;
        _logStream = logStream;
    }

    [HttpPost("start")]
    public IActionResult Start([FromQuery] int port = 18789)
    {
        _logger.LogInformation("收到启动请求，端口: {Port}", port);
        if (_manager.IsRunning)
            return BadRequest(new { Error = "正在运行中" });

        var success = _manager.Start(port);

        return Ok(new
        {
            success = success,
            port = port,
            message = success ? "启动成功" : "启动失败"
        });
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        _logger.LogInformation("收到停止请求");
        var success = _manager.Stop();

        return Ok(new
        {
            success = success,
            message = success ? "停止成功" : "停止失败"
        });
    }

    [HttpPost("uninstall")]
    public async Task<IActionResult> Uninstall([FromQuery] bool removeAllData = false)
    {
        var result = await _manager.UninstallAsync(removeWorkspace: removeAllData);

        return result switch
        {
            UninstallResult.Success => Ok(new { success = true, message = "OpenClaw 已成功卸载" }),
            UninstallResult.CliNotFoundButCleaned => Ok(new { success = true, message = "未找到 openclaw 命令，但已清理残留目录。", code = "CLI_NOT_FOUND_BUT_CLEANED" }),
            UninstallResult.CliNotFoundAndCleanupFailed => StatusCode(500, new { success = false, message = "未找到 openclaw 命令，且残留目录清理失败，请检查日志手动清理。" }),
            UninstallResult.OfficialUninstallFailed => StatusCode(500, new { success = false, message = "官方卸载命令执行失败，但已尝试清理残留。请检查日志。" }),
            _ => StatusCode(500, new { success = false, message = "卸载过程中发生部分失败，请检查日志。" })
        };
    }

    [HttpPost("install")]
    public async Task<IActionResult> Install([FromQuery] string? packageManager = null)
    {
        var result = await _manager.InstallAsync(packageManager);
        return result switch
        {
            InstallResult.Success => Ok(new { success = true, message = "OpenClaw 安装成功" }),
            InstallResult.AlreadyInstalled => Ok(new { success = true, message = "OpenClaw 已安装" }),
            InstallResult.NodeJsNotInstalled => BadRequest(new { success = false, error = "Node.js 未安装或版本过低，请安装 Node.js 22 或更高版本" }),
            InstallResult.NodeJsInstallFailed => StatusCode(500, new { success = false, error = "Node.js 安装失败，请手动安装" }),
            InstallResult.PackageManagerNotFound => BadRequest(new { success = false, error = "未找到 npm 或 pnpm，请手动安装" }),
            InstallResult.InstallFailed => StatusCode(500, new { success = false, error = "OpenClaw 安装失败" }),
            _ => StatusCode(500, new { success = false, error = "未知错误" })
        };
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        // 检查是否已安装
        string? openClawPath = ExecutableFinder.FindInPath("openclaw");
        bool isInstalled = !string.IsNullOrEmpty(openClawPath);

        _logger.LogDebug("状态检查: installed={IsInstalled}, running={IsRunning}",
            isInstalled, _manager.IsRunning);

        return Ok(new
        {
            isInstalled = isInstalled,
            isRunning = _manager.IsRunning
        });
    }

    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int last = 50)
    {
        try
        {
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(logPath))
                return Ok(new { Message = "暂无日志" });

            var latestFile = Directory.GetFiles(logPath, "manager-*.log")
                    .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                    .FirstOrDefault();

            if (latestFile == null)
                return Ok(new { Message = "暂无日志" });

            //使用 FileStream 允许共享读取
            using var fileStream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var lines = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }

            // 取最后 last 行
            var lastLines = lines.Skip(Math.Max(0, lines.Count - last)).ToList();
            return Ok(lines);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Message = "读取日志时发生错误", Error = ex.Message });
        }
    }

    [HttpGet("logs/stream")]
    public Task GetLogsStream()
    {
        return _logStream.StreamLogsAsync(Response, HttpContext.RequestAborted);
    }

}

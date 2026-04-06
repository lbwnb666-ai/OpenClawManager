using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenClawManager.Core.Services;

public class LogStreamService : ILogStreamService
{
    private readonly LogTrackerService _tracker;
    private readonly ILogger<LogStreamService> _logger;

    public LogStreamService(LogTrackerService tracker, ILogger<LogStreamService> logger)
    {
        _tracker = tracker;
        _logger = logger;
    }

    public async Task StreamLogsAsync(HttpResponse response, CancellationToken ct)
    {
        SetupSseHeaders(response);

        var logDir = GetLogDirectory();
        if (logDir == null)
        {
            await SendEventAsync(response, "system", "暂无日志", ct);
            return;
        }

        var currentFile = GetLatestLogFile(logDir);
        if (currentFile == null)
        {
            await SendEventAsync(response, "system", "暂无日志", ct);
            return;
        }

        await StreamFileAsync(response, currentFile, logDir, ct);
    }

    private void SetupSseHeaders(HttpResponse response)
    {
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
    }

    private string? GetLogDirectory()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        return Directory.Exists(path) ? path : null;
    }

    private string? GetLatestLogFile(string logDir)
    {
        return Directory.GetFiles(logDir, "manager-*.log")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .FirstOrDefault();
    }

    private async Task StreamFileAsync(
        HttpResponse response,
        string initialFile,
        string logDir,
        CancellationToken ct)
    {
        var currentFile = initialFile;

        // 发送历史
        await SendHistoryAsync(response, currentFile, 50, ct);

        // 实时监听
        while (!ct.IsCancellationRequested)
        {
            var latest = GetLatestLogFile(logDir);

            if (latest != currentFile && latest != null)
            {
                await SendEventAsync(response, "rotate",
                    $"新日志: {Path.GetFileName(latest)}", ct);
                currentFile = latest;
                _tracker.Reset(currentFile);
            }

            if (File.Exists(currentFile))
            {
                await SendNewLinesAsync(response, currentFile, ct);
            }

            await Task.Delay(1000, ct);
        }
    }

    private async Task SendHistoryAsync(
        HttpResponse response,
        string file,
        int count,
        CancellationToken ct)
    {
        var lines = await ReadAllLinesAsync(file, ct);
        var lastLines = lines.TakeLast(count);

        foreach (var line in lastLines)
        {
            await SendEventAsync(response, "history", line, ct);
        }

        _tracker.Update(file, new FileInfo(file).Length);
    }

    private async Task SendNewLinesAsync(
        HttpResponse response,
        string file,
        CancellationToken ct)
    {
        var position = _tracker.GetPosition(file);
        var length = new FileInfo(file).Length;

        if (position > length) position = 0; // 文件被清空

        using var stream = new FileStream(file, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite);

        stream.Position = position;

        using var reader = new StreamReader(stream);
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            await SendEventAsync(response, "log", line, ct);
        }

        _tracker.Update(file, stream.Position);
    }

    private async Task SendEventAsync(
        HttpResponse response,
        string eventType,
        string data,
        CancellationToken ct)
    {
        var payload = $"event: {eventType}\ndata: {Escape(data)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(payload);

        await response.Body.WriteAsync(bytes, ct);
        await response.Body.FlushAsync(ct);
    }

    private string Escape(string data) =>
        data.Replace("\n", "\\n").Replace("\r", "");

    private async Task<List<string>> ReadAllLinesAsync(
        string file,
        CancellationToken ct)
    {
        using var stream = new FileStream(file, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            lines.Add(line);
        }

        return lines;
    }
}

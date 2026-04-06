using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenClawManager.Common.ProcessClass;

namespace OpenClawManager.Api.Services;

public class AutoLaunchService : IHostedService
{
    private readonly ILogger<AutoLaunchService> _logger;
    private readonly IConfiguration _config;
    private readonly IHostApplicationLifetime _lifetime;

    public AutoLaunchService(
        ILogger<AutoLaunchService> logger,
        IConfiguration config,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _config = config;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 等待应用完全启动
        _lifetime.ApplicationStarted.Register(async () =>
        {
            await Task.Delay(2000, cancellationToken); // 确保服务就绪

            var port = _config.GetValue<int>("Urls:Http", 5000);
            var url = $"http://localhost:{port}";

            _logger.LogInformation("正在打开浏览器: {Url}", url);

            if (BrowserLauncher.Open(url))
            {
                _logger.LogInformation("浏览器已打开");
            }
            else
            {
                _logger.LogWarning("自动打开浏览器失败，请手动访问: {Url}", url);
            }
        });

        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
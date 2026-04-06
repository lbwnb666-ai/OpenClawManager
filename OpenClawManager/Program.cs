using Serilog;
using OpenClawManager.Core.Services;
using Serilog.Events;
using OpenClawManager.Common.Uninstall;
using OpenClawManager.Common.Install;
using OpenClawManager.Common.ProcessClass;
using OpenClawManager.Api.Services;


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // 过滤 ASP.NET Core 内部日志
    .WriteTo.Console()
    .WriteTo.File("logs/manager-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    //将 Serilog 添加到日志提供程序
    builder.Logging.ClearProviders();              // 可选：清除默认提供程序
    builder.Logging.AddSerilog(Log.Logger);        // 添加 Serilog

    builder.Services.AddControllers();

    //添加swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "OpenClawManager API",
                Version = "v1",

            });
        });


    builder.Services.AddSingleton<SimpleProcessManager>();
    builder.Services.AddSingleton<OpenClawUninstaller>();
    builder.Services.AddSingleton<OpenClawInstall>();
    builder.Services.AddHostedService<AutoLaunchService>();
    builder.Services.AddSingleton<LogTrackerService>();
    builder.Services.AddSingleton<ILogStreamService, LogStreamService>();

    builder.Services.AddCors(o => o.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    // 静态文件（前端）
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenClawManager API V1");
        });
    }

    app.UseCors("AllowAll");
    app.MapControllers();
    // 启动后自动打开浏览器
    var port = app.Configuration.GetValue<int>("Urls:Http", 5000);
    var url = $"http://localhost:{port}";

    Log.Information("OpenClaw Manager启动中");
    app.Run(); 
}
catch (Exception ex)
{
    Log.Fatal(ex, "程序异常终止");
}
finally
{ 
    Log.CloseAndFlush();
}
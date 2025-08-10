using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.V2.Extra;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;

var builder = WebApplication.CreateBuilder(args);

DI.RegisterService(builder);

builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            policy.WithOrigins("https://*.feishupkg.com",
                    "http://localhost:8080",
                    "tauri://localhost",
                    "https://tauri.localhost")
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});
builder.Services.AddHangfire(config =>
    config.UseInMemoryStorage().UseFilter(new AutomaticRetryAttribute() { Attempts = 2 })); //Hangfire如果因为异常重启，最多只重试一次
builder.Services.AddHangfireServer(options =>
{
    // 设置并发数量
    options.WorkerCount = 30;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 自动创建表结构
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();
    dbContext.Database.EnsureCreated();
}

app.UseHangfireDashboard("/api/ai/hangfire", new DashboardOptions()
{
    Authorization = new[] {new HangfireAuthorizationFilter()}, IsReadOnlyFunc = (context) => true
});

app.UseWebSockets();

app.MapControllers();
app.UseCors();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "StaticFiles")),
    RequestPath = "/api/ai/static"
});

//设置Hangfire Job
RecurringJob.AddOrUpdate("CleanAutomationBrowser", () => AutomationHelper.CleanCache(), Cron.Hourly);

app.Run();

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using ServerTest.Middleware;
using ServerTest.Options;
using ServerTest.RateLimit;

namespace ServerTest.Startup
{
    /// <summary>
    /// 系统启动管道配置，保持 Program.cs 简洁
    /// </summary>
    public static class SystemPipeline
    {
        public static void Configure(WebApplication app, ServerTest.Options.WebSocketOptions wsConfig)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseMiddleware<ExceptionHandlingMiddleware>();

            // 重要：系统就绪检查必须在其他中间件之前
            app.UseMiddleware<SystemReadinessMiddleware>();

            // 开发环境：保持 HTTP，避免预检重定向
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<HttpRateLimitMiddleware>();

            // WebSocket 配置
            app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(wsConfig.KeepAliveSeconds)
            });
        }
    }
}

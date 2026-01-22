using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Application.Services;
using ServerTest.Infrastructure.Db;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Middleware;
using ServerTest.Models;
using ServerTest.Monitoring;
using ServerTest.Options;
using ServerTest.RateLimit;
using ServerTest.Services;
using ServerTest.Strategy;
using ServerTest.WebSockets;
using StackExchange.Redis;
using System.Text.Json;
using AspNetWebSocketOptions = Microsoft.AspNetCore.Builder.WebSocketOptions;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 第一阶段：基础服务注册
// ============================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis 缓存配置
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "ServerTest:";
});

// 日志配置
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Startup monitor window (WinForms)
builder.Services.AddSingleton<StartupMonitorHost>();
builder.Services.AddSingleton<ILoggerProvider, StartupMonitorLoggerProvider>();

// CORS 配置
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ============================================================================
// 第二阶段：系统启动管理器（必须最先注册）
// ============================================================================
builder.Services.AddSingleton<SystemStartupManager>();

// ============================================================================
// 第三阶段：基础设施服务注册
// ============================================================================
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<RedisCacheService>();
builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<VerificationCodeService>();
builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddDbInfrastructure(builder.Configuration);
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<AccountService>();
builder.Services.Configure<HistoricalMarketDataOptions>(builder.Configuration.GetSection("HistoricalData"));
builder.Services.AddSingleton<HistoricalMarketDataCache>();
builder.Services.AddSingleton<HistoricalMarketDataSyncService>();
builder.Services.AddSingleton<BinanceHistoricalDataDownloader>();
builder.Services.AddHostedService<HistoricalMarketDataSyncHostedService>();

// Redis 连接（用于速率限制和连接管理）
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "127.0.0.1:6379"));
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
builder.Services.AddSingleton<IConnectionManager, RedisConnectionManager>();

// ============================================================================
// 第四阶段：实盘交易系统服务注册
// ============================================================================
// 行情数据引擎
builder.Services.AddSingleton<MarketDataEngine>();

// 价格服务
builder.Services.AddSingleton<ExchangePriceService>();

// 指标引擎
builder.Services.AddSingleton<IndicatorEngine>();

// 条件评估相关
builder.Services.AddSingleton<ConditionCacheService>();
builder.Services.AddSingleton<ConditionEvaluator>();
builder.Services.AddSingleton<ConditionUsageTracker>();

// 策略执行相关
builder.Services.AddSingleton<StrategyActionTaskQueue>();
builder.Services.AddSingleton<IStrategyValueResolver, IndicatorValueResolver>();
builder.Services.AddSingleton<IStrategyActionExecutor, QueuedStrategyActionExecutor>();
builder.Services.AddSingleton<StrategyJsonLoader>();
builder.Services.AddSingleton<RealTimeStrategyEngine>();

// 策略运行时服务（后台服务）
builder.Services.AddHostedService<StrategyRuntimeHostedService>();

// ============================================================================
// 第五阶段：网络层服务注册
// ============================================================================
builder.Services.AddScoped<WebSocketHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.HealthWsHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.AccountProfileUpdateHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.MarketSubscribeHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.MarketUnsubscribeHandler>();
builder.Services.AddSingleton<ServerTest.WebSockets.Subscriptions.IMarketSubscriptionStore, ServerTest.WebSockets.Subscriptions.InMemoryMarketSubscriptionStore>();
builder.Services.AddHostedService<MarketTickerBroadcastService>();

// 配置选项
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
builder.Services.Configure<Microsoft.AspNetCore.Builder.WebSocketOptions>(builder.Configuration.GetSection("WebSocket"));

// ============================================================================
// 构建应用
// ============================================================================
var app = builder.Build();

// ============================================================================
// 第六阶段：系统启动流程
// ============================================================================
var startupManager = app.Services.GetRequiredService<SystemStartupManager>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var startupMonitorHost = app.Services.GetRequiredService<StartupMonitorHost>();
startupMonitorHost.Start(startupManager);
var wsConfig = app.Services.GetRequiredService<IOptions<ServerTest.Options.WebSocketOptions>>().Value;

logger.LogInformation("");
logger.LogInformation("╔═══════════════════════════════════════════════════════════╗");
logger.LogInformation("║          DWQuant 量化交易系统启动流程                    ║");
logger.LogInformation("╚═══════════════════════════════════════════════════════════╝");
logger.LogInformation("");

try
{
    // ========================================================================
    // 步骤 1：启动基础设施
    // ========================================================================
    startupManager.MarkStarting(SystemModule.Infrastructure, "Redis、数据库等基础设施");

    // 测试 Redis 连接
    var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();
    try
    {
        await db.StringSetAsync("__startup_test__", "ok", TimeSpan.FromSeconds(1));
        var testValue = await db.StringGetAsync("__startup_test__");
        if (testValue == "ok")
        {
            startupManager.MarkReady(SystemModule.Infrastructure, "Redis 连接正常");
        }
        else
        {
            throw new Exception("Redis 测试失败");
        }
    }
    catch (Exception ex)
    {
        startupManager.MarkFailed(SystemModule.Infrastructure, $"Redis 连接失败: {ex.Message}");
        throw;
    }

    // ========================================================================
    // 步骤 2：启动行情数据引擎
    // ========================================================================
    startupManager.MarkStarting(SystemModule.MarketDataEngine, "行情数据引擎（WebSocket 订阅）");

    var marketDataEngine = app.Services.GetRequiredService<MarketDataEngine>();

    // 等待行情引擎初始化完成（带超时）
    var marketDataTimeout = TimeSpan.FromMinutes(2);
    logger.LogInformation("等待行情引擎初始化（超时时间: {Timeout}秒）...", marketDataTimeout.TotalSeconds);

    try
    {
        await marketDataEngine.WaitForInitializationAsync();
        startupManager.MarkReady(SystemModule.MarketDataEngine, "行情数据引擎已就绪");
    }
    catch (Exception ex)
    {
        startupManager.MarkFailed(SystemModule.MarketDataEngine, $"行情引擎初始化失败: {ex.Message}");
        logger.LogError(ex, "行情引擎初始化失败");
        throw;
    }

    // ========================================================================
    // 步骤 3：启动指标引擎
    // ========================================================================
    startupManager.MarkStarting(SystemModule.IndicatorEngine, "指标计算引擎");

    var indicatorEngine = app.Services.GetRequiredService<IndicatorEngine>();
    // 指标引擎在 StrategyRuntimeHostedService 中启动，这里只标记
    startupManager.MarkReady(SystemModule.IndicatorEngine, "指标引擎已注册");

    // ========================================================================
    // 步骤 4：启动策略引擎
    // ========================================================================
    startupManager.MarkStarting(SystemModule.StrategyEngine, "实时策略执行引擎");

    var strategyEngine = app.Services.GetRequiredService<RealTimeStrategyEngine>();
    // 策略引擎在 StrategyRuntimeHostedService 中启动，这里只标记
    startupManager.MarkReady(SystemModule.StrategyEngine, "策略引擎已注册");

    // ========================================================================
    // 步骤 5：启动实盘交易系统（整体）
    // ========================================================================
    startupManager.MarkStarting(SystemModule.TradingSystem, "实盘交易系统（行情+指标+策略）");

    // 等待策略运行时服务启动（通过检查策略引擎是否有策略注册来判断）
    logger.LogInformation("等待策略运行时服务启动...");
    await Task.Delay(2000); // 给 StrategyRuntimeHostedService 一些启动时间

    startupManager.MarkReady(SystemModule.TradingSystem, "实盘交易系统已就绪");

    // ========================================================================
    // 步骤 6：启动网络层
    // ========================================================================
    startupManager.MarkStarting(SystemModule.Network, "网络层（HTTP API + WebSocket）");

    // HTTP 管道配置
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // ⚠️ 重要：系统就绪检查中间件必须在其他中间件之前
    app.UseMiddleware<SystemReadinessMiddleware>();

    // Dev: keep HTTP only to avoid preflight redirect.
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<HttpRateLimitMiddleware>();

    // WebSocket 配置
    app.UseWebSockets(new AspNetWebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(wsConfig.KeepAliveSeconds)
    });

    startupManager.MarkReady(SystemModule.Network, "网络层已就绪");

    // ========================================================================
    // 启动完成，打印状态摘要
    // ========================================================================
    startupManager.PrintStatusSummary();

    logger.LogInformation("╔═══════════════════════════════════════════════════════════╗");
    logger.LogInformation("║          ✅ 系统启动完成，开始监听请求                    ║");
    logger.LogInformation("╚═══════════════════════════════════════════════════════════╝");
    logger.LogInformation("");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ 系统启动失败，应用将退出");
    startupManager.PrintStatusSummary();
    throw;
}

// ============================================================================
// 第七阶段：路由配置
// ============================================================================
// WebSocket 路由
app.Map(wsConfig.Path, wsApp =>
{
    wsApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "bad_request", "WebSocket request required");
            return;
        }

        var system = context.Request.Query["system"].ToString();
        if (string.IsNullOrWhiteSpace(system))
        {
            logger.LogWarning("WS missing system parameter");
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "bad_request", "Missing system");
            return;
        }

        var token = GetWebSocketToken(context);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("WS missing token");
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "Missing token");
            return;
        }

        var tokenService = context.RequestServices.GetRequiredService<AuthTokenService>();
        var tokenValidation = await tokenService.ValidateTokenAsync(token);
        if (!tokenValidation.IsValid)
        {
            logger.LogWarning("WS invalid token");
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "Invalid token");
            return;
        }

        var userId = tokenValidation.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("WS missing user id claim");
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "forbidden", "Missing user id");
            return;
        }

        var connectionManager = context.RequestServices.GetRequiredService<IConnectionManager>();
        var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        var connectionId = Guid.NewGuid();
        if (!connectionManager.TryReserve(userId, system, connectionId))
        {
            var kicked = false;
            var reserved = false;
            if (string.Equals(wsConfig.KickPolicy, "KickOld", StringComparison.OrdinalIgnoreCase))
            {
                var existing = connectionManager.GetConnections(userId)
                    .Where(c => string.Equals(c.System, system, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var connectionItem in existing)
                {
                    await handler.KickAsync(connectionItem, "replaced", context.RequestAborted);
                    connectionManager.Remove(connectionItem.UserId, connectionItem.System, connectionItem.ConnectionId);
                    kicked = true;
                }

                if (!kicked)
                {
                    connectionManager.ClearUserSystem(userId, system);
                    reserved = connectionManager.TryReserve(userId, system, connectionId);
                    kicked = reserved;
                }
                else
                {
                    reserved = connectionManager.TryReserve(userId, system, connectionId);
                }
            }

            if (!kicked || !reserved)
            {
                logger.LogWarning("WS connection limit reached for user {UserId} system {System}", userId, system);
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "connection_limit", "Too many connections for this system");
                return;
            }
        }

        WebSocketConnection connection;
        try
        {
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            connection = new WebSocketConnection(connectionId, userId, system, socket, DateTime.UtcNow, remoteIp);
            connectionManager.RegisterLocal(connection);
        }
        catch
        {
            connectionManager.Remove(userId, system, connectionId);
            throw;
        }

        logger.LogInformation("WS connected: user {UserId} system {System} connection {ConnectionId}", userId, system, connection.ConnectionId);

        try
        {
            await handler.HandleAsync(connection, context.RequestAborted);
        }
        finally
        {
            logger.LogInformation("WS disconnected: user {UserId} system {System} connection {ConnectionId}", userId, system, connection.ConnectionId);
        }
    });
});

// HTTP API 路由
app.MapControllers();

// ============================================================================
// 第八阶段：启动 HTTP 服务器
// ============================================================================
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("🌐 HTTP 服务器启动");
startupLogger.LogInformation("📍 监听地址: http://localhost:9635");
startupLogger.LogInformation("📖 Swagger UI: http://localhost:9635/swagger");
startupLogger.LogInformation("❤️  健康检查: http://localhost:9635/api/health");
startupLogger.LogInformation("");

app.Run();

static string? GetWebSocketToken(HttpContext context)
{
    var token = context.Request.Query["access_token"].ToString();
    if (!string.IsNullOrWhiteSpace(token))
    {
        return token;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return authorization.Substring(prefix.Length).Trim();
    }

    return null;
}

static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json";

    var payload = ErrorResponse.Create(code, message, context.TraceIdentifier);
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    await context.Response.WriteAsync(json);
}

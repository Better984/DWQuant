using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.StrategyManagement.Application;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.Positions.Application;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.MarketStreaming.Infrastructure;
using ServerTest.Modules.AdminBroadcast.Application;
using ServerTest.Infrastructure.Db;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Middleware;
using ServerTest.Models;
using ServerTest.Modules.Monitoring.Application;
using ServerTest.Modules.Monitoring.Infrastructure;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Options;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Modules.Notifications.Infrastructure.Delivery;
using ServerTest.RateLimit;
using ServerTest.Services;
using ServerTest.Modules.StrategyEngine.Application.RunChecks;
using ServerTest.Modules.StrategyEngine.Application.RunChecks.Checks;
using ServerTest.Modules.StrategyEngine.Domain;
using ServerTest.WebSockets;
using StackExchange.Redis;
using System.Text;
using NotificationUserNotifyChannelRepository = ServerTest.Modules.Notifications.Infrastructure.UserNotifyChannelRepository;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.StrategyRuntime.Application;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Modules.TradingExecution.Application;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Modules.TradingExecution.Infrastructure;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Startup;
using ServerTest.Protocol;

// ============================================================================
// 编码设置：确保控制台和日志输出使用 UTF-8 编码，避免中文乱码
// ============================================================================
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "127.0.0.1:6379";
var redisMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString);

// ============================================================================
// 第一阶段：基础服务注册
// ============================================================================
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ServerTest.Protocol.ProtocolRequestFilter>();
    options.Filters.Add<ServerTest.Protocol.ProtocolResponseFilter>();
})
    .AddJsonOptions(options =>
    {
        ProtocolJson.Apply(options.JsonSerializerOptions);
    });
builder.Services.AddScoped<ServerTest.Protocol.ProtocolRequestFilter>();
builder.Services.AddScoped<ServerTest.Protocol.ProtocolResponseFilter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis 缓存配置
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.InstanceName = "ServerTest:";
    options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redisMultiplexer);
});

// 日志配置
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 启动监控窗口（WinForms）
builder.Services.AddSingleton<StartupMonitorHost>();
builder.Services.AddSingleton<ILoggerProvider, StartupMonitorLoggerProvider>();
builder.Services.AddSingleton<MarketDataMaintenanceRepository>();

// CORS 配置：按配置文件限制来源，区分环境
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (isDevelopment)
        {
            if (corsOrigins.Length == 0)
            {
                // 开发环境默认放行，方便联调
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
                return;
            }
        }

        if (corsOrigins.Length == 0)
        {
            // 生产环境未配置白名单时拒绝所有来源
            policy.SetIsOriginAllowed(_ => false);
            return;
        }

        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ============================================================================
// 第二阶段：系统启动管理器（必须最先注册）
// ============================================================================
builder.Services.AddSingleton<SystemStartupManager>();
builder.Services.AddSingleton<SystemStartupWorkflow>();

// ============================================================================
// 第三阶段：基础设施服务注册
// ============================================================================
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<RedisCacheService>();
builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<VerificationCodeService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notification"));
builder.Services.AddSingleton<NotificationRepository>();
builder.Services.AddSingleton<NotificationPreferenceRepository>();
builder.Services.AddSingleton<NotificationAccountRepository>();
builder.Services.AddSingleton<NotificationUserNotifyChannelRepository>();
builder.Services.AddSingleton<INotificationTemplateRenderer, NotificationTemplateRenderer>();
builder.Services.AddSingleton<INotificationPreferenceResolver, NotificationPreferenceResolver>();
builder.Services.AddSingleton<NotificationPreferenceService>();
builder.Services.AddSingleton<INotificationPublisher, NotificationPublisher>();
builder.Services.AddSingleton<INotificationSender, EmailNotificationSender>();
builder.Services.AddSingleton<INotificationSender, DingTalkNotificationSender>();
builder.Services.AddSingleton<INotificationSender, WeComNotificationSender>();
builder.Services.AddSingleton<INotificationSender, TelegramNotificationSender>();
builder.Services.AddHostedService<NotificationDeliveryWorker>();
builder.Services.AddDbInfrastructure(builder.Configuration);
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AdminBroadcastService>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddScoped<StrategyRepository>();
builder.Services.AddSingleton<OSSService>();
builder.Services.Configure<HistoricalMarketDataOptions>(builder.Configuration.GetSection("HistoricalData"));
builder.Services.AddSingleton<HistoricalMarketDataCache>();
builder.Services.AddSingleton<HistoricalMarketDataRepository>();
builder.Services.AddSingleton<HistoricalMarketDataSyncService>();
builder.Services.AddSingleton<BinanceHistoricalDataDownloader>();
builder.Services.AddHostedService<HistoricalMarketDataSyncHostedService>();

// Redis 连接（用于速率限制和连接管理）
builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
builder.Services.AddSingleton<IConnectionManager, RedisConnectionManager>();
builder.Services.AddSingleton<WebSocketNodeId>();
builder.Services.AddHostedService<RedisConnectionKickSubscriber>();

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
builder.Services.AddSingleton<StrategyEngineRunLogQueue>();
builder.Services.AddSingleton<StrategyEngineRunLogRepository>();
builder.Services.AddSingleton<RealTimeStrategyEngine>();
builder.Services.AddSingleton<StrategyPositionRepository>();
builder.Services.AddSingleton<StrategyPositionCloseService>();
builder.Services.AddSingleton<UserExchangeApiKeyRepository>();
builder.Services.AddSingleton<StrategyRuntimeRepository>();
builder.Services.AddSingleton<StrategyRuntimeLoader>();
builder.Services.AddSingleton<StrategyOwnershipService>();
builder.Services.AddSingleton<StrategyRunCheckLogRepository>();
builder.Services.AddSingleton<StrategyRunCheckService>();
builder.Services.AddSingleton<IStrategyRunCheck, ExchangeReadyRunCheck>();
builder.Services.AddSingleton<IStrategyRunCheck, ApiKeyRunCheck>();
builder.Services.AddSingleton<IStrategyRunCheck, PositionModeRunCheck>();
builder.Services.AddSingleton<IStrategyRunCheck, BalanceRunCheck>();
builder.Services.AddSingleton<PositionRiskConfigStore>();
builder.Services.AddSingleton<IOrderExecutor, CcxtOrderExecutor>();
builder.Services.AddHostedService<StrategyRuntimeBootstrapHostedService>();
builder.Services.AddHostedService<TradeActionConsumer>();
builder.Services.AddHostedService<PositionRiskEngine>();
builder.Services.AddHostedService<StrategyEngineRunLogWriter>();
builder.Services.AddHostedService<ConditionCacheCleanupHostedService>();

// 策略运行时服务（后台服务）
builder.Services.AddHostedService<StrategyRuntimeHostedService>();
builder.Services.AddHostedService<StrategyRuntimeLeaseHostedService>();

// ============================================================================
// 第五阶段：网络层服务注册
// ============================================================================
builder.Services.AddScoped<WebSocketHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.HealthWsHandler>();
builder.Services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.AccountProfileUpdateHandler>();
builder.Services.AddScoped<IWsMessageHandler, MarketSubscribeHandler>();
builder.Services.AddScoped<IWsMessageHandler, MarketUnsubscribeHandler>();
builder.Services.AddSingleton<IMarketSubscriptionStore, RedisMarketSubscriptionStore>();
builder.Services.AddSingleton<MarketTickerBroadcastService>();
builder.Services.AddHostedService<MarketTickerBroadcastService>(sp => sp.GetRequiredService<MarketTickerBroadcastService>());
builder.Services.AddHostedService<KlineCloseListenerService>();

// 配置选项
builder.Services.AddSingleton<IValidateOptions<BusinessRulesOptions>, BusinessRulesOptionsValidator>();
builder.Services.AddOptions<BusinessRulesOptions>()
    .Bind(builder.Configuration.GetSection("BusinessRules"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<RuntimeQueueOptions>, RuntimeQueueOptionsValidator>();
builder.Services.AddOptions<RuntimeQueueOptions>()
    .Bind(builder.Configuration.GetSection("RuntimeQueue"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<StrategyOwnershipOptions>, StrategyOwnershipOptionsValidator>();
builder.Services.AddOptions<StrategyOwnershipOptions>()
    .Bind(builder.Configuration.GetSection("StrategyOwnership"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<MarketDataQueryOptions>, MarketDataQueryOptionsValidator>();
builder.Services.AddOptions<MarketDataQueryOptions>()
    .Bind(builder.Configuration.GetSection("MarketDataQuery"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<ConditionCacheOptions>, ConditionCacheOptionsValidator>();
builder.Services.AddOptions<ConditionCacheOptions>()
    .Bind(builder.Configuration.GetSection("ConditionCache"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<RedisKeyOptions>, RedisKeyOptionsValidator>();
builder.Services.AddOptions<RedisKeyOptions>()
    .Bind(builder.Configuration.GetSection("RedisKey"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<MonitoringOptions>, MonitoringOptionsValidator>();
builder.Services.AddOptions<MonitoringOptions>()
    .Bind(builder.Configuration.GetSection("Monitoring"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<RequestLimitsOptions>, RequestLimitsOptionsValidator>();
builder.Services.AddOptions<RequestLimitsOptions>()
    .Bind(builder.Configuration.GetSection("RequestLimits"))
    .ValidateOnStart();

builder.Services.AddOptions<StartupOptions>()
    .Bind(builder.Configuration.GetSection("Startup"))
    .Validate(options => options.MarketDataInitTimeoutSeconds > 0, "Startup:MarketDataInitTimeoutSeconds 必须大于 0")
    .Validate(options => options.StrategyRuntimeWarmupSeconds >= 0, "Startup:StrategyRuntimeWarmupSeconds 不能小于 0")
    .ValidateOnStart();

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
builder.Services.Configure<Microsoft.AspNetCore.Builder.WebSocketOptions>(builder.Configuration.GetSection("WebSocket"));
builder.Services.Configure<TradingOptions>(builder.Configuration.GetSection("Trading"));

// ============================================================================
// 构建应用
// ============================================================================
var app = builder.Build();

// ============================================================================
// 第六阶段：系统启动流程
// ============================================================================
var wsConfig = app.Services.GetRequiredService<IOptions<ServerTest.Options.WebSocketOptions>>().Value;
var startupWorkflow = app.Services.GetRequiredService<SystemStartupWorkflow>();
await startupWorkflow.RunAsync(app, wsConfig, SystemPipeline.Configure);

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
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "bad_request", "需要 WebSocket 请求");
            return;
        }

        var system = context.Request.Query["system"].ToString();
        if (string.IsNullOrWhiteSpace(system))
        {
            logger.LogWarning("WebSocket 缺少 system 参数");
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "bad_request", "缺少 system 参数");
            return;
        }

        var token = GetWebSocketToken(context);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("WebSocket 缺少 token");
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "缺少 token");
            return;
        }

        var tokenService = context.RequestServices.GetRequiredService<AuthTokenService>();
        var tokenValidation = await tokenService.ValidateTokenAsync(token);
        if (!tokenValidation.IsValid)
        {
            logger.LogWarning("WebSocket token 无效");
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "token 无效");
            return;
        }

        var userId = tokenValidation.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("WebSocket 缺少用户ID声明");
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "forbidden", "缺少用户ID声明");
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

                reserved = connectionManager.TryReserve(userId, system, connectionId);
                if (!reserved)
                {
                    connectionManager.BroadcastKick(userId, system, "replaced");
                    connectionManager.ClearUserSystem(userId, system);
                    reserved = connectionManager.TryReserve(userId, system, connectionId);
                }

                kicked = reserved;
            }

            if (!kicked || !reserved)
            {
                logger.LogWarning("WebSocket 连接数达到上限: 用户 {UserId} 系统 {System}", userId, system);
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "connection_limit", "该系统连接数已达上限");
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

        logger.LogInformation("WebSocket 已连接: 用户 {UserId} 系统 {System} 连接 {ConnectionId}", userId, system, connection.ConnectionId);

        try
        {
            await handler.HandleAsync(connection, context.RequestAborted);
        }
        finally
        {
            logger.LogInformation("WebSocket 已断开: 用户 {UserId} 系统 {System} 连接 {ConnectionId}", userId, system, connection.ConnectionId);
        }
    });
});

// 管理端 SignalR 路由
// HTTP API 路由
app.MapControllers();

// ============================================================================
// 第八阶段：启动 HTTP 服务
// ============================================================================
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("HTTP 服务已启动");
startupLogger.LogInformation("监听地址: http://localhost:9635");
startupLogger.LogInformation("Swagger UI: http://localhost:9635/swagger");
startupLogger.LogInformation("健康检查: http://localhost:9635/api/health/get");
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

    var errorCode = MapWebSocketErrorCode(code, statusCode);
    var payload = ProtocolEnvelopeFactory.Error(null, errorCode, message, null, context.TraceIdentifier);
    var json = ProtocolJson.Serialize(payload);

    await context.Response.WriteAsync(json);
}

static int MapWebSocketErrorCode(string code, int statusCode)
{
    return code switch
    {
        "bad_request" => ProtocolErrorCodes.InvalidRequest,
        "unauthorized" => ProtocolErrorCodes.Unauthorized,
        "forbidden" => ProtocolErrorCodes.Forbidden,
        "connection_limit" => ProtocolErrorCodes.LimitExceeded,
        _ => statusCode switch
        {
            StatusCodes.Status401Unauthorized => ProtocolErrorCodes.Unauthorized,
            StatusCodes.Status403Forbidden => ProtocolErrorCodes.Forbidden,
            StatusCodes.Status429TooManyRequests => ProtocolErrorCodes.RateLimited,
            StatusCodes.Status503ServiceUnavailable => ProtocolErrorCodes.ServiceUnavailable,
            _ => ProtocolErrorCodes.InternalError
        }
    };
}

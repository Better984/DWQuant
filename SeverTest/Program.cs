using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Config;
using ServerTest.Infrastructure.Db;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Middleware;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.AdminBroadcast.Application;
using ServerTest.Modules.AdminBroadcast.Infrastructure;
using ServerTest.Modules.Backtest.Application;
using ServerTest.Modules.Backtest.Infrastructure;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.MarketStreaming.Infrastructure;
using ServerTest.Modules.Monitoring.Application;
using ServerTest.Modules.Monitoring.Infrastructure;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Modules.Notifications.Infrastructure.Delivery;
using ServerTest.Modules.Positions.Application;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyEngine.Application.RunChecks;
using ServerTest.Modules.StrategyEngine.Application.RunChecks.Checks;
using ServerTest.Modules.StrategyEngine.Domain;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Modules.StrategyManagement.Application;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Modules.StrategyRuntime.Application;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Modules.TradingExecution.Application;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Modules.TradingExecution.Infrastructure;
using ServerTest.Protocol;
using ServerTest.RateLimit;
using ServerTest.Services;
using ServerTest.WebSockets;
using ServerTest.Options;
using StackExchange.Redis;
using System.Text.Json;
using System.Net.WebSockets;
using AppWebSocketOptions = ServerTest.Options.WebSocketOptions;

var roleSelection = ServerRoleBootstrap.Resolve(args, Directory.GetCurrentDirectory(), ServerRoleHelper.DefaultPromptSeconds);
var selectedRole = roleSelection.Role;

var builder = WebApplication.CreateBuilder(args);

ConfigureServerConfigProvider(builder);
ApplyRoleSelectionOverrides(builder, roleSelection);
ConfigureLogging(builder.Logging);
ConfigureOptions(builder.Services, builder.Configuration);

var enableHttpApi = selectedRole == ServerRole.Core || selectedRole == ServerRole.Full;
var enableUserWebSocket = enableHttpApi;
var enableWorkerGateway = selectedRole == ServerRole.Core || selectedRole == ServerRole.Full;

RegisterCommonServices(builder.Services, builder.Configuration, roleSelection, enableHttpApi);
RegisterRoleServices(builder.Services, builder.Configuration, selectedRole);

var app = builder.Build();

var startupManager = app.Services.GetRequiredService<SystemStartupManager>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var wsConfig = app.Services.GetRequiredService<IOptions<AppWebSocketOptions>>().Value;
var roleRuntime = app.Services.GetRequiredService<ServerRoleRuntime>();

logger.LogInformation("服务器角色: {Role}({RoleValue}), 来源={Source}, 角色文件={RoleFile}",
    ServerRoleHelper.ToDisplayName(roleRuntime.Role),
    ServerRoleHelper.ToValue(roleRuntime.Role),
    roleRuntime.Source,
    roleRuntime.RoleFilePath);

var monitorHost = app.Services.GetService<StartupMonitorHost>();
monitorHost?.Start(startupManager);

await RunStartupWorkflowAsync(app, selectedRole, enableHttpApi, enableUserWebSocket || enableWorkerGateway, logger);

if (enableUserWebSocket)
{
    MapUserWebSocket(app, wsConfig);
}

if (enableWorkerGateway)
{
    app.Map("/ws/worker", wsApp =>
    {
        wsApp.Run(async context =>
        {
            var gateway = context.RequestServices.GetRequiredService<BacktestWorkerGateway>();
            await gateway.HandleAsync(context).ConfigureAwait(false);
        });
    });
}

if (enableHttpApi)
{
    app.MapControllers();
}
else
{
    app.MapGet("/api/health", (SystemStartupManager manager, ServerRoleRuntime runtime) =>
    {
        return Results.Json(new
        {
            status = "healthy",
            role = ServerRoleHelper.ToValue(runtime.Role),
            roleName = ServerRoleHelper.ToDisplayName(runtime.Role),
            systems = manager.GetAllStatuses().ToDictionary(
                x => x.Key.ToString(),
                x => new
                {
                    status = x.Value.Status.ToString(),
                    ready = x.Value.Status == SystemStatus.Ready,
                    error = x.Value.Error
                })
        });
    });

    app.MapPost("/api/health/get", (SystemStartupManager manager, ServerRoleRuntime runtime) =>
    {
        return Results.Json(new
        {
            status = "healthy",
            role = ServerRoleHelper.ToValue(runtime.Role),
            roleName = ServerRoleHelper.ToDisplayName(runtime.Role),
            systems = manager.GetAllStatuses().ToDictionary(
                x => x.Key.ToString(),
                x => new
                {
                    status = x.Value.Status.ToString(),
                    ready = x.Value.Status == SystemStatus.Ready,
                    error = x.Value.Error
                })
        });
    });
}

logger.LogInformation("HTTP 服务已启动");
app.Run();

static void ConfigureServerConfigProvider(WebApplicationBuilder builder)
{
    var serverConfigConn = builder.Configuration.GetSection("Db")["ConnectionString"];
    if (string.IsNullOrWhiteSpace(serverConfigConn))
    {
        serverConfigConn = builder.Configuration["ConnectionStrings:DefaultConnection"];
    }

    serverConfigConn ??= string.Empty;
    ((IConfigurationBuilder)builder.Configuration)
        .Add(new ServerConfigConfigurationSource(serverConfigConn));
}

static void ApplyRoleSelectionOverrides(
    WebApplicationBuilder builder,
    ServerRoleBootstrap.ServerRoleSelection roleSelection)
{
    if (string.IsNullOrWhiteSpace(roleSelection.WorkerCoreWsUrls))
    {
        return;
    }

    builder.Configuration["BacktestWorker:CoreWsUrls"] = roleSelection.WorkerCoreWsUrls;
    var firstEndpoint = roleSelection.WorkerCoreWsUrls
        .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(firstEndpoint))
    {
        // 兼容只读取单地址字段的旧逻辑
        builder.Configuration["BacktestWorker:CoreWsUrl"] = firstEndpoint;
    }
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
}

static void ConfigureOptions(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<HistoricalMarketDataOptions>(configuration.GetSection("HistoricalData"));
    services.Configure<MarketDataQueryOptions>(configuration.GetSection("MarketDataQuery"));
    services.Configure<ConditionCacheOptions>(configuration.GetSection("ConditionCache"));
    services.Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));
    services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
    services.Configure<AppWebSocketOptions>(configuration.GetSection("WebSocket"));
    services.Configure<RequestLimitsOptions>(configuration.GetSection("RequestLimits"));
    services.Configure<TradingOptions>(configuration.GetSection("Trading"));
    services.Configure<RuntimeQueueOptions>(configuration.GetSection("RuntimeQueue"));
    services.Configure<StrategyOwnershipOptions>(configuration.GetSection("StrategyOwnership"));
    services.Configure<StartupOptions>(configuration.GetSection("Startup"));
    services.Configure<RedisKeyOptions>(configuration.GetSection("RedisKey"));
    services.Configure<BusinessRulesOptions>(configuration.GetSection("BusinessRules"));
    services.Configure<BacktestWorkerOptions>(configuration.GetSection("BacktestWorker"));

    services.AddSingleton<IValidateOptions<BusinessRulesOptions>, BusinessRulesOptionsValidator>();
    services.AddSingleton<IValidateOptions<ConditionCacheOptions>, ConditionCacheOptionsValidator>();
    services.AddSingleton<IValidateOptions<MarketDataQueryOptions>, MarketDataQueryOptionsValidator>();
    services.AddSingleton<IValidateOptions<MonitoringOptions>, MonitoringOptionsValidator>();
    services.AddSingleton<IValidateOptions<RedisKeyOptions>, RedisKeyOptionsValidator>();
    services.AddSingleton<IValidateOptions<RequestLimitsOptions>, RequestLimitsOptionsValidator>();
    services.AddSingleton<IValidateOptions<RuntimeQueueOptions>, RuntimeQueueOptionsValidator>();
    services.AddSingleton<IValidateOptions<StrategyOwnershipOptions>, StrategyOwnershipOptionsValidator>();
    services.AddSingleton<IValidateOptions<BacktestWorkerOptions>, BacktestWorkerOptionsValidator>();
}

static void RegisterCommonServices(
    IServiceCollection services,
    IConfiguration configuration,
    ServerRoleBootstrap.ServerRoleSelection roleSelection,
    bool enableHttpApi)
{
    var role = roleSelection.Role;
    services.AddSingleton(new ServerRoleRuntime(
        role,
        source: roleSelection.Source,
        roleFilePath: roleSelection.RoleFilePath,
        promptSeconds: roleSelection.PromptSeconds));

    if (enableHttpApi)
    {
        services.AddScoped<ProtocolRequestFilter>();
        services.AddScoped<ProtocolResponseFilter>();

        services.AddControllers(options =>
        {
            options.Filters.Add<ProtocolRequestFilter>();
            options.Filters.Add<ProtocolResponseFilter>();
        }).AddJsonOptions(options =>
        {
            ProtocolJson.Apply(options.JsonSerializerOptions);
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = configuration["Redis:ConnectionString"];
        options.InstanceName = "ServerTest:";
    });

    services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    services.AddSingleton<SystemStartupManager>();

    if (role != ServerRole.BacktestWorker)
    {
        services.AddSingleton<StartupMonitorHost>();
        services.AddSingleton<ILoggerProvider, StartupMonitorLoggerProvider>();
    }

    services.AddScoped<DatabaseService>();
    services.AddScoped<JwtService>();
    services.AddScoped<RedisCacheService>();
    services.AddScoped<AuthTokenService>();
    services.AddScoped<VerificationCodeService>();
    services.AddSingleton<IEmailSender, LogEmailSender>();
    services.AddDbInfrastructure(configuration);

    services.AddSingleton<ServerConfigRepository>();
    services.AddSingleton<ServerConfigStore>();
    services.AddSingleton<ServerConfigService>();
    services.AddHostedService<ServerConfigBootstrapHostedService>();

    services.AddScoped<AccountRepository>();
    services.AddScoped<AccountService>();
    services.AddSingleton<OSSService>();

    services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(configuration["Redis:ConnectionString"] ?? "127.0.0.1:6379"));
    services.AddSingleton<IRateLimiter, RedisRateLimiter>();
    services.AddSingleton<WebSocketNodeId>();

    if (role == ServerRole.BacktestWorker)
    {
        services.AddSingleton<IConnectionManager, InMemoryConnectionManager>();
    }
    else
    {
        services.AddSingleton<IConnectionManager, RedisConnectionManager>();
        services.AddHostedService<RedisConnectionKickSubscriber>();
    }

    services.AddSingleton<HistoricalMarketDataRepository>();
    services.AddSingleton<HistoricalMarketDataCache>();
    services.AddSingleton<HistoricalMarketDataSyncService>();
    services.AddSingleton<BinanceHistoricalDataDownloader>();
    services.AddSingleton<ContractDetailsRepository>();
    services.AddSingleton<ContractDetailsCacheService>();
    services.AddHostedService<ContractDetailsCacheHostedService>();

    if (role != ServerRole.BacktestWorker)
    {
        services.AddHostedService<HistoricalMarketDataSyncHostedService>();
    }

    services.AddSingleton<StrategyRuntimeTemplateRepository>();
    services.AddSingleton<StrategyRuntimeTemplateStore>();
    services.AddSingleton<IStrategyRuntimeTemplateProvider>(sp =>
        sp.GetRequiredService<StrategyRuntimeTemplateStore>());
    services.AddSingleton<StrategyRuntimeTemplateService>();
    services.AddSingleton<StrategyJsonLoader>();

    services.AddSingleton<BacktestProgressPushService>();
    services.AddSingleton<BacktestObjectPoolManager>();
    services.AddScoped<BacktestMainLoop>();
    services.AddScoped<BacktestRunner>();
    services.AddScoped<BacktestService>();
    services.AddHostedService<BacktestObjectPoolWarmupHostedService>();
    services.AddScoped<BacktestTaskRepository>();
    services.AddScoped<BacktestTaskService>();

    services.AddSingleton<BacktestWorkerRegistry>();
    services.AddScoped<BacktestWorkerMessageService>();
    services.AddScoped<BacktestWorkerGateway>();
}

static void RegisterRoleServices(IServiceCollection services, IConfiguration configuration, ServerRole role)
{
    if (role == ServerRole.BacktestWorker)
    {
        services.AddSingleton<IMarketDataProvider, NoopMarketDataProvider>();
        services.AddHostedService<BacktestWorkerClientHostedService>();
        return;
    }

    services.AddSingleton<MarketDataEngine>();
    services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<MarketDataEngine>());
    services.AddSingleton<ExchangePriceService>();
    services.AddSingleton<IndicatorEngine>();

    services.AddSingleton<ConditionCacheService>();
    services.AddSingleton<ConditionEvaluator>();
    services.AddSingleton<ConditionUsageTracker>();

    services.AddSingleton<StrategyActionTaskQueue>();
    services.AddSingleton<IStrategyValueResolver, IndicatorValueResolver>();
    services.AddSingleton<IStrategyActionExecutor, QueuedStrategyActionExecutor>();
    services.AddSingleton<StrategyEngineRunLogQueue>();
    services.AddSingleton<StrategyEngineRunLogRepository>();
    services.AddSingleton<RealTimeStrategyEngine>();

    services.AddSingleton<StrategyPositionRepository>();
    services.AddSingleton<UserExchangeApiKeyRepository>();
    services.AddSingleton<PositionRiskConfigStore>();
    services.AddSingleton<PositionRiskIndexManager>();
    services.AddSingleton<IOrderExecutor, CcxtOrderExecutor>();
    services.AddScoped<StrategyPositionCloseService>();
    services.AddSingleton<PositionOverviewService>();
    services.AddSingleton<StrategyRuntimeRepository>();
    services.AddSingleton<StrategyRuntimeLoader>();
    services.AddSingleton<StrategyOwnershipService>();

    services.AddSingleton<NotificationRepository>();
    services.AddSingleton<NotificationPreferenceRepository>();
    services.AddSingleton<NotificationAccountRepository>();
    services.AddSingleton<UserNotifyChannelRepository>();
    services.AddScoped<NotificationPreferenceService>();
    services.AddSingleton<INotificationPreferenceResolver, NotificationPreferenceResolver>();
    services.AddSingleton<INotificationPublisher, NotificationPublisher>();
    services.AddSingleton<INotificationTemplateRenderer, NotificationTemplateRenderer>();
    services.AddSingleton<INotificationSender, EmailNotificationSender>();
    services.AddSingleton<INotificationSender, DingTalkNotificationSender>();
    services.AddSingleton<INotificationSender, WeComNotificationSender>();
    services.AddSingleton<INotificationSender, TelegramNotificationSender>();

    services.AddScoped<IStrategyRunCheck, ApiKeyRunCheck>();
    services.AddScoped<IStrategyRunCheck, BalanceRunCheck>();
    services.AddScoped<IStrategyRunCheck, PositionModeRunCheck>();
    services.AddScoped<IStrategyRunCheck, ExchangeReadyRunCheck>();
    services.AddScoped<StrategyRunCheckService>();
    services.AddScoped<StrategyRunCheckLogRepository>();
    services.AddSingleton<TestStrategyCheckLogRepository>();

    services.AddScoped<StrategyRepository>();
    services.AddScoped<StrategyService>();

    services.AddHostedService<StrategyRuntimeBootstrapHostedService>();
    services.AddHostedService<StrategyRuntimeLeaseHostedService>();
    services.AddHostedService<TradeActionConsumer>();
    services.AddHostedService<PositionRiskEngine>();
    services.AddHostedService<StrategyEngineRunLogWriter>();
    services.AddHostedService<StrategyRuntimeHostedService>();
    services.AddHostedService<ConditionCacheCleanupHostedService>();
    services.AddHostedService<NotificationDeliveryWorker>();

    if (role == ServerRole.Full)
    {
        services.AddHostedService<BacktestTaskWorker>();
    }
    else
    {
        services.AddHostedService<BacktestWorkerDispatchHostedService>();
    }

    services.AddScoped<WebSocketHandler>();
    services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.HealthWsHandler>();
    services.AddScoped<IWsMessageHandler, ServerTest.WebSockets.Handlers.AccountProfileUpdateHandler>();
    services.AddScoped<IWsMessageHandler, MarketSubscribeHandler>();
    services.AddScoped<IWsMessageHandler, MarketUnsubscribeHandler>();

    var useRedisSubscriptionStore = configuration.GetValue<bool?>("MarketStreaming:UseRedisSubscriptionStore") ?? true;
    if (useRedisSubscriptionStore)
    {
        services.AddSingleton<IMarketSubscriptionStore, RedisMarketSubscriptionStore>();
    }
    else
    {
        services.AddSingleton<IMarketSubscriptionStore, InMemoryMarketSubscriptionStore>();
    }

    services.AddSingleton<MarketTickerBroadcastService>();
    services.AddHostedService<MarketTickerBroadcastService>(sp => sp.GetRequiredService<MarketTickerBroadcastService>());
    services.AddHostedService<KlineCloseListenerService>();

    services.AddSingleton<AdminWebSocketBroadcastService>();
    services.AddSingleton<ServerStatusService>();
    services.AddSingleton<AdminBroadcastService>();
    services.AddSingleton<ILoggerProvider, AdminLogBroadcastProvider>();
}

static async Task RunStartupWorkflowAsync(
    WebApplication app,
    ServerRole role,
    bool enableHttpApi,
    bool enableWebSocket,
    ILogger logger)
{
    var startupManager = app.Services.GetRequiredService<SystemStartupManager>();
    startupManager.MarkStarting(SystemModule.Infrastructure, "基础设施初始化");

    try
    {
        var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = redis.GetDatabase();
        await db.StringSetAsync("__startup_test__", "ok", TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var value = await db.StringGetAsync("__startup_test__").ConfigureAwait(false);
        if (value != "ok")
        {
            throw new InvalidOperationException("Redis 连通性测试失败");
        }

        startupManager.MarkReady(SystemModule.Infrastructure, "Redis 连接正常");
    }
    catch (Exception ex)
    {
        startupManager.MarkFailed(SystemModule.Infrastructure, $"Redis 连接失败: {ex.Message}");
        throw;
    }

    if (role == ServerRole.Core || role == ServerRole.Full)
    {
        startupManager.MarkStarting(SystemModule.MarketDataEngine, "行情数据引擎");
        try
        {
            var marketDataEngine = app.Services.GetRequiredService<MarketDataEngine>();
            var startupOptions = app.Services.GetRequiredService<IOptions<StartupOptions>>().Value;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, startupOptions.MarketDataInitTimeoutSeconds));
            await marketDataEngine.WaitForInitializationAsync().WaitAsync(timeout).ConfigureAwait(false);
            startupManager.MarkReady(SystemModule.MarketDataEngine, "行情数据引擎已就绪");
        }
        catch (Exception ex)
        {
            startupManager.MarkFailed(SystemModule.MarketDataEngine, $"行情引擎初始化失败: {ex.Message}");
            throw;
        }

        startupManager.MarkStarting(SystemModule.IndicatorEngine, "指标引擎");
        _ = app.Services.GetRequiredService<IndicatorEngine>();
        startupManager.MarkReady(SystemModule.IndicatorEngine, "指标引擎已注册");

        startupManager.MarkStarting(SystemModule.StrategyEngine, "策略引擎");
        _ = app.Services.GetRequiredService<RealTimeStrategyEngine>();
        startupManager.MarkReady(SystemModule.StrategyEngine, "策略引擎已注册");

        startupManager.MarkStarting(SystemModule.TradingSystem, "交易系统");
        var startupWarmup = app.Services.GetRequiredService<IOptions<StartupOptions>>().Value.StrategyRuntimeWarmupSeconds;
        if (startupWarmup > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(startupWarmup)).ConfigureAwait(false);
        }
        startupManager.MarkReady(SystemModule.TradingSystem, "交易系统已就绪");
    }
    else
    {
        startupManager.MarkReady(SystemModule.MarketDataEngine, "当前角色不启用行情引擎");
        startupManager.MarkReady(SystemModule.IndicatorEngine, "当前角色不启用指标引擎");
        startupManager.MarkReady(SystemModule.StrategyEngine, "当前角色不启用策略引擎");
        startupManager.MarkReady(SystemModule.TradingSystem, "当前角色不启用交易系统");
    }

    startupManager.MarkStarting(SystemModule.Network, "网络层");
    ConfigurePipeline(app, enableHttpApi, enableWebSocket);
    startupManager.MarkReady(SystemModule.Network, "网络层已就绪");

    startupManager.PrintStatusSummary();
    logger.LogInformation("系统启动完成");
}

static void ConfigurePipeline(WebApplication app, bool enableHttpApi, bool enableWebSocket)
{
    if (enableHttpApi && app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseCors();

    if (enableHttpApi)
    {
        app.UseMiddleware<SystemReadinessMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<HttpRateLimitMiddleware>();
    }

    if (enableWebSocket)
    {
        var wsConfig = app.Services.GetRequiredService<IOptions<AppWebSocketOptions>>().Value;
        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(wsConfig.KeepAliveSeconds)
        });
    }
}

static void MapUserWebSocket(WebApplication app, AppWebSocketOptions wsConfig)
{
    app.Map(wsConfig.Path, wsApp =>
    {
        wsApp.Run(async context =>
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await WriteWsErrorAsync(context, StatusCodes.Status400BadRequest, ProtocolErrorCodes.InvalidRequest, "WebSocket request required")
                    .ConfigureAwait(false);
                return;
            }

            var system = context.Request.Query["system"].ToString();
            if (string.IsNullOrWhiteSpace(system))
            {
                await WriteWsErrorAsync(context, StatusCodes.Status400BadRequest, ProtocolErrorCodes.MissingField, "Missing system")
                    .ConfigureAwait(false);
                return;
            }

            var token = GetWebSocketToken(context);
            if (string.IsNullOrWhiteSpace(token))
            {
                await WriteWsErrorAsync(context, StatusCodes.Status401Unauthorized, ProtocolErrorCodes.Unauthorized, "Missing token")
                    .ConfigureAwait(false);
                return;
            }

            var tokenService = context.RequestServices.GetRequiredService<AuthTokenService>();
            var validation = await tokenService.ValidateTokenAsync(token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                await WriteWsErrorAsync(context, StatusCodes.Status401Unauthorized, ProtocolErrorCodes.TokenInvalid, "Invalid token")
                    .ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(validation.UserId))
            {
                await WriteWsErrorAsync(context, StatusCodes.Status403Forbidden, ProtocolErrorCodes.Forbidden, "Missing user id")
                    .ConfigureAwait(false);
                return;
            }

            var userId = validation.UserId;
            var connectionManager = context.RequestServices.GetRequiredService<IConnectionManager>();
            var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
            var connectionId = Guid.NewGuid();

            if (!connectionManager.TryReserve(userId, system, connectionId))
            {
                var reserved = false;
                if (string.Equals(wsConfig.KickPolicy, "KickOld", StringComparison.OrdinalIgnoreCase))
                {
                    // 分布式场景先广播踢线，确保其他节点同 user/system 连接可被挤出。
                    connectionManager.BroadcastKick(userId, system, "replaced");

                    var existing = connectionManager.GetConnections(userId)
                        .Where(c => string.Equals(c.System, system, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var old in existing)
                    {
                        await handler.KickAsync(old, "replaced", context.RequestAborted).ConfigureAwait(false);
                        connectionManager.Remove(old.UserId, old.System, old.ConnectionId);
                    }

                    // 给跨节点踢线留出极短传播时间，避免立即重试仍被旧连接占位。
                    for (var attempt = 0; attempt < 5 && !reserved; attempt++)
                    {
                        if (attempt > 0)
                        {
                            await Task.Delay(80, context.RequestAborted).ConfigureAwait(false);
                        }

                        reserved = connectionManager.TryReserve(userId, system, connectionId);
                    }
                }

                if (!reserved)
                {
                    await WriteWsErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        ProtocolErrorCodes.LimitExceeded,
                        "Too many connections for this system").ConfigureAwait(false);
                    return;
                }
            }

            WebSocketConnection connection;
            try
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                var remoteIp = context.Connection.RemoteIpAddress?.ToString();
                connection = new WebSocketConnection(connectionId, userId, system, socket, DateTime.UtcNow, remoteIp);
                connectionManager.RegisterLocal(connection);
            }
            catch
            {
                connectionManager.Remove(userId, system, connectionId);
                throw;
            }

            logger.LogInformation(
                "WS connected: user={UserId} system={System} connection={ConnectionId}",
                userId,
                system,
                connection.ConnectionId);

            try
            {
                await handler.HandleAsync(connection, context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                logger.LogInformation(
                    "WS disconnected: user={UserId} system={System} connection={ConnectionId}",
                    userId,
                    system,
                    connection.ConnectionId);
            }
        });
    });
}

static string? GetWebSocketToken(HttpContext context)
{
    var token = context.Request.Query["access_token"].ToString();
    if (!string.IsNullOrWhiteSpace(token))
    {
        return token;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (!string.IsNullOrWhiteSpace(authorization) &&
        authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return authorization[prefix.Length..].Trim();
    }

    return null;
}

static async Task WriteWsErrorAsync(HttpContext context, int statusCode, int code, string message)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json";
    var payload = ProtocolEnvelopeFactory.Error(null, code, message, null, context.TraceIdentifier);
    var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    await context.Response.WriteAsync(json).ConfigureAwait(false);
}

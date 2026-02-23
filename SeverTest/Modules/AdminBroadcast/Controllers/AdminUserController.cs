using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.Infrastructure.Db;
using ServerTest.Options;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/user")]
    public sealed class AdminUserController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly StrategyRepository _strategyRepository;
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly UserNotifyChannelRepository _notifyChannelRepository;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly IDbManager _db;
        private readonly BusinessRulesOptions _businessRules;
        private static readonly string[] RandomMethodPool = { "GreaterThan", "LessThan", "CrossUp", "CrossDown", "CrossAny" };
        private static readonly string[] TradeExchangePool = { "binance", "okx", "bitget" };
        private static readonly string[] TradeSymbolPool = { "BTC/USDT", "ETH/USDT", "SOL/USDT", "XRP/USDT" };
        private static readonly int[] TradeTimeframeSecPool = { 60, 300, 900, 3600, 14400 };
        private static readonly string[] InputSourcePool = { "Open", "High", "Low", "Close", "Volume", "HL2", "HLC3", "OHLC4", "OC2", "HLCC4" };
        // 随机策略仅从常用指标池抽样，避免策略过于分散和难以调试。
        private static readonly HashSet<string> PreferredRandomIndicatorSet = new(new[]
        {
            "MA",
            "SMA",
            "EMA",
            "MACD",
            "RSI",
            "BBANDS",
            "ATR",
        }, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> TalibCodeAlias = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CONCEALINGBABYSWALLOW"] = "CDLCONCEALBABYSWALL",
            ["GAPSIDEBYSIDEWHITELINES"] = "CDLGAPSIDESIDEWHITE",
            ["HIKKAKEMODIFIED"] = "CDLHIKKAKEMOD",
            ["IDENTICALTHREECROWS"] = "CDLIDENTICAL3CROWS",
            ["PIERCINGLINE"] = "CDLPIERCING",
            ["RISINGFALLINGTHREEMETHODS"] = "CDLRISEFALL3METHODS",
            ["TAKURILINE"] = "CDLTAKURI",
            ["UNIQUETHREERIVER"] = "CDLUNIQUE3RIVER",
            ["UPDOWNSIDEGAPTHREEMETHODS"] = "CDLXSIDEGAP3METHODS",
        };
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly JsonSerializerOptions JsonCamelCaseOptions = new(JsonSerializerDefaults.Web);
        private static readonly Lazy<RandomIndicatorCatalog> RandomCatalogLoader = new(LoadRandomIndicatorCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

        public AdminUserController(
            ILogger<AdminUserController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            StrategyRepository strategyRepository,
            UserExchangeApiKeyRepository apiKeyRepository,
            UserNotifyChannelRepository notifyChannelRepository,
            StrategyPositionRepository positionRepository,
            IDbManager db,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _notifyChannelRepository = notifyChannelRepository ?? throw new ArgumentNullException(nameof(notifyChannelRepository));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.user.universal-search")]
        [HttpPost("universal-search")]
        public async Task<IActionResult> UniversalSearch([FromBody] ProtocolRequest<UniversalSearchRequest> request)
        {
            var payload = request.Data;
            var reqId = request?.ReqId;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Query))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.MissingField, "查询关键词不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            try
            {
                var query = payload.Query.Trim();
                ulong? targetUid = null;

                // 1. 尝试通过多种方式查找用户
                // 1.1 尝试作为UID
                if (ulong.TryParse(query, out var uidValue))
                {
                    var account = await _accountRepository.GetByUidAsync(uidValue, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (account != null)
                    {
                        targetUid = account.Uid;
                    }
                }

                // 1.2 尝试作为邮箱
                if (!targetUid.HasValue && query.Contains("@"))
                {
                    var account = await _accountRepository.GetByEmailAsync(query, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (account != null)
                    {
                        targetUid = account.Uid;
                    }
                }

                // 1.3 尝试通过策略ID (us_id) 查找
                if (!targetUid.HasValue && long.TryParse(query, out var usId))
                {
                    var sql = @"
SELECT uid FROM user_strategy WHERE us_id = @usId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { usId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.4 尝试通过策略定义ID (def_id) 查找创建者
                if (!targetUid.HasValue && long.TryParse(query, out var defId))
                {
                    var sql = @"
SELECT creator_uid FROM strategy_def WHERE def_id = @defId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { defId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.5 尝试通过分享码查找
                if (!targetUid.HasValue)
                {
                    var sql = @"
SELECT created_by_uid FROM share_code WHERE share_code = @shareCode LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { shareCode = query }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.6 尝试通过API Key ID查找
                if (!targetUid.HasValue && long.TryParse(query, out var apiKeyId))
                {
                    var sql = @"
SELECT uid FROM user_exchange_api_keys WHERE id = @apiKeyId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { apiKeyId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.7 尝试通过策略名称查找
                if (!targetUid.HasValue)
                {
                    var sql = @"
SELECT creator_uid FROM strategy_def WHERE name LIKE @name LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { name = $"%{query}%" }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                if (!targetUid.HasValue)
                {
                    var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.NotFound, "未找到相关用户", null, HttpContext.TraceIdentifier);
                    return NotFound(error);
                }

                // 2. 获取用户基础信息
                var targetAccount = await _accountRepository.GetByUidAsync(targetUid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
                if (targetAccount == null)
                {
                    var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.NotFound, "用户不存在", null, HttpContext.TraceIdentifier);
                    return NotFound(error);
                }

                // 3. 向下检索用户的所有行为
                var result = new
                {
                    account = new
                    {
                        uid = targetAccount.Uid,
                        email = targetAccount.Email,
                        nickname = targetAccount.Nickname,
                        avatarUrl = targetAccount.AvatarUrl,
                        signature = targetAccount.Signature,
                        status = targetAccount.Status,
                        role = targetAccount.Role,
                        vipExpiredAt = targetAccount.VipExpiredAt,
                        currentNotificationPlatform = targetAccount.CurrentNotificationPlatform,
                        strategyIds = targetAccount.StrategyIds,
                        lastLoginAt = targetAccount.LastLoginAt,
                        registerAt = targetAccount.RegisterAt,
                        createdAt = targetAccount.CreatedAt,
                        updatedAt = targetAccount.UpdatedAt,
                        passwordUpdatedAt = targetAccount.PasswordUpdatedAt,
                        deletedAt = targetAccount.DeletedAt,
                    },
                    strategies = await GetUserStrategiesAsync(targetUid.Value).ConfigureAwait(false),
                    shareCodesCreated = await GetShareCodesCreatedAsync(targetUid.Value).ConfigureAwait(false),
                    shareEvents = await GetShareEventsAsync(targetUid.Value).ConfigureAwait(false),
                    importLogs = await GetImportLogsAsync(targetUid.Value).ConfigureAwait(false),
                    exchangeApiKeys = await GetExchangeApiKeysAsync(targetUid.Value).ConfigureAwait(false),
                    notifyChannels = await GetNotifyChannelsAsync(targetUid.Value).ConfigureAwait(false),
                    positions = await GetPositionsAsync(targetUid.Value).ConfigureAwait(false),
                };

                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.user.universal-search");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, result, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "万向查询失败: Query={Query}", payload.Query);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"查询失败: {ex.Message}", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        [ProtocolType("admin.user.test.capability")]
        [HttpPost("test/capability")]
        public async Task<IActionResult> GetTestCapability([FromBody] ProtocolRequest<object> request)
        {
            var auth = await EnsureSuperAdminAsync().ConfigureAwait(false);
            if (!auth.Passed)
            {
                return auth.Error!;
            }

            return Ok(ApiResponse<object>.Ok(new
            {
                enabled = _businessRules.EnableSuperAdminTestTools
            }, "查询成功"));
        }

        [ProtocolType("admin.user.test.create-user")]
        [HttpPost("test/create-user")]
        public async Task<IActionResult> CreateTestUser([FromBody] ProtocolRequest<TestCreateUserRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效"));
            }

            var auth = await EnsureSuperAdminAndTestToolsEnabledAsync().ConfigureAwait(false);
            if (!auth.Passed)
            {
                return auth.Error!;
            }

            var email = payload.Email?.Trim() ?? string.Empty;
            var password = payload.Password ?? string.Empty;
            var nickname = payload.Nickname?.Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(ApiResponse<object>.Error("请输入邮箱"));
            }

            if (!IsValidEmail(email))
            {
                return BadRequest(ApiResponse<object>.Error("邮箱格式不正确"));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return BadRequest(ApiResponse<object>.Error("请输入密码"));
            }

            if (password.Length < 6)
            {
                return BadRequest(ApiResponse<object>.Error("密码长度至少为6位"));
            }

            if (!string.IsNullOrWhiteSpace(nickname) && nickname.Length > 64)
            {
                return BadRequest(ApiResponse<object>.Error("昵称长度不能超过64字符"));
            }

            var existing = await _accountRepository.GetByEmailAsync(email, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (existing != null)
            {
                return BadRequest(ApiResponse<object>.Error("该邮箱已被注册"));
            }

            var resolvedNickname = string.IsNullOrWhiteSpace(nickname) ? $"测试用户{GenerateRandomSuffix(6)}" : nickname;
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            try
            {
                const string defaultSignature = "测试账号";
                var createdUid = await _accountRepository.CreateAccountAsync(
                    email,
                    passwordHash,
                    resolvedNickname,
                    null,
                    defaultSignature,
                    null,
                    HttpContext.RequestAborted).ConfigureAwait(false);

                if (createdUid <= 0)
                {
                    return StatusCode(500, ApiResponse<object>.Error("创建测试用户失败"));
                }

                return Ok(ApiResponse<object>.Ok(new
                {
                    uid = createdUid,
                    email,
                    nickname = resolvedNickname,
                    role = 0,
                    status = 0
                }, "创建测试用户成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建测试用户失败: email={Email}", email);
                return StatusCode(500, ApiResponse<object>.Error("创建测试用户失败，请稍后重试"));
            }
        }

        [ProtocolType("admin.user.test.create-random-strategy")]
        [HttpPost("test/create-random-strategy")]
        public async Task<IActionResult> CreateRandomStrategyForUser([FromBody] ProtocolRequest<TestCreateRandomStrategyRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.TargetUid <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("目标用户ID无效"));
            }

            var auth = await EnsureSuperAdminAndTestToolsEnabledAsync().ConfigureAwait(false);
            if (!auth.Passed)
            {
                return auth.Error!;
            }

            var targetUid = (ulong)payload.TargetUid;
            var targetAccount = await _accountRepository.GetByUidAsync(targetUid, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (targetAccount == null)
            {
                return NotFound(ApiResponse<object>.Error("目标用户不存在"));
            }

            var autoSwitchToTesting = payload.AutoSwitchToTesting ?? true;
            try
            {
                var randomSpec = BuildRandomStrategySpec(
                    payload.PreferredExchange,
                    payload.PreferredSymbol,
                    payload.PreferredTimeframeSec);
                var strategyName = $"测试随机策略_{DateTime.Now:yyyyMMddHHmmss}_{GenerateRandomSuffix(4)}";
                var createRequest = new StrategyCreateRequest
                {
                    Name = strategyName,
                    AliasName = strategyName,
                    Description = "超级管理员测试台随机策略",
                    ConfigJson = JsonSerializer.SerializeToElement(randomSpec.Config, JsonCamelCaseOptions)
                };

                var createActionResult = await _strategyRepository.Create(payload.TargetUid, createRequest).ConfigureAwait(false);
                var createResult = ParseActionResult(createActionResult);
                if (!createResult.Success)
                {
                    return StatusCode(createResult.StatusCode, ApiResponse<object>.Error(createResult.Message));
                }

                var defId = ReadLongProperty(createResult.Data, "defId", "DefId");
                var usId = ReadLongProperty(createResult.Data, "usId", "UsId");
                var versionId = ReadLongProperty(createResult.Data, "versionId", "VersionId");
                var versionNo = ReadLongProperty(createResult.Data, "versionNo", "VersionNo");

                if (usId <= 0)
                {
                    return StatusCode(500, ApiResponse<object>.Error("随机策略创建失败：未返回有效策略实例ID"));
                }

                var testingRequested = autoSwitchToTesting;
                var testingSuccess = false;
                var testingMessage = "未请求切换";
                var strategyState = "completed";

                if (testingRequested)
                {
                    var updateRequest = new StrategyInstanceStateRequest
                    {
                        State = "testing"
                    };
                    var updateActionResult = await _strategyRepository.UpdateInstanceState(
                        payload.TargetUid,
                        usId,
                        updateRequest,
                        HttpContext.RequestAborted).ConfigureAwait(false);

                    var updateResult = ParseActionResult(updateActionResult);
                    if (updateResult.Success)
                    {
                        testingSuccess = true;
                        testingMessage = string.IsNullOrWhiteSpace(updateResult.Message) ? "切换testing成功" : updateResult.Message;
                        strategyState = ReadStringProperty(updateResult.Data, "state", "State") ?? "testing";
                    }
                    else
                    {
                        testingSuccess = false;
                        testingMessage = string.IsNullOrWhiteSpace(updateResult.Message) ? "切换testing失败" : updateResult.Message;
                    }
                }

                var response = new
                {
                    strategy = new
                    {
                        defId,
                        usId,
                        versionId,
                        versionNo,
                        name = strategyName,
                        state = strategyState
                    },
                    random = new
                    {
                        entryMethod = randomSpec.EntryMethod,
                        exitMethod = randomSpec.ExitMethod,
                        leftIndicator = randomSpec.LeftIndicatorCode,
                        rightIndicator = randomSpec.RightIndicatorCode,
                        timeframe = randomSpec.Timeframe,
                        exchange = randomSpec.Exchange,
                        symbol = randomSpec.Symbol
                    },
                    testing = new
                    {
                        requested = testingRequested,
                        success = testingSuccess,
                        message = testingMessage,
                        state = strategyState
                    }
                };

                var message = testingRequested && !testingSuccess
                    ? "随机策略已创建，但切换testing失败"
                    : "随机策略创建成功";
                return Ok(ApiResponse<object>.Ok(response, message));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建随机策略失败: targetUid={TargetUid}", payload.TargetUid);
                return StatusCode(500, ApiResponse<object>.Error("创建随机策略失败，请稍后重试"));
            }
        }

        private async Task<AuthCheckResult> EnsureSuperAdminAsync()
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return AuthCheckResult.Fail(Unauthorized(ApiResponse<object>.Error("未授权，请重新登录")));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return AuthCheckResult.Fail(StatusCode(403, ApiResponse<object>.Error("无权限访问")));
            }

            return AuthCheckResult.Ok(uid.Value);
        }

        private async Task<AuthCheckResult> EnsureSuperAdminAndTestToolsEnabledAsync()
        {
            var auth = await EnsureSuperAdminAsync().ConfigureAwait(false);
            if (!auth.Passed)
            {
                return auth;
            }

            if (!_businessRules.EnableSuperAdminTestTools)
            {
                return AuthCheckResult.Fail(StatusCode(403, ApiResponse<object>.Error("超级测试台未启用")));
            }

            return auth;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new MailAddress(email);
                return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GenerateRandomSuffix(int length)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
            }

            return new string(chars);
        }

        private static RandomStrategySpec BuildRandomStrategySpec(
            string? preferredExchange,
            string? preferredSymbol,
            int? preferredTimeframeSec)
        {
            var catalog = RandomCatalogLoader.Value;
            if (catalog.Indicators.Count == 0)
            {
                throw new InvalidOperationException("未加载到可用指标定义");
            }

            var leftIndicator = PickRandom(catalog.Indicators);
            var rightIndicator = PickRandom(catalog.Indicators);
            if (catalog.Indicators.Count > 1)
            {
                var retry = 0;
                while (ReferenceEquals(leftIndicator, rightIndicator) && retry < 8)
                {
                    rightIndicator = PickRandom(catalog.Indicators);
                    retry++;
                }
            }

            var entryMethod = PickRandom(RandomMethodPool);
            var exitMethod = ResolveReverseMethod(entryMethod);
            var exchange = ResolvePreferredStringOrRandom(preferredExchange, TradeExchangePool, "交易所");
            var symbol = ResolvePreferredStringOrRandom(preferredSymbol, TradeSymbolPool, "交易对");
            var timeframeSec = ResolvePreferredIntOrRandom(preferredTimeframeSec, TradeTimeframeSecPool, "时间周期");
            var timeframe = MarketDataKeyNormalizer.TimeframeFromSeconds(timeframeSec);
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                timeframe = "1m";
            }

            var leftRef = BuildIndicatorReference(leftIndicator, timeframe);
            var rightRef = BuildIndicatorReference(rightIndicator, timeframe);
            var config = BuildRandomStrategyConfig(exchange, symbol, timeframeSec, entryMethod, exitMethod, leftRef, rightRef);

            return new RandomStrategySpec
            {
                Config = config,
                EntryMethod = entryMethod,
                ExitMethod = exitMethod,
                LeftIndicatorCode = leftIndicator.IndicatorCode,
                RightIndicatorCode = rightIndicator.IndicatorCode,
                Timeframe = timeframe,
                Exchange = exchange,
                Symbol = symbol
            };
        }

        private static string ResolvePreferredStringOrRandom(
            string? preferredValue,
            IReadOnlyList<string> pool,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(preferredValue))
            {
                return PickRandom(pool);
            }

            var trimmedValue = preferredValue.Trim();
            var matched = pool.FirstOrDefault(item => string.Equals(item, trimmedValue, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                return matched;
            }

            throw new ArgumentException($"{fieldName}不支持：{trimmedValue}，可选值：{string.Join("/", pool)}");
        }

        private static int ResolvePreferredIntOrRandom(
            int? preferredValue,
            IReadOnlyList<int> pool,
            string fieldName)
        {
            if (!preferredValue.HasValue)
            {
                return PickRandom(pool);
            }

            if (pool.Contains(preferredValue.Value))
            {
                return preferredValue.Value;
            }

            throw new ArgumentException($"{fieldName}不支持：{preferredValue.Value}，可选值：{string.Join("/", pool)}");
        }

        private static StrategyConfig BuildRandomStrategyConfig(
            string exchange,
            string symbol,
            int timeframeSec,
            string entryMethod,
            string exitMethod,
            StrategyValueRef leftRef,
            StrategyValueRef rightRef)
        {
            var entryCondition = BuildCompareMethod(entryMethod, leftRef, rightRef);
            var exitCondition = BuildCompareMethod(exitMethod, leftRef, rightRef);

            return new StrategyConfig
            {
                Trade = new TradeConfig
                {
                    Exchange = exchange,
                    Symbol = symbol,
                    TimeframeSec = timeframeSec,
                    PositionMode = "Cross",
                    OpenConflictPolicy = "GiveUp",
                    Sizing = new TradeSizing
                    {
                        OrderQty = 0.001m,
                        MaxPositionQty = 1m,
                        Leverage = 5
                    },
                    Risk = new TradeRisk
                    {
                        TakeProfitPct = 2m,
                        StopLossPct = 1m,
                        Trailing = new TradeTrailingStop
                        {
                            Enabled = false,
                            ActivationProfitPct = 1m,
                            CloseOnDrawdownPct = 0.5m
                        }
                    }
                },
                Logic = new StrategyLogic
                {
                    Entry = new StrategyLogicSide
                    {
                        Long = BuildLogicBranch(true, "Long", entryCondition),
                        Short = BuildLogicBranch(false, "Short", null)
                    },
                    Exit = new StrategyLogicSide
                    {
                        Long = BuildLogicBranch(true, "CloseLong", exitCondition),
                        Short = BuildLogicBranch(false, "CloseShort", null)
                    }
                },
                Runtime = new StrategyRuntimeConfig
                {
                    ScheduleType = "Always",
                    OutOfSessionPolicy = "BlockEntryAllowExit",
                    TemplateIds = new List<string>(),
                    Templates = new List<StrategyRuntimeTemplateConfig>(),
                    Custom = new StrategyRuntimeCustomConfig()
                }
            };
        }

        private static StrategyLogicBranch BuildLogicBranch(bool enabled, string action, StrategyMethod? condition)
        {
            var groups = new List<ConditionGroup>();
            if (condition != null)
            {
                groups.Add(new ConditionGroup
                {
                    Enabled = true,
                    MinPassConditions = 1,
                    Conditions = new List<StrategyMethod> { condition }
                });
            }

            return new StrategyLogicBranch
            {
                Enabled = enabled,
                MinPassConditionContainer = 1,
                Containers = new List<ConditionContainer>
                {
                    new()
                    {
                        Checks = new ConditionGroupSet
                        {
                            Enabled = true,
                            MinPassGroups = 1,
                            Groups = groups
                        }
                    }
                },
                OnPass = new ActionSet
                {
                    Enabled = true,
                    MinPassConditions = 1,
                    Conditions = new List<StrategyMethod>
                    {
                        BuildActionMethod(action)
                    }
                }
            };
        }

        private static StrategyMethod BuildActionMethod(string action)
        {
            return new StrategyMethod
            {
                Enabled = true,
                Required = false,
                Method = "MakeTrade",
                Param = new[] { action }
            };
        }

        private static StrategyMethod BuildCompareMethod(string method, StrategyValueRef left, StrategyValueRef right)
        {
            return new StrategyMethod
            {
                Enabled = true,
                Required = false,
                Method = method,
                Args = new List<StrategyValueRef>
                {
                    CloneValueRef(left),
                    CloneValueRef(right)
                }
            };
        }

        private static StrategyValueRef CloneValueRef(StrategyValueRef source)
        {
            return new StrategyValueRef
            {
                RefType = source.RefType,
                Indicator = source.Indicator,
                Timeframe = source.Timeframe,
                Input = source.Input,
                Params = source.Params?.ToList() ?? new List<double>(),
                Output = source.Output,
                OffsetRange = source.OffsetRange?.ToArray() ?? new[] { 0, 0 },
                CalcMode = source.CalcMode
            };
        }

        private static string ResolveReverseMethod(string method)
        {
            return method switch
            {
                "GreaterThan" => "LessThan",
                "LessThan" => "GreaterThan",
                "CrossUp" => "CrossDown",
                "CrossDown" => "CrossUp",
                _ => "CrossAny"
            };
        }

        private static StrategyValueRef BuildIndicatorReference(RandomIndicatorDefinition indicator, string timeframe)
        {
            return new StrategyValueRef
            {
                RefType = "Indicator",
                Indicator = indicator.IndicatorCode,
                Timeframe = timeframe,
                Input = BuildIndicatorInput(indicator.InputSlots),
                Params = indicator.Options.Select(option => option.Value).ToList(),
                Output = "Value",
                OffsetRange = new[] { 0, 0 },
                CalcMode = "OnBarClose"
            };
        }

        private static string BuildIndicatorInput(IReadOnlyList<RandomInputSlot> inputSlots)
        {
            if (inputSlots.Count == 0)
            {
                return "Close";
            }

            var selected = inputSlots
                .Select(slot => new KeyValuePair<RandomInputSlot, string>(slot, PickRandomInputSource()))
                .ToList();
            var allReal = selected.All(item => item.Key.Kind == RandomInputKind.Real);

            if (allReal)
            {
                if (selected.Count == 1)
                {
                    return selected[0].Value;
                }

                return string.Join(",", selected.Select(item => item.Value));
            }

            return string.Join(";", selected.Select(item => $"{item.Key.KeyName}={item.Value}"));
        }

        private static string PickRandomInputSource()
        {
            return PickRandom(InputSourcePool);
        }

        private static T PickRandom<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0)
            {
                throw new InvalidOperationException("随机池为空，无法选择随机值");
            }

            return values[Random.Shared.Next(values.Count)];
        }

        private static ActionResultSnapshot ParseActionResult(IActionResult? actionResult)
        {
            if (actionResult is ObjectResult objectResult)
            {
                var statusCode = objectResult.StatusCode ?? 200;
                var success = statusCode >= 200 && statusCode < 300;
                var message = success ? "操作成功" : "操作失败";
                object? data = null;

                if (objectResult.Value != null)
                {
                    var maybeSuccess = ReadBoolProperty(objectResult.Value, "Success", "success");
                    if (maybeSuccess.HasValue)
                    {
                        success = maybeSuccess.Value;
                    }

                    var maybeMessage = ReadStringProperty(objectResult.Value, "Message", "message");
                    if (!string.IsNullOrWhiteSpace(maybeMessage))
                    {
                        message = maybeMessage;
                    }

                    data = ReadObjectProperty(objectResult.Value, "Data", "data");
                }

                return new ActionResultSnapshot(statusCode, success, message, data);
            }

            if (actionResult is StatusCodeResult statusResult)
            {
                var success = statusResult.StatusCode >= 200 && statusResult.StatusCode < 300;
                return new ActionResultSnapshot(statusResult.StatusCode, success, success ? "操作成功" : "操作失败", null);
            }

            return new ActionResultSnapshot(500, false, "未能解析操作结果", null);
        }

        private static object? ReadObjectProperty(object source, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (TryReadProperty(source, propertyName, out var value))
                {
                    return value;
                }
            }

            return null;
        }

        private static long ReadLongProperty(object? source, params string[] propertyNames)
        {
            if (source == null)
            {
                return 0;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!TryReadProperty(source, propertyName, out var value))
                {
                    continue;
                }

                if (TryConvertToLong(value, out var longValue))
                {
                    return longValue;
                }
            }

            return 0;
        }

        private static string? ReadStringProperty(object? source, params string[] propertyNames)
        {
            if (source == null)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!TryReadProperty(source, propertyName, out var value) || value == null)
                {
                    continue;
                }

                if (value is string text)
                {
                    return text;
                }

                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                {
                    return jsonElement.GetString();
                }

                return Convert.ToString(value);
            }

            return null;
        }

        private static bool? ReadBoolProperty(object? source, params string[] propertyNames)
        {
            if (source == null)
            {
                return null;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!TryReadProperty(source, propertyName, out var value))
                {
                    continue;
                }

                if (TryConvertToBool(value, out var boolValue))
                {
                    return boolValue;
                }
            }

            return null;
        }

        private static bool TryReadProperty(object source, string propertyName, out object? value)
        {
            value = null;
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            if (source is JsonElement jsonElement)
            {
                if (TryGetJsonProperty(jsonElement, out var propertyValue, propertyName))
                {
                    value = propertyValue;
                    return true;
                }

                return false;
            }

            if (source is IDictionary<string, object?> dict)
            {
                foreach (var item in dict)
                {
                    if (string.Equals(item.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item.Value;
                        return true;
                    }
                }
            }

            var property = source
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property == null)
            {
                return false;
            }

            value = property.GetValue(source);
            return true;
        }

        private static bool TryGetJsonProperty(JsonElement element, out JsonElement value, params string[] propertyNames)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object || propertyNames.Length == 0)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var propertyName in propertyNames)
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryConvertToLong(object? value, out long result)
        {
            switch (value)
            {
                case null:
                    result = 0;
                    return false;
                case long longValue:
                    result = longValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case byte byteValue:
                    result = byteValue;
                    return true;
                case ulong ulongValue when ulongValue <= long.MaxValue:
                    result = (long)ulongValue;
                    return true;
                case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt64(out var numericValue):
                    result = numericValue;
                    return true;
                case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String &&
                                                   long.TryParse(jsonElement.GetString(), out var textNumericValue):
                    result = textNumericValue;
                    return true;
                default:
                    return long.TryParse(Convert.ToString(value), out result);
            }
        }

        private static bool TryConvertToBool(object? value, out bool result)
        {
            switch (value)
            {
                case null:
                    result = false;
                    return false;
                case bool boolValue:
                    result = boolValue;
                    return true;
                case JsonElement jsonElement when jsonElement.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    result = jsonElement.GetBoolean();
                    return true;
                case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String &&
                                                   bool.TryParse(jsonElement.GetString(), out var boolTextValue):
                    result = boolTextValue;
                    return true;
                default:
                    return bool.TryParse(Convert.ToString(value), out result);
            }
        }

        private static RandomIndicatorCatalog LoadRandomIndicatorCatalog()
        {
            var configPath = ResolveRandomConfigPath();
            var metaPath = ResolveRandomMetaPath();
            if (!System.IO.File.Exists(configPath))
            {
                throw new FileNotFoundException("未找到 talib 指标配置文件", configPath);
            }

            if (!System.IO.File.Exists(metaPath))
            {
                throw new FileNotFoundException("未找到 talib meta 文件", metaPath);
            }

            var configRoot = JsonSerializer.Deserialize<RandomConfigRoot>(System.IO.File.ReadAllText(configPath), JsonOptions)
                             ?? new RandomConfigRoot();
            var metaRoot = JsonSerializer.Deserialize<Dictionary<string, RandomMetaIndicator>>(System.IO.File.ReadAllText(metaPath), JsonOptions)
                           ?? new Dictionary<string, RandomMetaIndicator>(StringComparer.OrdinalIgnoreCase);

            var commonOptions = configRoot.Common != null
                ? new Dictionary<string, RandomConfigCommonOption>(configRoot.Common, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, RandomConfigCommonOption>(StringComparer.OrdinalIgnoreCase);

            var indicators = new List<RandomIndicatorDefinition>();
            foreach (var configIndicator in configRoot.Indicators ?? Enumerable.Empty<RandomConfigIndicator>())
            {
                var code = NormalizeCode(configIndicator.Code);
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var talibCode = ResolveTalibCode(code, metaRoot);
                if (string.IsNullOrWhiteSpace(talibCode))
                {
                    continue;
                }

                if (!metaRoot.TryGetValue(talibCode, out var metaDef) || metaDef == null)
                {
                    continue;
                }

                indicators.Add(new RandomIndicatorDefinition
                {
                    IndicatorCode = code,
                    TalibCode = talibCode,
                    DisplayName = !string.IsNullOrWhiteSpace(configIndicator.NameCn)
                        ? configIndicator.NameCn!
                        : (!string.IsNullOrWhiteSpace(configIndicator.NameEn) ? configIndicator.NameEn! : code),
                    InputSlots = BuildInputSlots(metaDef),
                    Options = BuildOptionValues(metaDef, configIndicator.Options ?? new List<RandomConfigOption>(), commonOptions)
                });
            }

            return new RandomIndicatorCatalog
            {
                ConfigPath = configPath,
                MetaPath = metaPath,
                Indicators = SelectPreferredRandomIndicators(indicators)
            };
        }

        private static List<RandomIndicatorDefinition> SelectPreferredRandomIndicators(
            IReadOnlyList<RandomIndicatorDefinition> indicators)
        {
            var preferred = indicators
                .Where(indicator => PreferredRandomIndicatorSet.Contains(indicator.IndicatorCode))
                .ToList();

            if (preferred.Count > 0)
            {
                return preferred;
            }

            // 极端场景（配置缺失）回退全量，确保功能可用。
            return indicators.ToList();
        }

        private static List<RandomInputSlot> BuildInputSlots(RandomMetaIndicator meta)
        {
            var result = new List<RandomInputSlot>();
            if (meta.Inputs == null || meta.Inputs.Count == 0)
            {
                return result;
            }

            var normalizedNames = meta.Inputs
                .Select(input => NormalizeInputName(input.Name ?? string.Empty))
                .ToList();
            var realCount = normalizedNames.Count(name => name is "INREAL" or "INREAL0" or "INREAL1" or "REAL");
            var periodsCount = normalizedNames.Count(name => name is "INPERIODS" or "PERIODS");
            var realIndex = 0;
            var periodsIndex = 0;

            foreach (var normalizedName in normalizedNames)
            {
                if (normalizedName is "INREAL" or "INREAL0" or "INREAL1" or "REAL")
                {
                    realIndex++;
                    result.Add(new RandomInputSlot
                    {
                        Kind = RandomInputKind.Real,
                        KeyName = realCount > 1 ? $"Real{realIndex}" : "Real"
                    });
                    continue;
                }

                if (normalizedName is "INPERIODS" or "PERIODS")
                {
                    periodsIndex++;
                    result.Add(new RandomInputSlot
                    {
                        Kind = RandomInputKind.Periods,
                        KeyName = periodsCount > 1 ? $"Periods{periodsIndex}" : "Periods"
                    });
                }
            }

            return result;
        }

        private static List<RandomOptionValue> BuildOptionValues(
            RandomMetaIndicator meta,
            IReadOnlyList<RandomConfigOption> configOptions,
            IReadOnlyDictionary<string, RandomConfigCommonOption> commonOptions)
        {
            var result = new List<RandomOptionValue>();
            if (meta.Options == null || meta.Options.Count == 0)
            {
                return result;
            }

            for (var i = 0; i < meta.Options.Count; i++)
            {
                var option = meta.Options[i];
                var optionName = option.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(optionName))
                {
                    continue;
                }

                var configOption = i < configOptions.Count ? configOptions[i] : null;
                var defaultValue = ResolveOptionDefaultValue(option, configOption, commonOptions);
                result.Add(new RandomOptionValue
                {
                    Name = optionName,
                    Type = string.IsNullOrWhiteSpace(option.Type) ? "Double" : option.Type!,
                    Value = NormalizeOptionValue(defaultValue, option.Type)
                });
            }

            return result;
        }

        private static double ResolveOptionDefaultValue(
            RandomMetaOption option,
            RandomConfigOption? configOption,
            IReadOnlyDictionary<string, RandomConfigCommonOption> commonOptions)
        {
            if (option.DefaultValue.HasValue && double.IsFinite(option.DefaultValue.Value))
            {
                return option.DefaultValue.Value;
            }

            if (!string.IsNullOrWhiteSpace(configOption?.Ref))
            {
                var refKey = ExtractRefKey(configOption.Ref!);
                if (!string.IsNullOrWhiteSpace(refKey)
                    && commonOptions.TryGetValue(refKey, out var commonOption)
                    && commonOption.Default.HasValue
                    && double.IsFinite(commonOption.Default.Value))
                {
                    return commonOption.Default.Value;
                }
            }

            return 0;
        }

        private static string ExtractRefKey(string value)
        {
            var parts = value.Split('/');
            return parts.Length == 0 ? string.Empty : parts[^1];
        }

        private static double NormalizeOptionValue(double value, string? optionType)
        {
            var normalizedType = (optionType ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedType is "integer" or "matype")
            {
                return Math.Round(value, MidpointRounding.AwayFromZero);
            }

            return value;
        }

        private static string ResolveRandomConfigPath()
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "Config", "talib_indicators_config.json");
            if (System.IO.File.Exists(direct))
            {
                return direct;
            }

            foreach (var root in BuildProbeRoots())
            {
                var inServerProject = Path.Combine(root, "SeverTest", "Config", "talib_indicators_config.json");
                if (System.IO.File.Exists(inServerProject))
                {
                    return inServerProject;
                }

                var inRepoRoot = Path.Combine(root, "Config", "talib_indicators_config.json");
                if (System.IO.File.Exists(inRepoRoot))
                {
                    return inRepoRoot;
                }
            }

            return direct;
        }

        private static string ResolveRandomMetaPath()
        {
            var inRuntimeConfig = Path.Combine(AppContext.BaseDirectory, "Config", "talib_web_api_meta.json");
            if (System.IO.File.Exists(inRuntimeConfig))
            {
                return inRuntimeConfig;
            }

            foreach (var root in BuildProbeRoots())
            {
                var inServerProject = Path.Combine(root, "SeverTest", "Config", "talib_web_api_meta.json");
                if (System.IO.File.Exists(inServerProject))
                {
                    return inServerProject;
                }

                var inClientPublic = Path.Combine(root, "Client", "public", "talib_web_api_meta.json");
                if (System.IO.File.Exists(inClientPublic))
                {
                    return inClientPublic;
                }
            }

            return inRuntimeConfig;
        }

        private static List<string> BuildProbeRoots()
        {
            var roots = new List<string>();
            TryAddRoot(roots, AppContext.BaseDirectory);
            TryAddRoot(roots, Environment.CurrentDirectory);
            TryAddRoot(roots, Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty));

            var expanded = new List<string>(roots);
            foreach (var root in roots)
            {
                var current = root;
                for (var i = 0; i < 10; i++)
                {
                    var parent = Directory.GetParent(current);
                    if (parent == null)
                    {
                        break;
                    }

                    current = parent.FullName;
                    TryAddRoot(expanded, current);
                }
            }

            return expanded;
        }

        private static void TryAddRoot(List<string> roots, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = Path.GetFullPath(path);
            if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(normalized);
            }
        }

        private static string NormalizeCode(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeInputName(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToUpperInvariant();
        }

        private static string? ResolveTalibCode(string code, IReadOnlyDictionary<string, RandomMetaIndicator> meta)
        {
            if (meta.ContainsKey(code))
            {
                return code;
            }

            if (TalibCodeAlias.TryGetValue(code, out var alias) && meta.ContainsKey(alias))
            {
                return alias;
            }

            return null;
        }

        private sealed class AuthCheckResult
        {
            public bool Passed { get; private set; }
            public long? Uid { get; private set; }
            public IActionResult? Error { get; private set; }

            public static AuthCheckResult Ok(long uid)
            {
                return new AuthCheckResult
                {
                    Passed = true,
                    Uid = uid
                };
            }

            public static AuthCheckResult Fail(IActionResult error)
            {
                return new AuthCheckResult
                {
                    Passed = false,
                    Error = error
                };
            }
        }

        private sealed record ActionResultSnapshot(int StatusCode, bool Success, string Message, object? Data);

        private sealed class RandomStrategySpec
        {
            public StrategyConfig Config { get; set; } = new();
            public string EntryMethod { get; set; } = string.Empty;
            public string ExitMethod { get; set; } = string.Empty;
            public string LeftIndicatorCode { get; set; } = string.Empty;
            public string RightIndicatorCode { get; set; } = string.Empty;
            public string Timeframe { get; set; } = string.Empty;
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
        }

        private sealed class RandomIndicatorCatalog
        {
            public string ConfigPath { get; set; } = string.Empty;
            public string MetaPath { get; set; } = string.Empty;
            public List<RandomIndicatorDefinition> Indicators { get; set; } = new();
        }

        private sealed class RandomIndicatorDefinition
        {
            public string IndicatorCode { get; set; } = string.Empty;
            public string TalibCode { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<RandomInputSlot> InputSlots { get; set; } = new();
            public List<RandomOptionValue> Options { get; set; } = new();
        }

        private sealed class RandomInputSlot
        {
            public RandomInputKind Kind { get; set; }
            public string KeyName { get; set; } = string.Empty;
        }

        private enum RandomInputKind
        {
            Real = 0,
            Periods = 1
        }

        private sealed class RandomOptionValue
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "Double";
            public double Value { get; set; }
        }

        private sealed class RandomConfigRoot
        {
            [JsonPropertyName("common")]
            public Dictionary<string, RandomConfigCommonOption>? Common { get; set; }

            [JsonPropertyName("indicators")]
            public List<RandomConfigIndicator>? Indicators { get; set; }
        }

        private sealed class RandomConfigCommonOption
        {
            [JsonPropertyName("default")]
            public double? Default { get; set; }
        }

        private sealed class RandomConfigIndicator
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("name_en")]
            public string? NameEn { get; set; }

            [JsonPropertyName("name_cn")]
            public string? NameCn { get; set; }

            [JsonPropertyName("options")]
            public List<RandomConfigOption>? Options { get; set; }
        }

        private sealed class RandomConfigOption
        {
            [JsonPropertyName("$ref")]
            public string? Ref { get; set; }
        }

        private sealed class RandomMetaIndicator
        {
            [JsonPropertyName("inputs")]
            public List<RandomMetaInput>? Inputs { get; set; }

            [JsonPropertyName("options")]
            public List<RandomMetaOption>? Options { get; set; }
        }

        private sealed class RandomMetaInput
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private sealed class RandomMetaOption
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("defaultValue")]
            public double? DefaultValue { get; set; }
        }

        private async Task<List<object>> GetUserStrategiesAsync(ulong uid)
        {
            var sql = @"
SELECT 
    us.us_id AS UsId,
    us.def_id AS DefId,
    us.alias_name AS AliasName,
    us.state AS State,
    us.visibility AS Visibility,
    us.share_code AS ShareCode,
    us.exchange_api_key_id AS ExchangeApiKeyId,
    us.source_type AS SourceType,
    us.created_at AS CreatedAt,
    us.updated_at AS UpdatedAt
FROM user_strategy us
WHERE us.uid = @uid
ORDER BY us.updated_at DESC;";

            var strategies = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return strategies.Select(s => new
            {
                usId = (long)s.UsId,
                defId = (long)s.DefId,
                aliasName = (string)s.AliasName,
                state = (string)s.State,
                visibility = (string)s.Visibility,
                shareCode = s.ShareCode?.ToString(),
                exchangeApiKeyId = s.ExchangeApiKeyId != null ? (long?)s.ExchangeApiKeyId : null,
                sourceType = (string)s.SourceType,
                createdAt = ((DateTime)s.CreatedAt).ToString("O"),
                updatedAt = ((DateTime)s.UpdatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetShareCodesCreatedAsync(ulong uid)
        {
            var sql = @"
SELECT 
    share_code AS ShareCode,
    def_id AS DefId,
    created_by_uid AS CreatedByUid,
    is_active AS IsActive,
    expired_at AS ExpiredAt,
    created_at AS CreatedAt
FROM share_code
WHERE created_by_uid = @uid
ORDER BY created_at DESC;";

            var codes = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return codes.Select(c => new
            {
                shareCode = (string)c.ShareCode,
                defId = (long)c.DefId,
                createdByUid = (ulong)c.CreatedByUid,
                isActive = Convert.ToBoolean(c.IsActive),
                expiredAt = c.ExpiredAt != null ? ((DateTime)c.ExpiredAt).ToString("O") : null,
                createdAt = ((DateTime)c.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetShareEventsAsync(ulong uid)
        {
            var sql = @"
SELECT 
    event_id AS EventId,
    share_code AS ShareCode,
    def_id AS DefId,
    from_uid AS FromUid,
    to_uid AS ToUid,
    from_instance_id AS FromInstanceId,
    to_instance_id AS ToInstanceId,
    event_type AS EventType,
    created_at AS CreatedAt
FROM share_event
WHERE from_uid = @uid OR to_uid = @uid
ORDER BY created_at DESC
LIMIT 100;";

            var events = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return events.Select(e => new
            {
                eventId = (long)e.EventId,
                shareCode = (string)e.ShareCode,
                defId = (long)e.DefId,
                fromUid = (ulong)e.FromUid,
                toUid = (ulong)e.ToUid,
                fromInstanceId = (long)e.FromInstanceId,
                toInstanceId = (long)e.ToInstanceId,
                eventType = (string)e.EventType,
                createdAt = ((DateTime)e.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetImportLogsAsync(ulong uid)
        {
            var sql = @"
SELECT 
    id AS Id,
    uid AS Uid,
    us_id AS UsId,
    source_type AS SourceType,
    source_ref AS SourceRef,
    created_at AS CreatedAt
FROM strategy_import_log
WHERE uid = @uid
ORDER BY created_at DESC;";

            var logs = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return logs.Select(l => new
            {
                id = (long)l.Id,
                uid = (ulong)l.Uid,
                usId = (long)l.UsId,
                sourceType = (string)l.SourceType,
                sourceRef = l.SourceRef?.ToString(),
                createdAt = ((DateTime)l.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetExchangeApiKeysAsync(ulong uid)
        {
            var keys = await _apiKeyRepository.GetAllByUidAsync((long)uid, HttpContext.RequestAborted).ConfigureAwait(false);
            return keys.Select(k => new
            {
                id = k.Id,
                uid = k.Uid,
                exchangeType = k.ExchangeType,
                label = k.Label,
                createdAt = k.CreatedAt.ToString("O"),
                updatedAt = k.UpdatedAt.ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetNotifyChannelsAsync(ulong uid)
        {
            var channels = await _notifyChannelRepository.GetAllByUidAsync((long)uid, HttpContext.RequestAborted).ConfigureAwait(false);
            return channels.Select(c => new
            {
                id = c.Id,
                uid = c.Uid,
                platform = c.Platform,
                address = c.Address,
                isEnabled = c.IsEnabled,
                isDefault = c.IsDefault,
                createdAt = c.CreatedAt.ToString("O"),
                updatedAt = c.UpdatedAt.ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetPositionsAsync(ulong uid)
        {
            var positions = await _positionRepository.GetByUidAsync((long)uid, null, null, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return positions.Select(p => new
            {
                positionId = p.PositionId,
                uid = p.Uid,
                usId = p.UsId,
                exchangeApiKeyId = p.ExchangeApiKeyId,
                exchange = p.Exchange,
                symbol = p.Symbol,
                side = p.Side,
                entryPrice = p.EntryPrice,
                qty = p.Qty,
                status = p.Status,
                openedAt = p.OpenedAt.ToString("O"),
                closedAt = p.ClosedAt?.ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty).ConfigureAwait(false);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                return null;
            }

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
        }

        [ProtocolType("admin.user.update")]
        [HttpPost("update")]
        public async Task<IActionResult> UpdateUser([FromBody] ProtocolRequest<UpdateUserRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.Uid == 0)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效：用户ID不能为空"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            try
            {
                var targetUid = (ulong)payload.Uid;
                var patch = new Dictionary<string, object?>();

                // 更新角色
                if (payload.Role.HasValue)
                {
                    patch["role"] = payload.Role.Value;
                }

                // 更新状态
                if (payload.Status.HasValue)
                {
                    patch["status"] = payload.Status.Value;
                }

                // 更新昵称
                if (payload.Nickname != null)
                {
                    patch["nickname"] = payload.Nickname;
                }

                // 更新头像
                if (payload.AvatarUrl != null)
                {
                    patch["avatar_url"] = payload.AvatarUrl;
                }

                // 更新签名
                if (payload.Signature != null)
                {
                    patch["signature"] = payload.Signature;
                }

                // 更新VIP到期时间
                if (payload.VipExpiredAt != null)
                {
                    patch["vip_expired_at"] = payload.VipExpiredAt;
                }

                // 更新当前通知平台
                if (payload.CurrentNotificationPlatform != null)
                {
                    patch["current_notification_platform"] = payload.CurrentNotificationPlatform;
                }

                // 更新策略ID列表
                if (payload.StrategyIds != null)
                {
                    patch["strategy_ids"] = payload.StrategyIds;
                }

                if (patch.Count == 0)
                {
                    return BadRequest(ApiResponse<object>.Error("至少需要提供一个更新字段"));
                }

                // 更新 updated_at
                patch["updated_at"] = DateTime.UtcNow;

                var affected = await _accountRepository.AdminUpdateAsync(targetUid, patch, null, HttpContext.RequestAborted).ConfigureAwait(false);
                if (affected == 0)
                {
                    return NotFound(ApiResponse<object>.Error("用户不存在或更新失败"));
                }

                // 重新获取更新后的用户信息
                var updatedAccount = await _accountRepository.GetByUidAsync(targetUid, null, HttpContext.RequestAborted).ConfigureAwait(false);
                var result = updatedAccount != null ? new
                {
                    uid = updatedAccount.Uid,
                    email = updatedAccount.Email,
                    nickname = updatedAccount.Nickname,
                    avatarUrl = updatedAccount.AvatarUrl,
                    signature = updatedAccount.Signature,
                    status = updatedAccount.Status,
                    role = updatedAccount.Role,
                    vipExpiredAt = updatedAccount.VipExpiredAt,
                    currentNotificationPlatform = updatedAccount.CurrentNotificationPlatform,
                    strategyIds = updatedAccount.StrategyIds,
                    updatedAt = updatedAccount.UpdatedAt,
                } : null;

                return Ok(ApiResponse<object>.Ok(result, "更新成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新用户失败: Uid={Uid}", payload.Uid);
                return StatusCode(500, ApiResponse<object>.Error($"更新失败: {ex.Message}"));
            }
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }
    }

    public sealed class UniversalSearchRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    public sealed class TestCreateUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Nickname { get; set; }
    }

    public sealed class TestCreateRandomStrategyRequest
    {
        public long TargetUid { get; set; }
        public bool? AutoSwitchToTesting { get; set; } = true;
        public string? PreferredExchange { get; set; }
        public string? PreferredSymbol { get; set; }
        public int? PreferredTimeframeSec { get; set; }
    }

    public sealed class UpdateUserRequest
    {
        public long Uid { get; set; }
        public byte? Role { get; set; }
        public byte? Status { get; set; }
        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Signature { get; set; }
        public DateTime? VipExpiredAt { get; set; }
        public string? CurrentNotificationPlatform { get; set; }
        public string? StrategyIds { get; set; }
    }
}

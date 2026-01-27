using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Services;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/strategy")]
    public sealed class StrategyController : BaseController
    {
        private static readonly HashSet<string> AllowedStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "draft",
            "ready",
            "running",
            "paused",
            "paused_open_position",
            "completed",
            "archived"
        };
        private static readonly HashSet<string> AllowedInstanceStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "completed",
            "running",
            "paused",
            "paused_open_position"
        };
        private static readonly JsonSerializerOptions CamelCaseSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private const string ShareCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int ShareCodeLength = 8;
        private const string StrategyStateLogTag = "[StrategyState]";

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly RealTimeStrategyEngine _strategyEngine;

        private sealed class StrategyListItem
        {
            public long UsId { get; set; }
            public long DefId { get; set; }
            public string DefName { get; set; } = string.Empty;
            public string AliasName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public int VersionNo { get; set; }
            public JsonElement? ConfigJson { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class StrategyVersionItem
        {
            public long VersionId { get; set; }
            public int VersionNo { get; set; }
            public string Changelog { get; set; } = string.Empty;
            public JsonElement? ConfigJson { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsPinned { get; set; }
        }

        private sealed class SharePolicySnapshot
        {
            public bool CanFork { get; set; } = true;
            public int? MaxClaims { get; set; }
        }

        public StrategyController(
            ILogger<StrategyController> logger,
            DatabaseService db,
            AuthTokenService tokenService,
            StrategyJsonLoader strategyLoader,
            RealTimeStrategyEngine strategyEngine)
            : base(logger)
        {
            _db = db;
            _tokenService = tokenService;
            _strategyLoader = strategyLoader;
            _strategyEngine = strategyEngine;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] StrategyCreateRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var name = request.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(ApiResponse<object>.Error("请输入策略名称"));
            }

            if (name.Length > 64)
            {
                return BadRequest(ApiResponse<object>.Error("策略名称长度不能超过64字符"));
            }

            var description = request.Description?.Trim() ?? string.Empty;
            if (description.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("策略描述长度不能超过255字符"));
            }

            var aliasName = request.AliasName?.Trim();
            if (string.IsNullOrWhiteSpace(aliasName))
            {
                aliasName = name;
            }

            if (aliasName.Length > 64)
            {
                return BadRequest(ApiResponse<object>.Error("策略实例名称长度不能超过64字符"));
            }

            if (request.ConfigJson.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少策略配置"));
            }

            var configJson = JsonSerializer.Serialize(request.ConfigJson);
            var contentHash = ComputeSha256(configJson);

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var defCmd = new MySqlCommand(@"
INSERT INTO strategy_def (creator_uid, def_type, name, description, latest_version_id, created_at, updated_at)
VALUES (@creator_uid, @def_type, @name, @description, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
", connection, transaction);
                defCmd.Parameters.AddWithValue("@creator_uid", uid.Value);
                defCmd.Parameters.AddWithValue("@def_type", "custom");
                defCmd.Parameters.AddWithValue("@name", name);
                defCmd.Parameters.AddWithValue("@description", description);
                await defCmd.ExecuteNonQueryAsync();
                var defId = defCmd.LastInsertedId;

                if (defId <= 0)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, ApiResponse<object>.Error("创建策略失败，请稍后重试"));
                }

                var versionCmd = new MySqlCommand(@"
INSERT INTO strategy_version (def_id, version_no, content_hash, config_json, artifact_uri, changelog, created_by, created_at)
VALUES (@def_id, @version_no, @content_hash, @config_json, NULL, @changelog, @created_by, CURRENT_TIMESTAMP)
", connection, transaction);
                versionCmd.Parameters.AddWithValue("@def_id", defId);
                versionCmd.Parameters.AddWithValue("@version_no", 1);
                versionCmd.Parameters.AddWithValue("@content_hash", contentHash);
                versionCmd.Parameters.AddWithValue("@config_json", configJson);
                versionCmd.Parameters.AddWithValue("@changelog", string.Empty);
                versionCmd.Parameters.AddWithValue("@created_by", uid.Value);
                await versionCmd.ExecuteNonQueryAsync();
                var versionId = versionCmd.LastInsertedId;

                if (versionId <= 0)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, ApiResponse<object>.Error("创建策略版本失败，请稍后重试"));
                }

                var userCmd = new MySqlCommand(@"
INSERT INTO user_strategy
  (uid, def_id, pinned_version_id, alias_name, description, state, visibility, share_code, price_usdt, source_type, source_ref, created_at, updated_at)
VALUES
  (@uid, @def_id, @pinned_version_id, @alias_name, @description, @state, @visibility, NULL, @price_usdt, @source_type, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
", connection, transaction);
                userCmd.Parameters.AddWithValue("@uid", uid.Value);
                userCmd.Parameters.AddWithValue("@def_id", defId);
                userCmd.Parameters.AddWithValue("@pinned_version_id", versionId);
                userCmd.Parameters.AddWithValue("@alias_name", aliasName);
                userCmd.Parameters.AddWithValue("@description", description);
                userCmd.Parameters.AddWithValue("@state", "completed");
                userCmd.Parameters.AddWithValue("@visibility", "private");
                userCmd.Parameters.AddWithValue("@price_usdt", 0);
                userCmd.Parameters.AddWithValue("@source_type", "custom");
                await userCmd.ExecuteNonQueryAsync();
                var usId = userCmd.LastInsertedId;

                if (usId <= 0)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, ApiResponse<object>.Error("创建策略实例失败，请稍后重试"));
                }

                var updateDefCmd = new MySqlCommand(@"
UPDATE strategy_def
SET latest_version_id = @version_id, updated_at = CURRENT_TIMESTAMP
WHERE def_id = @def_id
", connection, transaction);
                updateDefCmd.Parameters.AddWithValue("@version_id", versionId);
                updateDefCmd.Parameters.AddWithValue("@def_id", defId);
                await updateDefCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                var response = new
                {
                    DefId = defId,
                    UsId = usId,
                    VersionId = versionId,
                    VersionNo = 1
                };

                return Ok(ApiResponse<object>.Ok(response, "创建成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建策略失败: uid={Uid}", uid.Value);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("创建策略失败，请稍后重试"));
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> List()
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
SELECT
  us.us_id,
  us.def_id,
  us.alias_name,
  us.description,
  us.state,
  us.updated_at,
  sd.name AS def_name,
  sv.version_no,
  sv.config_json
FROM user_strategy us
JOIN strategy_def sd ON sd.def_id = us.def_id
JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.uid = @uid
ORDER BY us.updated_at DESC
", connection);
                cmd.Parameters.AddWithValue("@uid", uid.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<StrategyListItem>();
                while (await reader.ReadAsync())
                {
                    results.Add(new StrategyListItem
                    {
                        UsId = reader.GetInt64("us_id"),
                        DefId = reader.GetInt64("def_id"),
                        DefName = reader.GetString("def_name"),
                        AliasName = reader.GetString("alias_name"),
                        Description = reader.GetString("description"),
                        State = reader.GetString("state"),
                        VersionNo = reader.GetInt32("version_no"),
                        ConfigJson = ParseConfigJson(reader["config_json"]),
                        UpdatedAt = reader.GetDateTime("updated_at"),
                    });
                }

                return Ok(ApiResponse<List<StrategyListItem>>.Ok(results));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取策略列表失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("获取策略列表失败，请稍后重试"));
            }
        }

        [HttpPost("update")]
        public async Task<IActionResult> Update([FromBody] StrategyUpdateRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (request.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例"));
            }

            if (request.ConfigJson.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少策略配置"));
            }

            var configJson = JsonSerializer.Serialize(request.ConfigJson);
            var contentHash = ComputeSha256(configJson);
            var changelog = request.Changelog?.Trim() ?? string.Empty;
            if (changelog.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("变更说明长度不能超过255字符"));
            }

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                long defId;
                var ownerCmd = new MySqlCommand(@"
SELECT def_id
FROM user_strategy
WHERE us_id = @us_id AND uid = @uid
LIMIT 1
", connection, transaction);
                ownerCmd.Parameters.AddWithValue("@us_id", request.UsId);
                ownerCmd.Parameters.AddWithValue("@uid", uid.Value);

                await using (var reader = await ownerCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                    }

                    defId = reader.GetInt64("def_id");
                }

                var versionNoCmd = new MySqlCommand(@"
SELECT IFNULL(MAX(version_no), 0)
FROM strategy_version
WHERE def_id = @def_id
", connection, transaction);
                versionNoCmd.Parameters.AddWithValue("@def_id", defId);
                var nextVersionNo = Convert.ToInt32(await versionNoCmd.ExecuteScalarAsync()) + 1;

                var versionCmd = new MySqlCommand(@"
INSERT INTO strategy_version (def_id, version_no, content_hash, config_json, artifact_uri, changelog, created_by, created_at)
VALUES (@def_id, @version_no, @content_hash, @config_json, NULL, @changelog, @created_by, CURRENT_TIMESTAMP)
", connection, transaction);
                versionCmd.Parameters.AddWithValue("@def_id", defId);
                versionCmd.Parameters.AddWithValue("@version_no", nextVersionNo);
                versionCmd.Parameters.AddWithValue("@content_hash", contentHash);
                versionCmd.Parameters.AddWithValue("@config_json", configJson);
                versionCmd.Parameters.AddWithValue("@changelog", changelog);
                versionCmd.Parameters.AddWithValue("@created_by", uid.Value);
                await versionCmd.ExecuteNonQueryAsync();
                var newVersionId = versionCmd.LastInsertedId;

                if (newVersionId <= 0)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, ApiResponse<object>.Error("保存策略失败，请稍后重试"));
                }

                var updateUsCmd = new MySqlCommand(@"
UPDATE user_strategy
SET pinned_version_id = @version_id, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
", connection, transaction);
                updateUsCmd.Parameters.AddWithValue("@version_id", newVersionId);
                updateUsCmd.Parameters.AddWithValue("@us_id", request.UsId);
                updateUsCmd.Parameters.AddWithValue("@uid", uid.Value);
                await updateUsCmd.ExecuteNonQueryAsync();

                var shouldUpdateDef = false;
                var defOwnerCmd = new MySqlCommand(@"
SELECT creator_uid, def_type
FROM strategy_def
WHERE def_id = @def_id
LIMIT 1
", connection, transaction);
                defOwnerCmd.Parameters.AddWithValue("@def_id", defId);
                await using (var reader = await defOwnerCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var creatorUid = reader.GetInt64("creator_uid");
                        var defType = reader.GetString("def_type");
                        shouldUpdateDef = creatorUid == uid.Value
                            && string.Equals(defType, "custom", StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (shouldUpdateDef)
                {
                    var updateDefCmd = new MySqlCommand(@"
UPDATE strategy_def
SET latest_version_id = @version_id, updated_at = CURRENT_TIMESTAMP
WHERE def_id = @def_id
", connection, transaction);
                    updateDefCmd.Parameters.AddWithValue("@version_id", newVersionId);
                    updateDefCmd.Parameters.AddWithValue("@def_id", defId);
                    await updateDefCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                var response = new
                {
                    UsId = request.UsId,
                    NewVersionId = newVersionId,
                    NewVersionNo = nextVersionNo
                };

                return Ok(ApiResponse<object>.Ok(response, "保存成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "修改策略失败: uid={Uid} usId={UsId}", uid.Value, request.UsId);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("修改失败，请稍后重试"));
            }
        }

        [HttpPost("publish")]
        public async Task<IActionResult> Publish([FromBody] StrategyPublishRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (request.UsId <= 0 || request.VersionId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的发布参数"));
            }

            string? nextState = null;
            if (!string.IsNullOrWhiteSpace(request.StateAfterPublish))
            {
                nextState = request.StateAfterPublish.Trim().ToLowerInvariant();
                if (!AllowedStates.Contains(nextState))
                {
                    return BadRequest(ApiResponse<object>.Error("不支持的策略状态"));
                }
            }

            var changelog = request.Changelog?.Trim();
            if (!string.IsNullOrWhiteSpace(changelog) && changelog.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("变更说明长度不能超过255字符"));
            }

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                long defId;
                var currentState = string.Empty;
                var ownerCmd = new MySqlCommand(@"
SELECT def_id, state
FROM user_strategy
WHERE us_id = @us_id AND uid = @uid
LIMIT 1
", connection, transaction);
                ownerCmd.Parameters.AddWithValue("@us_id", request.UsId);
                ownerCmd.Parameters.AddWithValue("@uid", uid.Value);

                await using (var reader = await ownerCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                    }

                    defId = reader.GetInt64("def_id");
                    currentState = reader.GetString("state");
                }

                var versionCmd = new MySqlCommand(@"
SELECT version_id
FROM strategy_version
WHERE version_id = @version_id AND def_id = @def_id
LIMIT 1
", connection, transaction);
                versionCmd.Parameters.AddWithValue("@version_id", request.VersionId);
                versionCmd.Parameters.AddWithValue("@def_id", defId);
                var versionExists = await versionCmd.ExecuteScalarAsync();
                if (versionExists == null)
                {
                    return BadRequest(ApiResponse<object>.Error("版本不存在或不属于该策略"));
                }

                var updateUsSql = nextState == null
                    ? @"
UPDATE user_strategy
SET pinned_version_id = @version_id, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
"
                    : @"
UPDATE user_strategy
SET pinned_version_id = @version_id, state = @state, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
";

                var updateUsCmd = new MySqlCommand(updateUsSql, connection, transaction);
                updateUsCmd.Parameters.AddWithValue("@version_id", request.VersionId);
                updateUsCmd.Parameters.AddWithValue("@us_id", request.UsId);
                updateUsCmd.Parameters.AddWithValue("@uid", uid.Value);
                if (nextState != null)
                {
                    updateUsCmd.Parameters.AddWithValue("@state", nextState);
                }
                await updateUsCmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(changelog))
                {
                    var updateVersionCmd = new MySqlCommand(@"
UPDATE strategy_version
SET changelog = @changelog
WHERE version_id = @version_id
", connection, transaction);
                    updateVersionCmd.Parameters.AddWithValue("@changelog", changelog);
                    updateVersionCmd.Parameters.AddWithValue("@version_id", request.VersionId);
                    await updateVersionCmd.ExecuteNonQueryAsync();
                }

                var shouldUpdateDef = false;
                var defCmd = new MySqlCommand(@"
SELECT creator_uid, def_type
FROM strategy_def
WHERE def_id = @def_id
LIMIT 1
", connection, transaction);
                defCmd.Parameters.AddWithValue("@def_id", defId);
                await using (var reader = await defCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var creatorUid = reader.GetInt64("creator_uid");
                        var defType = reader.GetString("def_type");
                        shouldUpdateDef = creatorUid == uid.Value
                            && string.Equals(defType, "template", StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (shouldUpdateDef)
                {
                    var updateDefCmd = new MySqlCommand(@"
UPDATE strategy_def
SET latest_version_id = @version_id, updated_at = CURRENT_TIMESTAMP
WHERE def_id = @def_id
", connection, transaction);
                    updateDefCmd.Parameters.AddWithValue("@version_id", request.VersionId);
                    updateDefCmd.Parameters.AddWithValue("@def_id", defId);
                    await updateDefCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                var response = new
                {
                    UsId = request.UsId,
                    PinnedVersionId = request.VersionId,
                    State = nextState ?? currentState
                };

                return Ok(ApiResponse<object>.Ok(response, "发布成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "发布策略失败: uid={Uid} usId={UsId}", uid.Value, request.UsId);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("发布失败，请稍后重试"));
            }
        }

        [HttpPost("share/create-code")]
        public async Task<IActionResult> CreateShareCode([FromBody] StrategyShareCreateRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (request.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例"));
            }

            var policy = request.Policy ?? new ShareCodePolicy();
            var canFork = policy.CanFork ?? policy.AllowCopy ?? true;
            var maxClaims = policy.MaxClaims.HasValue && policy.MaxClaims.Value > 0
                ? policy.MaxClaims.Value
                : (int?)null;
            var expiredAt = policy.ExpiredAt;

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                long defId;
                string? existingShareCode = null;
                var ownerCmd = new MySqlCommand(@"
SELECT def_id, share_code
FROM user_strategy
WHERE us_id = @us_id AND uid = @uid
LIMIT 1
", connection, transaction);
                ownerCmd.Parameters.AddWithValue("@us_id", request.UsId);
                ownerCmd.Parameters.AddWithValue("@uid", uid.Value);

                await using (var reader = await ownerCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                    }

                    defId = reader.GetInt64("def_id");
                    if (!reader.IsDBNull(reader.GetOrdinal("share_code")))
                    {
                        existingShareCode = reader.GetString("share_code");
                    }
                }

                if (!string.IsNullOrWhiteSpace(existingShareCode))
                {
                    var deactivateCmd = new MySqlCommand(@"
UPDATE share_code
SET is_active = 0
WHERE share_code = @share_code
", connection, transaction);
                    deactivateCmd.Parameters.AddWithValue("@share_code", existingShareCode);
                    await deactivateCmd.ExecuteNonQueryAsync();
                }

                var shareCode = await GenerateUniqueShareCodeAsync(connection, transaction);
                var policyJson = JsonSerializer.Serialize(new
                {
                    canFork,
                    maxClaims,
                    expiredAt
                }, CamelCaseSerializerOptions);

                var insertShareCmd = new MySqlCommand(@"
INSERT INTO share_code
  (share_code, def_id, created_by_uid, policy_json, is_active, expired_at, created_at)
VALUES
  (@share_code, @def_id, @created_by_uid, @policy_json, 1, @expired_at, CURRENT_TIMESTAMP)
", connection, transaction);
                insertShareCmd.Parameters.AddWithValue("@share_code", shareCode);
                insertShareCmd.Parameters.AddWithValue("@def_id", defId);
                insertShareCmd.Parameters.AddWithValue("@created_by_uid", uid.Value);
                insertShareCmd.Parameters.AddWithValue("@policy_json", policyJson);
                insertShareCmd.Parameters.AddWithValue("@expired_at", (object?)expiredAt ?? DBNull.Value);
                await insertShareCmd.ExecuteNonQueryAsync();

                var updateUsCmd = new MySqlCommand(@"
UPDATE user_strategy
SET visibility = 'shared', share_code = @share_code, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
", connection, transaction);
                updateUsCmd.Parameters.AddWithValue("@share_code", shareCode);
                updateUsCmd.Parameters.AddWithValue("@us_id", request.UsId);
                updateUsCmd.Parameters.AddWithValue("@uid", uid.Value);
                await updateUsCmd.ExecuteNonQueryAsync();

                var insertEventCmd = new MySqlCommand(@"
INSERT INTO share_event
  (share_code, def_id, from_uid, to_uid, from_instance_id, to_instance_id, event_type, created_at)
VALUES
  (@share_code, @def_id, @from_uid, @to_uid, @from_instance_id, @to_instance_id, @event_type, CURRENT_TIMESTAMP)
", connection, transaction);
                insertEventCmd.Parameters.AddWithValue("@share_code", shareCode);
                insertEventCmd.Parameters.AddWithValue("@def_id", defId);
                insertEventCmd.Parameters.AddWithValue("@from_uid", uid.Value);
                insertEventCmd.Parameters.AddWithValue("@to_uid", uid.Value);
                insertEventCmd.Parameters.AddWithValue("@from_instance_id", request.UsId);
                insertEventCmd.Parameters.AddWithValue("@to_instance_id", request.UsId);
                insertEventCmd.Parameters.AddWithValue("@event_type", "create_code");
                await insertEventCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                var response = new
                {
                    UsId = request.UsId,
                    Visibility = "shared",
                    ShareCode = shareCode
                };
                return Ok(ApiResponse<object>.Ok(response, "分享码生成成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建分享码失败: uid={Uid} usId={UsId}", uid.Value, request.UsId);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("创建分享码失败，请稍后重试"));
            }
        }

        [HttpPost("import/share-code")]
        public async Task<IActionResult> ImportShareCode([FromBody] StrategyImportShareCodeRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var rawShareCode = request.ShareCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawShareCode))
            {
                return BadRequest(ApiResponse<object>.Error("请输入分享码"));
            }

            var shareCode = NormalizeShareCode(rawShareCode);
            if (!IsValidShareCode(shareCode))
            {
                return BadRequest(ApiResponse<object>.Error("分享码格式不正确"));
            }

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                long defId;
                long creatorUid;
                string policyJson;
                bool isActive;
                DateTime? expiredAt;

                var shareCmd = new MySqlCommand(@"
SELECT def_id, created_by_uid, policy_json, is_active, expired_at
FROM share_code
WHERE share_code = @share_code
LIMIT 1
", connection, transaction);
                shareCmd.Parameters.AddWithValue("@share_code", shareCode);

                await using (var reader = await shareCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("分享码不存在"));
                    }

                    defId = reader.GetInt64("def_id");
                    creatorUid = reader.GetInt64("created_by_uid");
                    policyJson = reader.GetString("policy_json");
                    isActive = reader.GetBoolean("is_active");
                    expiredAt = reader.IsDBNull(reader.GetOrdinal("expired_at"))
                        ? null
                        : reader.GetDateTime("expired_at");
                }

                if (!isActive)
                {
                    return BadRequest(ApiResponse<object>.Error("分享码已失效"));
                }

                if (expiredAt.HasValue && expiredAt.Value <= DateTime.Now)
                {
                    return BadRequest(ApiResponse<object>.Error("分享码已过期"));
                }

                var policySnapshot = ParseSharePolicy(policyJson);
                if (!policySnapshot.CanFork)
                {
                    return BadRequest(ApiResponse<object>.Error("该分享码不允许复制"));
                }

                if (policySnapshot.MaxClaims.HasValue)
                {
                    var countCmd = new MySqlCommand(@"
SELECT COUNT(*)
FROM share_event
WHERE share_code = @share_code AND event_type IN ('claim', 'fork')
", connection, transaction);
                    countCmd.Parameters.AddWithValue("@share_code", shareCode);
                    var usedCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                    if (usedCount >= policySnapshot.MaxClaims.Value)
                    {
                        return BadRequest(ApiResponse<object>.Error("分享码使用次数已达上限"));
                    }
                }

                long sourceUsId;
                long pinnedVersionId;
                string sourceAlias;
                string sourceDescription;
                var sourceCmd = new MySqlCommand(@"
SELECT us_id, pinned_version_id, alias_name, description
FROM user_strategy
WHERE share_code = @share_code
LIMIT 1
", connection, transaction);
                sourceCmd.Parameters.AddWithValue("@share_code", shareCode);

                await using (var reader = await sourceCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return BadRequest(ApiResponse<object>.Error("分享码未绑定策略"));
                    }

                    sourceUsId = reader.GetInt64("us_id");
                    pinnedVersionId = reader.GetInt64("pinned_version_id");
                    sourceAlias = reader.GetString("alias_name");
                    sourceDescription = reader.GetString("description");
                }

                var aliasName = request.AliasName?.Trim();
                if (string.IsNullOrWhiteSpace(aliasName))
                {
                    aliasName = $"{sourceAlias} 副本";
                }

                var insertUsCmd = new MySqlCommand(@"
INSERT INTO user_strategy
  (uid, def_id, pinned_version_id, alias_name, description, state, visibility, share_code, price_usdt, source_type, source_ref, created_at, updated_at)
VALUES
  (@uid, @def_id, @pinned_version_id, @alias_name, @description, @state, @visibility, NULL, @price_usdt, @source_type, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
", connection, transaction);
                insertUsCmd.Parameters.AddWithValue("@uid", uid.Value);
                insertUsCmd.Parameters.AddWithValue("@def_id", defId);
                insertUsCmd.Parameters.AddWithValue("@pinned_version_id", pinnedVersionId);
                insertUsCmd.Parameters.AddWithValue("@alias_name", aliasName);
                insertUsCmd.Parameters.AddWithValue("@description", sourceDescription);
                insertUsCmd.Parameters.AddWithValue("@state", "completed");
                insertUsCmd.Parameters.AddWithValue("@visibility", "private");
                insertUsCmd.Parameters.AddWithValue("@price_usdt", 0);
                insertUsCmd.Parameters.AddWithValue("@source_type", "share_code");
                await insertUsCmd.ExecuteNonQueryAsync();
                var newUsId = insertUsCmd.LastInsertedId;

                var insertLogCmd = new MySqlCommand(@"
INSERT INTO strategy_import_log
  (uid, us_id, source_type, source_ref, created_at)
VALUES
  (@uid, @us_id, @source_type, @source_ref, CURRENT_TIMESTAMP)
", connection, transaction);
                insertLogCmd.Parameters.AddWithValue("@uid", uid.Value);
                insertLogCmd.Parameters.AddWithValue("@us_id", newUsId);
                insertLogCmd.Parameters.AddWithValue("@source_type", "share_code");
                insertLogCmd.Parameters.AddWithValue("@source_ref", shareCode);
                await insertLogCmd.ExecuteNonQueryAsync();
                var importLogId = insertLogCmd.LastInsertedId;

                var updateUsCmd = new MySqlCommand(@"
UPDATE user_strategy
SET source_ref = @source_ref, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
", connection, transaction);
                updateUsCmd.Parameters.AddWithValue("@source_ref", importLogId.ToString());
                updateUsCmd.Parameters.AddWithValue("@us_id", newUsId);
                updateUsCmd.Parameters.AddWithValue("@uid", uid.Value);
                await updateUsCmd.ExecuteNonQueryAsync();

                var insertEventCmd = new MySqlCommand(@"
INSERT INTO share_event
  (share_code, def_id, from_uid, to_uid, from_instance_id, to_instance_id, event_type, created_at)
VALUES
  (@share_code, @def_id, @from_uid, @to_uid, @from_instance_id, @to_instance_id, @event_type, CURRENT_TIMESTAMP)
", connection, transaction);
                insertEventCmd.Parameters.AddWithValue("@share_code", shareCode);
                insertEventCmd.Parameters.AddWithValue("@def_id", defId);
                insertEventCmd.Parameters.AddWithValue("@from_uid", creatorUid);
                insertEventCmd.Parameters.AddWithValue("@to_uid", uid.Value);
                insertEventCmd.Parameters.AddWithValue("@from_instance_id", sourceUsId);
                insertEventCmd.Parameters.AddWithValue("@to_instance_id", newUsId);
                insertEventCmd.Parameters.AddWithValue("@event_type", "claim");
                await insertEventCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                var response = new
                {
                    NewUsId = newUsId,
                    DefId = defId,
                    PinnedVersionId = pinnedVersionId,
                    SourceType = "share_code",
                    ImportLogId = importLogId
                };
                return Ok(ApiResponse<object>.Ok(response, "导入成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "导入分享码失败: uid={Uid} shareCode={ShareCode}", uid.Value, request.ShareCode);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("导入失败，请稍后重试"));
            }
        }

        [HttpGet("versions")]
        public async Task<IActionResult> Versions([FromQuery] long usId)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (usId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();

                long defId;
                long pinnedVersionId;
                var ownerCmd = new MySqlCommand(@"
SELECT def_id, pinned_version_id
FROM user_strategy
WHERE us_id = @us_id AND uid = @uid
LIMIT 1
", connection);
                ownerCmd.Parameters.AddWithValue("@us_id", usId);
                ownerCmd.Parameters.AddWithValue("@uid", uid.Value);

                await using (var reader = await ownerCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                    }

                    defId = reader.GetInt64("def_id");
                    pinnedVersionId = reader.GetInt64("pinned_version_id");
                }

                var versionCmd = new MySqlCommand(@"
SELECT version_id, version_no, config_json, changelog, created_at
FROM strategy_version
WHERE def_id = @def_id
ORDER BY version_no ASC
", connection);
                versionCmd.Parameters.AddWithValue("@def_id", defId);

                using var versionReader = await versionCmd.ExecuteReaderAsync();
                var results = new List<StrategyVersionItem>();
                var changelogOrdinal = versionReader.GetOrdinal("changelog");
                while (await versionReader.ReadAsync())
                {
                    var versionId = versionReader.GetInt64("version_id");
                    results.Add(new StrategyVersionItem
                    {
                        VersionId = versionId,
                        VersionNo = versionReader.GetInt32("version_no"),
                        Changelog = versionReader.IsDBNull(changelogOrdinal) ? string.Empty : versionReader.GetString(changelogOrdinal),
                        ConfigJson = ParseConfigJson(versionReader["config_json"]),
                        CreatedAt = versionReader.GetDateTime("created_at"),
                        IsPinned = versionId == pinnedVersionId,
                    });
                }

                return Ok(ApiResponse<List<StrategyVersionItem>>.Ok(results));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取策略版本失败: uid={Uid} usId={UsId}", uid.Value, usId);
                return StatusCode(500, ApiResponse<object>.Error("获取策略版本失败，请稍后重试"));
            }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] StrategyDeleteRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (request.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var deleteCmd = new MySqlCommand(@"
DELETE FROM user_strategy
WHERE us_id = @us_id AND uid = @uid
", connection);
                deleteCmd.Parameters.AddWithValue("@us_id", request.UsId);
                deleteCmd.Parameters.AddWithValue("@uid", uid.Value);
                var affected = await deleteCmd.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                }

                return Ok(ApiResponse<object>.Ok(new { request.UsId }, "删除成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "删除策略失败: uid={Uid} usId={UsId}", uid.Value, request.UsId);
                return StatusCode(500, ApiResponse<object>.Error("删除失败，请稍后重试"));
            }
        }

        [HttpPatch("instances/{id}/state")]
        public async Task<IActionResult> UpdateInstanceState(long id, [FromBody] StrategyInstanceStateRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (id <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例"));
            }

            var nextState = request.State?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AllowedInstanceStates.Contains(nextState))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的策略状态"));
            }

            StrategyModel? runtimeStrategy = null;
            var runtimeState = MapInstanceState(nextState);

            await using var connection = await _db.GetConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                long defId;
                long pinnedVersionId;
                string aliasName;
                string description;
                string visibility;
                string? shareCode;
                decimal priceUsdt;
                string sourceType;
                string? sourceRef;
                string defName;
                string defDescription;
                string defType;
                long creatorUid;
                long versionId;
                int versionNo;
                string? configJson;

                var queryCmd = new MySqlCommand(@"
SELECT
  us.def_id,
  us.pinned_version_id,
  us.alias_name,
  us.description,
  us.visibility,
  us.share_code,
  us.price_usdt,
  us.source_type,
  us.source_ref,
  sd.name AS def_name,
  sd.description AS def_description,
  sd.def_type,
  sd.creator_uid,
  sv.version_id,
  sv.version_no,
  sv.config_json
FROM user_strategy us
JOIN strategy_def sd ON sd.def_id = us.def_id
JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.us_id = @us_id AND us.uid = @uid
LIMIT 1
", connection, transaction);
                queryCmd.Parameters.AddWithValue("@us_id", id);
                queryCmd.Parameters.AddWithValue("@uid", uid.Value);

                await using (var reader = await queryCmd.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return NotFound(ApiResponse<object>.Error("未找到策略实例"));
                    }

                    defId = reader.GetInt64("def_id");
                    pinnedVersionId = reader.GetInt64("pinned_version_id");
                    aliasName = reader.GetString("alias_name");
                    description = reader.GetString("description");
                    visibility = reader.GetString("visibility");
                    shareCode = reader.IsDBNull(reader.GetOrdinal("share_code")) ? null : reader.GetString("share_code");
                    priceUsdt = reader.GetDecimal("price_usdt");
                    sourceType = reader.GetString("source_type");
                    sourceRef = reader.IsDBNull(reader.GetOrdinal("source_ref")) ? null : reader.GetString("source_ref");
                    defName = reader.GetString("def_name");
                    defDescription = reader.IsDBNull(reader.GetOrdinal("def_description")) ? string.Empty : reader.GetString("def_description");
                    defType = reader.GetString("def_type");
                    creatorUid = reader.GetInt64("creator_uid");
                    versionId = reader.GetInt64("version_id");
                    versionNo = reader.GetInt32("version_no");
                    configJson = reader.IsDBNull(reader.GetOrdinal("config_json")) ? null : reader.GetString("config_json");
                }

                if (ShouldRegisterRuntime(runtimeState))
                {
                    var config = _strategyLoader.ParseConfig(configJson);
                    if (config == null)
                    {
                        await SafeRollbackAsync(transaction);
                        return StatusCode(500, ApiResponse<object>.Error("策略配置解析失败"));
                    }

                    var document = new StrategyDocument
                    {
                        UserStrategy = new StrategyUserStrategy
                        {
                            UsId = id,
                            Uid = uid.Value,
                            DefId = defId,
                            AliasName = aliasName,
                            Description = description,
                            State = nextState,
                            Visibility = visibility,
                            ShareCode = shareCode,
                            PriceUsdt = priceUsdt,
                            Source = new StrategySourceRef
                            {
                                Type = sourceType,
                                Ref = sourceRef
                            },
                            PinnedVersionId = pinnedVersionId,
                            UpdatedAt = DateTimeOffset.UtcNow
                        },
                        Definition = new StrategyDefinition
                        {
                            DefId = defId,
                            DefType = defType,
                            Name = defName,
                            Description = defDescription,
                            CreatorUid = creatorUid
                        },
                        Version = new StrategyVersion
                        {
                            VersionId = versionId,
                            VersionNo = versionNo,
                            ConfigJson = config
                        }
                    };

                    runtimeStrategy = _strategyLoader.LoadFromDocument(document);
                    if (runtimeStrategy == null)
                    {
                        await SafeRollbackAsync(transaction);
                        return StatusCode(500, ApiResponse<object>.Error("策略实例加载失败"));
                    }
                }

                var updateCmd = new MySqlCommand(@"
UPDATE user_strategy
SET state = @state, updated_at = CURRENT_TIMESTAMP
WHERE us_id = @us_id AND uid = @uid
", connection, transaction);
                updateCmd.Parameters.AddWithValue("@state", nextState);
                updateCmd.Parameters.AddWithValue("@us_id", id);
                updateCmd.Parameters.AddWithValue("@uid", uid.Value);
                await updateCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();

                if (runtimeStrategy != null)
                {
                    runtimeStrategy.State = runtimeState;
                    _strategyEngine.UpsertStrategy(runtimeStrategy);
                }
                else
                {
                    _strategyEngine.RemoveStrategy(id.ToString());
                }

                Logger.LogInformation("{Tag} uid={Uid} usId={UsId} state={State}", StrategyStateLogTag, uid.Value, id, nextState);

                var response = new { UsId = id, State = nextState };
                return Ok(ApiResponse<object>.Ok(response, "状态更新成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新策略状态失败: uid={Uid} usId={UsId}", uid.Value, id);
                await SafeRollbackAsync(transaction);
                return StatusCode(500, ApiResponse<object>.Error("状态更新失败，请稍后重试"));
            }
        }

        private static string GenerateShareCode()
        {
            Span<byte> buffer = stackalloc byte[ShareCodeLength];
            RandomNumberGenerator.Fill(buffer);
            var chars = new char[ShareCodeLength];
            for (var i = 0; i < ShareCodeLength; i++)
            {
                chars[i] = ShareCodeAlphabet[buffer[i] % ShareCodeAlphabet.Length];
            }

            return $"{new string(chars[..4])}-{new string(chars[4..])}";
        }

        private static async Task<string> GenerateUniqueShareCodeAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var code = GenerateShareCode();
                var checkCmd = new MySqlCommand(@"
SELECT 1
FROM share_code
WHERE share_code = @share_code
LIMIT 1
", connection, transaction);
                checkCmd.Parameters.AddWithValue("@share_code", code);
                var exists = await checkCmd.ExecuteScalarAsync();
                if (exists == null)
                {
                    return code;
                }
            }

            throw new InvalidOperationException("无法生成唯一分享码");
        }

        private static string NormalizeShareCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var cleaned = new string(raw.Where(char.IsLetterOrDigit).ToArray())
                .ToUpperInvariant();
            if (cleaned.Length == ShareCodeLength)
            {
                return $"{cleaned[..4]}-{cleaned[4..]}";
            }

            return cleaned;
        }

        private static bool IsValidShareCode(string shareCode)
        {
            var cleaned = new string(shareCode.Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length == ShareCodeLength;
        }

        private static SharePolicySnapshot ParseSharePolicy(string policyJson)
        {
            var snapshot = new SharePolicySnapshot();
            if (string.IsNullOrWhiteSpace(policyJson))
            {
                return snapshot;
            }

            try
            {
                using var document = JsonDocument.Parse(policyJson);
                var root = document.RootElement;
                if (root.TryGetProperty("canFork", out var canForkElement) &&
                    (canForkElement.ValueKind == JsonValueKind.True || canForkElement.ValueKind == JsonValueKind.False))
                {
                    snapshot.CanFork = canForkElement.GetBoolean();
                }
                else if (root.TryGetProperty("allowCopy", out var allowCopyElement) &&
                         (allowCopyElement.ValueKind == JsonValueKind.True || allowCopyElement.ValueKind == JsonValueKind.False))
                {
                    snapshot.CanFork = allowCopyElement.GetBoolean();
                }

                if (root.TryGetProperty("maxClaims", out var maxClaimsElement) && maxClaimsElement.ValueKind == JsonValueKind.Number)
                {
                    if (maxClaimsElement.TryGetInt32(out var maxClaims) && maxClaims > 0)
                    {
                        snapshot.MaxClaims = maxClaims;
                    }
                }
                else if (root.TryGetProperty("maxUses", out var maxUsesElement) && maxUsesElement.ValueKind == JsonValueKind.Number)
                {
                    if (maxUsesElement.TryGetInt32(out var maxUses) && maxUses > 0)
                    {
                        snapshot.MaxClaims = maxUses;
                    }
                }
            }
            catch (JsonException)
            {
                return snapshot;
            }

            return snapshot;
        }

        private static StrategyState MapInstanceState(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return StrategyState.Draft;
            }

            switch (state.Trim().ToLowerInvariant())
            {
                case "running":
                    return StrategyState.Running;
                case "paused":
                    return StrategyState.Paused;
                case "paused_open_position":
                    return StrategyState.PausedOpenPosition;
                case "completed":
                    return StrategyState.Completed;
                default:
                    return StrategyState.Draft;
            }
        }

        private static bool ShouldRegisterRuntime(StrategyState state)
        {
            return state == StrategyState.Running
                || state == StrategyState.PausedOpenPosition
                || state == StrategyState.Testing;
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                return null;
            }

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
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

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static JsonElement? ParseConfigJson(object? raw)
        {
            if (raw == null || raw == DBNull.Value)
            {
                return null;
            }

            var text = Convert.ToString(raw);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task SafeRollbackAsync(MySqlTransaction transaction)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "事务回滚失败");
            }
        }
    }
}



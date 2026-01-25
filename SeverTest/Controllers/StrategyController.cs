using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Services;

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
            "archived"
        };

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;

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

        public StrategyController(
            ILogger<StrategyController> logger,
            DatabaseService db,
            AuthTokenService tokenService)
            : base(logger)
        {
            _db = db;
            _tokenService = tokenService;
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
                userCmd.Parameters.AddWithValue("@state", "draft");
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

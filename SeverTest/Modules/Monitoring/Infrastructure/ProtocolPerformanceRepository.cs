using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Monitoring.Domain;

namespace ServerTest.Modules.Monitoring.Infrastructure
{
    public sealed class ProtocolPerformanceRepository
    {
        private const string CreateTableSql = @"
CREATE TABLE IF NOT EXISTS `monitor_protocol_performance`
(
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT COMMENT '主键',
    `req_id` VARCHAR(64) NOT NULL COMMENT '协议请求ID',
    `transport` VARCHAR(16) NOT NULL COMMENT '传输类型: http/ws',
    `protocol_type` VARCHAR(128) NOT NULL COMMENT '协议类型',
    `request_path` VARCHAR(255) DEFAULT NULL COMMENT 'HTTP路径或WS入口',
    `http_method` VARCHAR(16) DEFAULT NULL COMMENT 'HTTP方法',
    `user_id` VARCHAR(64) DEFAULT NULL COMMENT '用户ID',
    `system_name` VARCHAR(64) DEFAULT NULL COMMENT '系统标识',
    `trace_id` VARCHAR(128) DEFAULT NULL COMMENT '服务端TraceId',
    `remote_ip` VARCHAR(64) DEFAULT NULL COMMENT '客户端IP',
    `server_started_at` DATETIME(3) DEFAULT NULL COMMENT '服务端开始时间',
    `server_completed_at` DATETIME(3) DEFAULT NULL COMMENT '服务端结束时间',
    `server_elapsed_ms` INT DEFAULT NULL COMMENT '服务端处理耗时(毫秒)',
    `client_started_at` DATETIME(3) DEFAULT NULL COMMENT '前端开始时间',
    `client_completed_at` DATETIME(3) DEFAULT NULL COMMENT '前端结束时间',
    `client_elapsed_ms` INT DEFAULT NULL COMMENT '前端总往返耗时(毫秒)',
    `client_network_overhead_ms` INT DEFAULT NULL COMMENT '前端总耗时减去服务端耗时后的近似网络/浏览器开销',
    `protocol_code` INT DEFAULT NULL COMMENT '协议错误码',
    `http_status` INT DEFAULT NULL COMMENT 'HTTP状态码',
    `is_success` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否成功',
    `is_timeout` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否超时',
    `error_message` VARCHAR(512) DEFAULT NULL COMMENT '错误信息',
    `created_at` DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
    `updated_at` DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3) COMMENT '更新时间',
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_monitor_protocol_req_transport` (`req_id`, `transport`),
    KEY `idx_monitor_protocol_type_time` (`protocol_type`, `created_at`),
    KEY `idx_monitor_protocol_transport_time` (`transport`, `created_at`),
    KEY `idx_monitor_protocol_user_time` (`user_id`, `created_at`),
    KEY `idx_monitor_protocol_slow_scan` (`server_elapsed_ms`, `client_elapsed_ms`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='协议性能监控表';";

        private const string UpsertServerSql = @"
INSERT INTO monitor_protocol_performance
(
    req_id,
    transport,
    protocol_type,
    request_path,
    http_method,
    user_id,
    system_name,
    trace_id,
    remote_ip,
    server_started_at,
    server_completed_at,
    server_elapsed_ms,
    protocol_code,
    http_status,
    is_success,
    is_timeout,
    error_message
)
VALUES
(
    @ReqId,
    @Transport,
    @ProtocolType,
    @RequestPath,
    @HttpMethod,
    @UserId,
    @SystemName,
    @TraceId,
    @RemoteIp,
    @ServerStartedAt,
    @ServerCompletedAt,
    @ServerElapsedMs,
    @ProtocolCode,
    @HttpStatus,
    @IsSuccess,
    @IsTimeout,
    @ErrorMessage
)
ON DUPLICATE KEY UPDATE
    protocol_type = COALESCE(NULLIF(VALUES(protocol_type), ''), protocol_type),
    request_path = COALESCE(VALUES(request_path), request_path),
    http_method = COALESCE(VALUES(http_method), http_method),
    user_id = COALESCE(VALUES(user_id), user_id),
    system_name = COALESCE(VALUES(system_name), system_name),
    trace_id = COALESCE(VALUES(trace_id), trace_id),
    remote_ip = COALESCE(VALUES(remote_ip), remote_ip),
    server_started_at = COALESCE(VALUES(server_started_at), server_started_at),
    server_completed_at = COALESCE(VALUES(server_completed_at), server_completed_at),
    server_elapsed_ms = COALESCE(VALUES(server_elapsed_ms), server_elapsed_ms),
    protocol_code = COALESCE(VALUES(protocol_code), protocol_code),
    http_status = COALESCE(VALUES(http_status), http_status),
    is_success = CASE
        WHEN is_success = 0 THEN 0
        WHEN VALUES(is_success) = 0 THEN 0
        ELSE 1
    END,
    is_timeout = CASE
        WHEN is_timeout = 1 OR VALUES(is_timeout) = 1 THEN 1
        ELSE 0
    END,
    error_message = COALESCE(VALUES(error_message), error_message),
    client_network_overhead_ms = CASE
        WHEN client_elapsed_ms IS NOT NULL AND COALESCE(VALUES(server_elapsed_ms), server_elapsed_ms) IS NOT NULL
        THEN GREATEST(client_elapsed_ms - COALESCE(VALUES(server_elapsed_ms), server_elapsed_ms), 0)
        ELSE client_network_overhead_ms
    END,
    updated_at = CURRENT_TIMESTAMP(3);";

        private const string UpsertClientSql = @"
INSERT INTO monitor_protocol_performance
(
    req_id,
    transport,
    protocol_type,
    request_path,
    http_method,
    system_name,
    client_started_at,
    client_completed_at,
    client_elapsed_ms,
    protocol_code,
    http_status,
    is_success,
    is_timeout,
    error_message
)
VALUES
(
    @ReqId,
    @Transport,
    @ProtocolType,
    @RequestPath,
    @HttpMethod,
    @SystemName,
    @ClientStartedAt,
    @ClientCompletedAt,
    @ClientElapsedMs,
    @ProtocolCode,
    @HttpStatus,
    @IsSuccess,
    @IsTimeout,
    @ErrorMessage
)
ON DUPLICATE KEY UPDATE
    protocol_type = COALESCE(NULLIF(VALUES(protocol_type), ''), protocol_type),
    request_path = COALESCE(VALUES(request_path), request_path),
    http_method = COALESCE(VALUES(http_method), http_method),
    system_name = COALESCE(VALUES(system_name), system_name),
    client_started_at = COALESCE(VALUES(client_started_at), client_started_at),
    client_completed_at = COALESCE(VALUES(client_completed_at), client_completed_at),
    client_elapsed_ms = COALESCE(VALUES(client_elapsed_ms), client_elapsed_ms),
    protocol_code = COALESCE(VALUES(protocol_code), protocol_code),
    http_status = COALESCE(VALUES(http_status), http_status),
    is_success = CASE
        WHEN is_success = 0 THEN 0
        WHEN VALUES(is_success) = 0 THEN 0
        ELSE 1
    END,
    is_timeout = CASE
        WHEN is_timeout = 1 OR VALUES(is_timeout) = 1 THEN 1
        ELSE 0
    END,
    error_message = COALESCE(VALUES(error_message), error_message),
    client_network_overhead_ms = CASE
        WHEN VALUES(client_elapsed_ms) IS NOT NULL AND server_elapsed_ms IS NOT NULL
        THEN GREATEST(VALUES(client_elapsed_ms) - server_elapsed_ms, 0)
        ELSE client_network_overhead_ms
    END,
    updated_at = CURRENT_TIMESTAMP(3);";

        private readonly IDbManager _db;
        private int _tableEnsured;

        public ProtocolPerformanceRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task EnsureTableAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _tableEnsured) == 1)
            {
                return;
            }

            await _db.ExecuteAsync(CreateTableSql, null, null, ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _tableEnsured, 1);
        }

        public Task<int> UpsertServerMetricAsync(ProtocolPerformanceServerMetric metric, CancellationToken ct = default)
        {
            return _db.ExecuteAsync(UpsertServerSql, metric, null, ct);
        }

        public Task<int> UpsertClientMetricsAsync(IReadOnlyCollection<ProtocolPerformanceClientMetric> metrics, CancellationToken ct = default)
        {
            return _db.ExecuteAsync(UpsertClientSql, metrics, null, ct);
        }

        public async Task<IReadOnlyList<ProtocolPerformanceSummaryItem>> GetSummaryAsync(
            DateTime windowStart,
            DateTime windowEnd,
            string? transport,
            int top,
            int slowServerThresholdMs,
            int slowClientThresholdMs,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
    protocol_type AS ProtocolType,
    transport AS Transport,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN is_success = 1 THEN 1 ELSE 0 END) AS SuccessCount,
    SUM(CASE WHEN is_success = 0 THEN 1 ELSE 0 END) AS ErrorCount,
    ROUND(AVG(server_elapsed_ms), 2) AS AvgServerElapsedMs,
    MAX(server_elapsed_ms) AS MaxServerElapsedMs,
    ROUND(AVG(client_elapsed_ms), 2) AS AvgClientElapsedMs,
    MAX(client_elapsed_ms) AS MaxClientElapsedMs,
    ROUND(AVG(client_network_overhead_ms), 2) AS AvgClientNetworkOverheadMs,
    SUM(CASE
        WHEN COALESCE(server_elapsed_ms, 0) >= @SlowServerThresholdMs
            OR COALESCE(client_elapsed_ms, 0) >= @SlowClientThresholdMs
        THEN 1 ELSE 0 END) AS SlowCount,
    MAX(updated_at) AS LastSeenAt
FROM monitor_protocol_performance
WHERE updated_at >= @WindowStart
  AND updated_at < @WindowEnd
  AND (@Transport IS NULL OR transport = @Transport)
GROUP BY protocol_type, transport
ORDER BY COALESCE(AVG(client_elapsed_ms), AVG(server_elapsed_ms), 0) DESC, COUNT(*) DESC
LIMIT @Top;";

            var result = await _db.QueryAsync<ProtocolPerformanceSummaryItem>(sql, new
            {
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                Transport = transport,
                Top = top,
                SlowServerThresholdMs = slowServerThresholdMs,
                SlowClientThresholdMs = slowClientThresholdMs
            }, null, ct).ConfigureAwait(false);

            return result.ToList();
        }

        internal async Task<ProtocolPerformanceGlobalStats> GetGlobalStatsAsync(
            DateTime windowStart,
            DateTime windowEnd,
            string? transport,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
    ROUND(AVG(server_elapsed_ms), 2) AS AvgServerElapsedMs,
    ROUND(AVG(client_elapsed_ms), 2) AS AvgClientElapsedMs
FROM monitor_protocol_performance
WHERE updated_at >= @WindowStart
  AND updated_at < @WindowEnd
  AND (@Transport IS NULL OR transport = @Transport);";

            return await _db.QuerySingleOrDefaultAsync<ProtocolPerformanceGlobalStats>(sql, new
            {
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                Transport = transport
            }, null, ct).ConfigureAwait(false) ?? new ProtocolPerformanceGlobalStats();
        }
    }
}

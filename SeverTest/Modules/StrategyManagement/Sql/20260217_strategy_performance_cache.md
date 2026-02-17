# 20260217 StrategyPerformanceCache 缓存表

> 作用：为公开策略场景提供可复用的 30 日资金曲线缓存，避免多人访问时重复回测。  
> 覆盖范围：公开帖子、分享码、公开市场、官方策略。  
> 数据库：`dwquant`

```sql
CREATE TABLE IF NOT EXISTS strategy_performance_cache (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cache_key VARCHAR(128) NOT NULL,
  us_id BIGINT UNSIGNED NOT NULL,
  window_days INT NOT NULL DEFAULT 30,
  curve_source VARCHAR(16) NOT NULL DEFAULT 'live',
  is_backtest TINYINT(1) NOT NULL DEFAULT 0,
  pnl_series_json LONGTEXT NOT NULL,
  trade_log_json LONGTEXT NULL,
  position_log_json LONGTEXT NULL,
  open_close_log_json LONGTEXT NULL,
  live_fingerprint VARCHAR(256) NULL,
  backtest_fingerprint VARCHAR(256) NULL,
  cache_scope VARCHAR(32) NOT NULL DEFAULT 'private',
  expires_at DATETIME(3) NULL,
  last_hit_at DATETIME(3) NULL,
  hit_count BIGINT NOT NULL DEFAULT 0,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_strategy_performance_cache_key (cache_key),
  KEY idx_strategy_performance_cache_us_days (us_id, window_days),
  KEY idx_strategy_performance_cache_scope (cache_scope),
  KEY idx_strategy_performance_cache_expires (expires_at),
  KEY idx_strategy_performance_cache_updated (updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

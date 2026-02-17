# 20260216 Planet 星球模块建表脚本

> 说明：仓库默认忽略 `*.sql`，此处提供可审计的 SQL 文本版本。  
> 执行数据库：`dwquant`。  
> 执行方式：可直接复制以下 SQL 到 MySQL 客户端执行。

```sql
CREATE TABLE IF NOT EXISTS planet_post (
  post_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uid BIGINT UNSIGNED NOT NULL,
  title VARCHAR(128) NOT NULL,
  content TEXT NOT NULL,
  visibility VARCHAR(16) NOT NULL DEFAULT 'public',
  status VARCHAR(16) NOT NULL DEFAULT 'active',
  like_count INT UNSIGNED NOT NULL DEFAULT 0,
  dislike_count INT UNSIGNED NOT NULL DEFAULT 0,
  favorite_count INT UNSIGNED NOT NULL DEFAULT 0,
  comment_count INT UNSIGNED NOT NULL DEFAULT 0,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (post_id),
  KEY idx_planet_post_uid_time (uid, created_at),
  KEY idx_planet_post_visibility_status_time (visibility, status, created_at),
  KEY idx_planet_post_status_time (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS planet_post_image (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  post_id BIGINT UNSIGNED NOT NULL,
  image_url VARCHAR(512) NOT NULL,
  sort_order INT UNSIGNED NOT NULL DEFAULT 0,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_planet_post_image_post_sort (post_id, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS planet_post_strategy (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  post_id BIGINT UNSIGNED NOT NULL,
  us_id BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_planet_post_strategy_post_us (post_id, us_id),
  KEY idx_planet_post_strategy_us (us_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS planet_post_reaction (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  post_id BIGINT UNSIGNED NOT NULL,
  uid BIGINT UNSIGNED NOT NULL,
  reaction_type VARCHAR(16) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_planet_post_reaction_post_uid (post_id, uid),
  KEY idx_planet_post_reaction_post_type (post_id, reaction_type),
  KEY idx_planet_post_reaction_uid_time (uid, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS planet_post_favorite (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  post_id BIGINT UNSIGNED NOT NULL,
  uid BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uk_planet_post_favorite_post_uid (post_id, uid),
  KEY idx_planet_post_favorite_uid_time (uid, created_at),
  KEY idx_planet_post_favorite_post (post_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS planet_post_comment (
  comment_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  post_id BIGINT UNSIGNED NOT NULL,
  uid BIGINT UNSIGNED NOT NULL,
  content VARCHAR(1000) NOT NULL,
  status VARCHAR(16) NOT NULL DEFAULT 'active',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (comment_id),
  KEY idx_planet_post_comment_post_time (post_id, created_at),
  KEY idx_planet_post_comment_uid_time (uid, created_at),
  KEY idx_planet_post_comment_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## 回滚建议

- 若需回滚，可按依赖逆序删除：
  1. `planet_post_comment`
  2. `planet_post_favorite`
  3. `planet_post_reaction`
  4. `planet_post_strategy`
  5. `planet_post_image`
  6. `planet_post`

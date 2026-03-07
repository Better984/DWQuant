import type { StrategyConfig } from './StrategyModule.types';

const isPlainObject = (value: unknown): value is Record<string, unknown> => {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
};

const toCamelLikeKey = (key: string) => {
  if (!key) {
    return key;
  }
  return `${key[0].toLowerCase()}${key.slice(1)}`;
};

const normalizeObjectKeysDeep = (value: unknown): unknown => {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeObjectKeysDeep(item));
  }
  if (!isPlainObject(value)) {
    return value;
  }

  const normalized: Record<string, unknown> = {};
  Object.entries(value).forEach(([key, item]) => {
    normalized[toCamelLikeKey(key)] = normalizeObjectKeysDeep(item);
  });
  return normalized;
};

export const isStrategyConfig = (value: unknown): value is StrategyConfig => {
  if (!isPlainObject(value)) {
    return false;
  }
  return isPlainObject(value.trade) && isPlainObject(value.logic);
};

// 兼容 AI / 历史消息里 PascalCase 键名的策略配置，统一转为前端使用的 camelCase 结构。
export const normalizeStrategyConfig = (raw: unknown): StrategyConfig | undefined => {
  if (!raw) {
    return undefined;
  }

  let parsed: unknown = raw;
  if (typeof raw === 'string') {
    if (!raw.trim()) {
      return undefined;
    }
    try {
      parsed = JSON.parse(raw) as unknown;
    } catch {
      return undefined;
    }
  }

  const normalized = normalizeObjectKeysDeep(parsed);
  return isStrategyConfig(normalized) ? normalized : undefined;
};

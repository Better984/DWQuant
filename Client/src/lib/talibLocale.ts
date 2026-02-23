/**
 * TA 指标分类中文映射。
 * 优先使用配置中的 *_cn 字段；当未配置时再使用此映射兜底。
 */
export const TALIB_GROUP_CN_LABELS: Record<string, string> = {
  "Overlap Studies": "叠加研究",
  "Price Transform": "价格变换",
  "Momentum Indicators": "动量指标",
  "Volume Indicators": "成交量指标",
  "Volatility Indicators": "波动率指标",
  "Cycle Indicators": "周期指标",
  "Pattern Recognition": "形态识别",
  "Math Transform": "数学变换",
  "Math Operators": "数学运算",
  "Statistic Functions": "统计函数",
  Other: "其他",
};

function hasChineseText(value: string): boolean {
  return /[\u4e00-\u9fff]/.test(value);
}

export function resolveTalibGroupCn(
  group: string | null | undefined,
  preferredCn?: string | null | undefined
): string {
  const preferred = (preferredCn ?? "").trim();
  if (preferred.length > 0) {
    return preferred;
  }

  const raw = (group ?? "").trim();
  if (raw.length === 0) {
    return TALIB_GROUP_CN_LABELS.Other;
  }

  if (hasChineseText(raw)) {
    return raw;
  }

  return TALIB_GROUP_CN_LABELS[raw] ?? raw;
}

export function resolveTalibIndicatorDisplayName(
  code: string,
  nameCn?: string | null,
  nameEn?: string | null,
  abbrEn?: string | null
): string {
  const cn = (nameCn ?? "").trim();
  if (cn.length > 0) {
    return cn;
  }

  const en = (nameEn ?? "").trim();
  if (en.length > 0) {
    return en;
  }

  const abbr = (abbrEn ?? "").trim();
  if (abbr.length > 0) {
    return abbr;
  }

  return code;
}

const FIELD_LABEL_MAP: Record<string, string> = {
  OPEN: '开盘价 (Open)',
  HIGH: '最高价 (High)',
  LOW: '最低价 (Low)',
  CLOSE: '收盘价 (Close)',
  VOLUME: '成交量 (Volume)',
  HL2: '高低均价 (HL2)',
  HLC3: '高低收均价 (HLC3)',
  OHLC4: '四价均价 (OHLC4)',
  OC2: '开收均价 (OC2)',
  HLCC4: '高低收收均价 (HLCC4)',
};

const normalizeText = (value?: string) => (value || '').trim().replace(/\s{2,}/g, ' ');

const stripFieldGroupPrefix = (value?: string) =>
  normalizeText((value || '').replace(/^K线字段\s*-\s*/i, ''));

const normalizeFieldKey = (raw?: string) => normalizeText(raw).toUpperCase();

const formatFieldValueIdLabel = (valueId?: string) => {
  const normalized = normalizeText(valueId);
  if (!/^field:/i.test(normalized)) {
    return '';
  }
  const fieldKey = normalizeFieldKey(normalized.slice('field:'.length));
  return FIELD_LABEL_MAP[fieldKey] || fieldKey;
};

const formatIndicatorReferenceKeyLabel = (valueId?: string) => {
  const normalized = normalizeText(valueId);
  const parts = normalized.split('|');
  if (parts.length < 5) {
    return '';
  }
  const [indicator, timeframe, input, output, ...paramParts] = parts;
  if (!indicator) {
    return '';
  }
  const paramsText = normalizeText(paramParts.join('|'));
  const head = paramsText ? `${indicator}(${paramsText})` : indicator;
  const labelParts = [
    head,
    normalizeText(timeframe),
    normalizeText(input),
    normalizeText(output),
  ].filter(Boolean);
  return normalizeText(labelParts.join(' · '));
};

// 统一格式化条件值显示，避免工作台直接暴露内部 valueId / 引用键。
export const formatStrategyValueLabel = (valueId?: string, preferredLabel?: string) => {
  const label = stripFieldGroupPrefix(preferredLabel);
  if (label) {
    return label;
  }
  if (!valueId) {
    return '未配置';
  }
  const fieldLabel = formatFieldValueIdLabel(valueId);
  if (fieldLabel) {
    return fieldLabel;
  }
  const indicatorRefLabel = formatIndicatorReferenceKeyLabel(valueId);
  if (indicatorRefLabel) {
    return indicatorRefLabel;
  }
  return stripFieldGroupPrefix(valueId);
};

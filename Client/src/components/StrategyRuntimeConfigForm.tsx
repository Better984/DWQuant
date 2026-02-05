import React from 'react';

import type {
  StrategyRuntimeConfig,
  StrategyRuntimeCustomConfig,
  StrategyRuntimeTemplateConfig,
  StrategyRuntimeTimeRange,
} from './StrategyModule.types';

type TimezoneOption = {
  value: string;
  label: string;
};

interface StrategyRuntimeConfigFormProps {
  config: StrategyRuntimeConfig;
  templateOptions: StrategyRuntimeTemplateConfig[];
  timezoneOptions: TimezoneOption[];
  onChange: (config: StrategyRuntimeConfig) => void;
}

const normalizeRuntimeConfig = (input?: StrategyRuntimeConfig): StrategyRuntimeConfig => {
  const fallbackTimezone = 'Asia/Shanghai';
  const safe = input || {
    scheduleType: 'Always',
    outOfSessionPolicy: 'BlockEntryAllowExit',
    templateIds: [],
    templates: [],
    custom: {
      mode: 'Deny',
      timezone: fallbackTimezone,
      days: [],
      timeRanges: [],
    },
  };

  return {
    scheduleType: safe.scheduleType || 'Always',
    outOfSessionPolicy: safe.outOfSessionPolicy || 'BlockEntryAllowExit',
    templateIds: Array.isArray(safe.templateIds) ? safe.templateIds : [],
    templates: Array.isArray(safe.templates) ? safe.templates : [],
    custom: {
      mode: safe.custom?.mode === 'Allow' ? 'Allow' : 'Deny',
      timezone: safe.custom?.timezone || fallbackTimezone,
      days: Array.isArray(safe.custom?.days) ? safe.custom?.days || [] : [],
      timeRanges: Array.isArray(safe.custom?.timeRanges) ? safe.custom?.timeRanges || [] : [],
    },
  };
};

const DAY_OPTIONS = [
  { value: 'mon', label: '周一' },
  { value: 'tue', label: '周二' },
  { value: 'wed', label: '周三' },
  { value: 'thu', label: '周四' },
  { value: 'fri', label: '周五' },
  { value: 'sat', label: '周六' },
  { value: 'sun', label: '周日' },
];

const HOUR_OPTIONS = Array.from({ length: 24 }, (_, hour) => {
  const label = `${String(hour).padStart(2, '0')}:00`;
  return { value: label, label };
});

const buildDaysLabel = (days: string[]) => {
  if (!days || days.length === 0) {
    return '未选择星期';
  }
  const labelMap = new Map(DAY_OPTIONS.map((option) => [option.value, option.label]));
  return days.map((day) => labelMap.get(day) || day).join('、');
};

const buildRangesLabel = (ranges: StrategyRuntimeTimeRange[]) => {
  if (!ranges || ranges.length === 0) {
    return '未配置时间段';
  }
  return ranges.map((range) => `${range.start}-${range.end}`).join(' / ');
};

const StrategyRuntimeConfigForm: React.FC<StrategyRuntimeConfigFormProps> = ({
  config,
  templateOptions,
  timezoneOptions,
  onChange,
}) => {
  const safeConfig = React.useMemo(() => normalizeRuntimeConfig(config), [config]);

  const updateConfig = (next: Partial<StrategyRuntimeConfig>) => {
    onChange({
      ...safeConfig,
      ...next,
      custom: { ...safeConfig.custom, ...(next.custom || {}) },
    });
  };

  const updateCustom = (next: Partial<StrategyRuntimeCustomConfig>) => {
    updateConfig({ custom: { ...safeConfig.custom, ...next } });
  };

  const toggleDay = (day: string) => {
    const nextDays = safeConfig.custom.days.includes(day)
      ? safeConfig.custom.days.filter((item) => item !== day)
      : [...safeConfig.custom.days, day];
    updateCustom({ days: nextDays });
  };

  const addTimeRange = () => {
    const nextRanges = [
      ...safeConfig.custom.timeRanges,
      { start: '09:00', end: '10:00' },
    ];
    updateCustom({ timeRanges: nextRanges });
  };

  const updateTimeRange = (index: number, key: keyof StrategyRuntimeTimeRange, value: string) => {
    const nextRanges = safeConfig.custom.timeRanges.map((range, idx) =>
      idx === index ? { ...range, [key]: value } : range,
    );
    updateCustom({ timeRanges: nextRanges });
  };

  const removeTimeRange = (index: number) => {
    const nextRanges = safeConfig.custom.timeRanges.filter((_, idx) => idx !== index);
    updateCustom({ timeRanges: nextRanges });
  };

  const toggleTemplate = (template: StrategyRuntimeTemplateConfig) => {
    const exists = safeConfig.templateIds.includes(template.id);
    const nextIds = exists
      ? safeConfig.templateIds.filter((item) => item !== template.id)
      : [...safeConfig.templateIds, template.id];
    updateConfig({ templateIds: nextIds });
  };

  return (
    <div className="runtime-config">
      <div className="runtime-config-row">
        <div className="trade-form-label">运行时间模式</div>
        <div className="trade-chip-group">
          {(['Always', 'Template', 'Custom'] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              className={`trade-chip ${safeConfig.scheduleType === mode ? 'active' : ''}`}
              onClick={() => updateConfig({ scheduleType: mode })}
            >
              {mode === 'Always' ? '全天运行' : mode === 'Template' ? '模板' : '自定义'}
            </button>
          ))}
        </div>
      </div>

      {safeConfig.scheduleType === 'Always' && (
        <div className="runtime-config-hint">
          选择“模板”或“自定义”后可设置策略运行时间段。
        </div>
      )}

      {safeConfig.scheduleType === 'Template' && (
        <div className="runtime-config-section">
          <div className="trade-form-label">选择时间模板（可多选）</div>
          <div className="runtime-template-grid">
            {templateOptions.map((template) => {
              const isSelected = safeConfig.templateIds.includes(template.id);
              return (
                <button
                  key={template.id}
                  type="button"
                  className={`runtime-template-card ${isSelected ? 'active' : ''}`}
                  onClick={() => toggleTemplate(template)}
                >
                  <div className="runtime-template-title">{template.name}</div>
                  <div className="runtime-template-subtitle">{template.timezone}</div>
                  <div className="runtime-template-meta">
                    {buildDaysLabel(template.days)}
                  </div>
                  <div className="runtime-template-meta">
                    {buildRangesLabel(template.timeRanges)}
                  </div>
                </button>
              );
            })}
          </div>
          <div className="runtime-config-hint">模板时间可精确到分钟。</div>
        </div>
      )}

      {safeConfig.scheduleType === 'Custom' && (
        <div className="runtime-config-section">
          <div className="runtime-config-row">
            <div className="trade-form-label">自定义类型</div>
            <div className="trade-chip-group">
              {(['Allow', 'Deny'] as const).map((mode) => (
                <button
                  key={mode}
                  type="button"
                  className={`trade-chip ${safeConfig.custom.mode === mode ? 'active' : ''}`}
                  onClick={() => updateCustom({ mode })}
                >
                  {mode === 'Allow' ? '交易时段' : '不交易时段'}
                </button>
              ))}
            </div>
          </div>

          <div className="runtime-config-row">
            <div className="trade-form-label">时区</div>
            <select
              className="runtime-timezone-select"
              value={safeConfig.custom.timezone}
              onChange={(event) => updateCustom({ timezone: event.target.value })}
            >
              {timezoneOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>

          <div className="runtime-config-row">
            <div className="trade-form-label">星期选择</div>
            <div className="runtime-day-grid">
              {DAY_OPTIONS.map((day) => (
                <button
                  key={day.value}
                  type="button"
                  className={`runtime-day-chip ${
                    safeConfig.custom.days.includes(day.value) ? 'active' : ''
                  }`}
                  onClick={() => toggleDay(day.value)}
                >
                  {day.label}
                </button>
              ))}
            </div>
          </div>

          <div className="runtime-config-row">
            <div className="trade-form-label">时间段</div>
            <div className="runtime-time-list">
              {safeConfig.custom.timeRanges.map((range, index) => (
                <div key={`${range.start}-${range.end}-${index}`} className="runtime-time-row">
                  <select
                    className="runtime-time-select"
                    value={range.start}
                    onChange={(event) => updateTimeRange(index, 'start', event.target.value)}
                  >
                    {HOUR_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                  <span className="runtime-time-separator">-</span>
                  <select
                    className="runtime-time-select"
                    value={range.end}
                    onChange={(event) => updateTimeRange(index, 'end', event.target.value)}
                  >
                    {HOUR_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    className="runtime-time-remove"
                    onClick={() => removeTimeRange(index)}
                  >
                    删除
                  </button>
                </div>
              ))}
              <button type="button" className="runtime-time-add" onClick={addTimeRange}>
                添加时间段
              </button>
            </div>
            <div className="runtime-config-hint">
              自定义时间仅支持整点，最小 1 小时；开始与结束相同时表示全天。
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default StrategyRuntimeConfigForm;

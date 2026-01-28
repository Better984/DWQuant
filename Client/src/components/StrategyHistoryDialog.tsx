import React, { useMemo } from 'react';
import type {
  StrategyConfig,
  StrategyLogicBranchConfig,
  StrategyMethodConfig,
  StrategyValueRef,
} from './StrategyModule.types';
import './StrategyHistoryDialog.css';

export type StrategyHistoryVersion = {
  versionId: number;
  versionNo: number;
  configJson?: StrategyConfig;
  createdAt?: string;
  changelog?: string;
  isPinned?: boolean;
};

type StrategyHistoryDialogProps = {
  versions: StrategyHistoryVersion[];
  selectedVersionId: number | null;
  onSelectVersion: (versionId: number) => void;
  onClose: () => void;
  isLoading?: boolean;
};

type DiffLineStatus = 'unchanged' | 'added' | 'removed';

type DiffLine = {
  text: string;
  status: DiffLineStatus;
};

type GroupStatus = 'empty' | 'removed' | 'added' | 'changed';

type DiffGroup = {
  index: number;
  title: string;
  leftLines: DiffLine[];
  rightLines: DiffLine[];
  leftStatus: GroupStatus;
  rightStatus: GroupStatus;
};

type DiffSection = {
  id: string;
  title: string;
  groups: DiffGroup[];
  summary: DiffSummary;
};

type GroupDisplay = {
  index: number;
  lines: string[];
};

type DiffSummary = {
  currentCount: number;
  previousCount: number;
  delta: number;
  addedIndicators: string[];
  removedIndicators: string[];
  addedOperators: string[];
  removedOperators: string[];
};

const formatValueRef = (value?: StrategyValueRef | string | null) => {
  if (value === null || value === undefined) {
    return '未配置';
  }
  if (typeof value === 'string') {
    return value;
  }

  const refType = (value.refType || '').toLowerCase();
  if (refType === 'const' || refType === 'number') {
    return value.input?.trim() || '0';
  }
  if (refType === 'field') {
    const field = value.input || 'Field';
    const timeframe = value.timeframe ? ` ${value.timeframe}` : '';
    return `${field}${timeframe}`.trim();
  }

  const indicator = value.indicator || 'Indicator';
  const timeframe = value.timeframe ? ` ${value.timeframe}` : '';
  const params = value.params && value.params.length > 0 ? `(${value.params.join(',')})` : '';
  const output = value.output ? `.${value.output}` : '';
  return `${indicator}${timeframe}${params}${output}`.trim();
};

const formatCondition = (condition: StrategyMethodConfig) => {
  const args = condition.args ?? [];
  const left = formatValueRef(args[0] as StrategyValueRef | string | undefined);
  const right = formatValueRef(args[1] as StrategyValueRef | string | undefined);
  const method = condition.method || 'Unknown';
  return `${left} ${method} ${right}`.trim();
};

const buildGroups = (branch?: StrategyLogicBranchConfig): GroupDisplay[] => {
  const groups = branch?.containers?.[0]?.checks?.groups ?? [];
  return groups.map((group, index) => ({
    index,
    lines: (group.conditions || []).map(formatCondition),
  }));
};

const collectConditions = (branch?: StrategyLogicBranchConfig): StrategyMethodConfig[] => {
  const containers = branch?.containers ?? [];
  const conditions: StrategyMethodConfig[] = [];
  containers.forEach((container) => {
    const groups = container?.checks?.groups ?? [];
    groups.forEach((group) => {
      (group.conditions ?? []).forEach((condition) => {
        conditions.push(condition);
      });
    });
  });
  return conditions;
};

const extractIndicators = (conditions: StrategyMethodConfig[]): Set<string> => {
  const indicators = new Set<string>();
  conditions.forEach((condition) => {
    (condition.args ?? []).forEach((arg) => {
      if (!arg || typeof arg !== 'object') {
        return;
      }
      const ref = arg as StrategyValueRef;
      const refType = (ref.refType || '').toLowerCase();
      if (refType === 'const' || refType === 'field') {
        return;
      }
      const name = (ref.indicator || ref.input || '').trim();
      if (name) {
        indicators.add(name);
      }
    });
  });
  return indicators;
};

const extractOperators = (conditions: StrategyMethodConfig[]): Set<string> => {
  const operators = new Set<string>();
  conditions.forEach((condition) => {
    const method = (condition.method || '').trim();
    if (method) {
      operators.add(method);
    }
  });
  return operators;
};

const diffSets = (current: Set<string>, previous: Set<string>) => {
  const added = Array.from(current).filter((item) => !previous.has(item));
  const removed = Array.from(previous).filter((item) => !current.has(item));
  const sorter = (a: string, b: string) => a.localeCompare(b);
  return {
    added: added.sort(sorter),
    removed: removed.sort(sorter),
  };
};

const buildSummary = (
  previousBranch?: StrategyLogicBranchConfig,
  currentBranch?: StrategyLogicBranchConfig,
): DiffSummary => {
  const previousConditions = collectConditions(previousBranch);
  const currentConditions = collectConditions(currentBranch);
  const previousIndicators = extractIndicators(previousConditions);
  const currentIndicators = extractIndicators(currentConditions);
  const previousOperators = extractOperators(previousConditions);
  const currentOperators = extractOperators(currentConditions);
  const indicatorDiff = diffSets(currentIndicators, previousIndicators);
  const operatorDiff = diffSets(currentOperators, previousOperators);

  return {
    currentCount: currentConditions.length,
    previousCount: previousConditions.length,
    delta: currentConditions.length - previousConditions.length,
    addedIndicators: indicatorDiff.added,
    removedIndicators: indicatorDiff.removed,
    addedOperators: operatorDiff.added,
    removedOperators: operatorDiff.removed,
  };
};

const buildDisplayLines = (
  lines: string[],
  status: GroupStatus,
  otherSet: Set<string>,
  isLeft: boolean,
): DiffLine[] => {
  if (status === 'empty') {
    return [{ text: '无此条件组', status: 'unchanged' }];
  }

  if (lines.length === 0) {
    const placeholderStatus: DiffLineStatus =
      status === 'removed' ? 'removed' : status === 'added' ? 'added' : 'unchanged';
    return [{ text: '（空条件组）', status: placeholderStatus }];
  }

  if (status === 'removed') {
    return lines.map((line) => ({ text: line, status: 'removed' }));
  }

  if (status === 'added') {
    return lines.map((line) => ({ text: line, status: 'added' }));
  }

  return lines.map((line) => ({
    text: line,
    status: otherSet.has(line) ? 'unchanged' : isLeft ? 'removed' : 'added',
  }));
};

const buildSections = (
  leftConfig?: StrategyConfig,
  rightConfig?: StrategyConfig,
): DiffSection[] => {
  const sections = [
    { id: 'open-long', title: '开多条件', pick: (config?: StrategyConfig) => config?.logic?.entry?.long },
    { id: 'open-short', title: '开空条件', pick: (config?: StrategyConfig) => config?.logic?.entry?.short },
    { id: 'close-long', title: '平多条件', pick: (config?: StrategyConfig) => config?.logic?.exit?.long },
    { id: 'close-short', title: '平空条件', pick: (config?: StrategyConfig) => config?.logic?.exit?.short },
  ];

  return sections.map((section) => {
    const leftGroups = buildGroups(section.pick(leftConfig));
    const rightGroups = buildGroups(section.pick(rightConfig));
    const summary = buildSummary(section.pick(leftConfig), section.pick(rightConfig));
    const maxGroups = Math.max(leftGroups.length, rightGroups.length, 1);
    const groups: DiffGroup[] = [];

    for (let index = 0; index < maxGroups; index += 1) {
      const leftGroup = leftGroups[index];
      const rightGroup = rightGroups[index];
      const leftLines = leftGroup?.lines ?? [];
      const rightLines = rightGroup?.lines ?? [];
      const rightSet = new Set(rightLines);
      const leftSet = new Set(leftLines);
      const hasOverlap =
        leftLines.length === 0 && rightLines.length === 0
          ? true
          : leftLines.some((line) => rightSet.has(line));

      const leftStatus: GroupStatus = !leftGroup
        ? 'empty'
        : !rightGroup || !hasOverlap
          ? 'removed'
          : 'changed';
      const rightStatus: GroupStatus = !rightGroup
        ? 'empty'
        : !leftGroup || !hasOverlap
          ? 'added'
          : 'changed';

      groups.push({
        index,
        title: `条件组 ${index + 1}`,
        leftLines: buildDisplayLines(leftLines, leftStatus, rightSet, true),
        rightLines: buildDisplayLines(rightLines, rightStatus, leftSet, false),
        leftStatus,
        rightStatus,
      });
    }

    return {
      id: section.id,
      title: section.title,
      groups,
      summary,
    };
  });
};

const StrategyHistoryDialog: React.FC<StrategyHistoryDialogProps> = ({
  versions,
  selectedVersionId,
  onSelectVersion,
  onClose,
  isLoading = false,
}) => {
  const sortedVersions = useMemo(() => {
    return [...versions].sort((a, b) => a.versionNo - b.versionNo);
  }, [versions]);

  const lastVersion = sortedVersions.length > 0 ? sortedVersions[sortedVersions.length - 1] : undefined;
  const resolvedSelectedId = selectedVersionId ?? lastVersion?.versionId ?? null;
  const selectedIndex = resolvedSelectedId
    ? sortedVersions.findIndex((item) => item.versionId === resolvedSelectedId)
    : -1;
  const selectedVersion = selectedIndex >= 0 ? sortedVersions[selectedIndex] : lastVersion;
  const previousVersion = selectedIndex > 0 ? sortedVersions[selectedIndex - 1] : null;

  const sections = useMemo(() => {
    return buildSections(previousVersion?.configJson, selectedVersion?.configJson);
  }, [previousVersion, selectedVersion]);

  const renderLine = (line: DiffLine, index: number) => {
    const prefix = line.status === 'added' ? '+' : line.status === 'removed' ? '-' : '·';
    return (
      <div
        className={`strategy-history-line is-${line.status}`}
        key={`${line.status}-${line.text}-${prefix}-${index}`}
      >
        <span className="strategy-history-line-prefix">{prefix}</span>
        <span className="strategy-history-line-text">{line.text}</span>
      </div>
    );
  };

  const buildSummaryText = (summary: DiffSummary) => {
    const parts: string[] = [];

    if (summary.delta !== 0) {
      const deltaText = summary.delta > 0 ? `新增${summary.delta}个条件` : `减少${Math.abs(summary.delta)}个条件`;
      parts.push(deltaText);
    }

    if (summary.addedIndicators.length > 0) {
      parts.push(`新增${summary.addedIndicators.length}个指标(${summary.addedIndicators.join(', ')})`);
    }
    if (summary.removedIndicators.length > 0) {
      parts.push(`移除${summary.removedIndicators.length}个指标(${summary.removedIndicators.join(', ')})`);
    }

    if (summary.addedOperators.length > 0) {
      parts.push(`新增${summary.addedOperators.length}个操作符(${summary.addedOperators.join(', ')})`);
    }
    if (summary.removedOperators.length > 0) {
      parts.push(`移除${summary.removedOperators.length}个操作符(${summary.removedOperators.join(', ')})`);
    }

    return parts.length > 0 ? parts.join('  ') : '无变更';
  };


  return (
    <div className="strategy-history-dialog">
      <div className="strategy-history-header">
        <div className="strategy-history-title">策略历史版本查看</div>
        <div className="strategy-history-versions">
          {sortedVersions.map((item) => (
            <button
              key={item.versionId}
              type="button"
              className={`strategy-history-version ${item.versionId === resolvedSelectedId ? 'is-active' : ''}`}
              onClick={() => onSelectVersion(item.versionId)}
            >
              v{item.versionNo}
            </button>
          ))}
        </div>
        <button className="strategy-history-close" type="button" onClick={onClose} aria-label="关闭">
          <svg width={20} height={20} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
              d="M18 6L6 18M6 6L18 18"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      </div>

      <div className="strategy-history-body">
        {isLoading ? (
          <div className="strategy-history-empty">加载中...</div>
        ) : !selectedVersion ? (
          <div className="strategy-history-empty">暂无版本记录</div>
        ) : (
          <>
            <div className="strategy-history-columns">
              <div className="strategy-history-column-title">
                {previousVersion ? `上一个版本 v${previousVersion.versionNo}` : '上一个版本'}
              </div>
              <div className="strategy-history-column-title">
                {selectedVersion ? `当前版本 v${selectedVersion.versionNo}` : '当前版本'}
              </div>
            </div>

            {sections.map((section) => (
              <div className="strategy-history-section" key={section.id}>
                <div className="strategy-history-section-title">{section.title}</div>
                <div className="strategy-history-section-summary">
                  {buildSummaryText(section.summary)}
                </div>
                <div className="strategy-history-section-grid">
                  <div className="strategy-history-panel">
                    {section.groups.map((group) => (
                      <div
                        key={`${section.id}-left-${group.index}`}
                        className={`strategy-history-group is-${group.leftStatus}`}
                      >
                        <div className="strategy-history-group-title">{group.title}</div>
                        <div className="strategy-history-lines">
                          {group.leftLines.map((line, index) => renderLine(line, index))}
                        </div>
                      </div>
                    ))}
                  </div>
                  <div className="strategy-history-panel">
                    {section.groups.map((group) => (
                      <div
                        key={`${section.id}-right-${group.index}`}
                        className={`strategy-history-group is-${group.rightStatus}`}
                      >
                        <div className="strategy-history-group-title">{group.title}</div>
                        <div className="strategy-history-lines">
                          {group.rightLines.map((line, index) => renderLine(line, index))}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            ))}
          </>
        )}
      </div>
    </div>
  );
};

export default StrategyHistoryDialog;

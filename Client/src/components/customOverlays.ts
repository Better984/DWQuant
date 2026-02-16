/**
 * 自定义绘图覆盖层 - 扩展 klinecharts 内置功能
 * 参考 KlineCharts Pro 实现
 */

import { registerOverlay } from "klinecharts";

// 计算两点之间的距离
function getDistance(p1: { x: number; y: number }, p2: { x: number; y: number }): number {
  const dx = p1.x - p2.x;
  const dy = p1.y - p2.y;
  return Math.sqrt(dx * dx + dy * dy);
}

type RiskDirection = "long" | "short";
type RiskHitType = "takeProfit" | "stopLoss" | "rangeEnd";
type RiskTradeResolution = {
  hasEntry: boolean;
  startIndex: number;
  endIndex: number;
  endPrice: number;
  hitType: RiskHitType;
};

type RiskBar = {
  timestamp: number;
  open: number;
  high: number;
  low: number;
  close: number;
};

const RISK_ZONE_COLORS = {
  takeProfitPlan: "rgba(34, 197, 94, 0.16)",
  stopLossPlan: "rgba(239, 68, 68, 0.20)",
  executedProfit: "rgba(21, 128, 61, 0.32)",
  executedLoss: "rgba(220, 38, 38, 0.32)",
} as const;
const DEFAULT_RISK_TAKE_PROFIT_PCT = 0.04;
const DEFAULT_RISK_STOP_LOSS_PCT = 0.02;
const EPSILON = 1e-9;

function toFiniteNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string") {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }
  return null;
}

function normalizeRiskDirection(value: unknown, fallback: RiskDirection): RiskDirection {
  if (value === "short") {
    return "short";
  }
  if (value === "long") {
    return "long";
  }
  return fallback;
}

function createRectCoordinates(leftX: number, rightX: number, topY: number, bottomY: number) {
  return [
    { x: leftX, y: topY },
    { x: rightX, y: topY },
    { x: rightX, y: bottomY },
    { x: leftX, y: bottomY },
  ];
}

function ensureRiskExtendData(overlay: { extendData?: unknown }): Record<string, unknown> {
  if (!overlay.extendData || typeof overlay.extendData !== "object") {
    overlay.extendData = {};
  }
  return overlay.extendData as Record<string, unknown>;
}

function getDefaultTakeProfitPrice(entryPrice: number, direction: RiskDirection): number {
  return direction === "long"
    ? entryPrice * (1 + DEFAULT_RISK_TAKE_PROFIT_PCT)
    : entryPrice * (1 - DEFAULT_RISK_TAKE_PROFIT_PCT);
}

function getDefaultStopLossPrice(entryPrice: number, direction: RiskDirection): number {
  return direction === "long"
    ? entryPrice * (1 - DEFAULT_RISK_STOP_LOSS_PCT)
    : entryPrice * (1 + DEFAULT_RISK_STOP_LOSS_PCT);
}

function normalizeRiskLevel(entryPrice: number, direction: RiskDirection, level: number, levelType: "takeProfit" | "stopLoss") {
  if (direction === "long") {
    if (levelType === "takeProfit") {
      return level > entryPrice + EPSILON ? level : getDefaultTakeProfitPrice(entryPrice, direction);
    }
    return level < entryPrice - EPSILON ? level : getDefaultStopLossPrice(entryPrice, direction);
  }
  if (levelType === "takeProfit") {
    return level < entryPrice - EPSILON ? level : getDefaultTakeProfitPrice(entryPrice, direction);
  }
  return level > entryPrice + EPSILON ? level : getDefaultStopLossPrice(entryPrice, direction);
}

function syncPointTime(target: Record<string, unknown>, source: Record<string, unknown>) {
  target.timestamp = source.timestamp;
  target.dataIndex = source.dataIndex;
}

function ensureRiskPointsStructure(
  points: Array<Record<string, unknown>>,
  direction: RiskDirection,
  options?: { resetRiskLevels?: boolean }
) {
  if (!points[0]) {
    points[0] = {};
  }
  const rawEntryPrice = toFiniteNumber(points[0].value);
  const entryPrice = rawEntryPrice ?? 1;
  points[0].value = entryPrice;

  if (!points[1]) {
    points[1] = {};
    syncPointTime(points[1], points[0]);
  }
  if (!Number.isFinite(toFiniteNumber(points[1].value))) {
    points[1].value = entryPrice;
  }
  points[1].value = entryPrice;

  if (!points[2]) {
    points[2] = {};
    syncPointTime(points[2], points[1]);
  }
  if (!points[3]) {
    points[3] = {};
    syncPointTime(points[3], points[1]);
  }
  syncPointTime(points[2], points[1]);
  syncPointTime(points[3], points[1]);

  const shouldResetRiskLevels = options?.resetRiskLevels ?? false;
  if (shouldResetRiskLevels || toFiniteNumber(points[2].value) === null) {
    points[2].value = getDefaultTakeProfitPrice(entryPrice, direction);
  }
  if (shouldResetRiskLevels || toFiniteNumber(points[3].value) === null) {
    points[3].value = getDefaultStopLossPrice(entryPrice, direction);
  }

  const takeProfit = normalizeRiskLevel(
    entryPrice,
    direction,
    toFiniteNumber(points[2].value) ?? getDefaultTakeProfitPrice(entryPrice, direction),
    "takeProfit"
  );
  const stopLoss = normalizeRiskLevel(
    entryPrice,
    direction,
    toFiniteNumber(points[3].value) ?? getDefaultStopLossPrice(entryPrice, direction),
    "stopLoss"
  );
  points[2].value = takeProfit;
  points[3].value = stopLoss;
}

function getRiskBarsFromOverlay(overlay: { extendData?: unknown }): RiskBar[] {
  const extendData = overlay.extendData as
    | {
        getBars?: () => unknown;
        bars?: unknown;
      }
    | undefined;
  let rawBars: unknown = [];
  if (typeof extendData?.getBars === "function") {
    try {
      rawBars = extendData.getBars();
    } catch {
      rawBars = [];
    }
  } else if (Array.isArray(extendData?.bars)) {
    rawBars = extendData?.bars;
  }
  if (!Array.isArray(rawBars)) {
    return [];
  }
  const bars: RiskBar[] = [];
  for (const item of rawBars) {
    if (!item || typeof item !== "object") {
      continue;
    }
    const candidate = item as Record<string, unknown>;
    const timestamp = toFiniteNumber(candidate.timestamp);
    const high = toFiniteNumber(candidate.high);
    const low = toFiniteNumber(candidate.low);
    const close = toFiniteNumber(candidate.close);
    const open = toFiniteNumber(candidate.open);
    if (
      timestamp === null ||
      high === null ||
      low === null ||
      close === null
    ) {
      continue;
    }
    bars.push({
      timestamp,
      high: Math.max(high, low),
      low: Math.min(high, low),
      close,
      open: open ?? close,
    });
  }
  bars.sort((a, b) => a.timestamp - b.timestamp);
  return bars;
}

function resolveRiskTradeWindow(options: {
  direction: RiskDirection;
  entryPrice: number;
  takeProfitPrice: number;
  stopLossPrice: number;
  bars: RiskBar[];
}): RiskTradeResolution {
  const { direction, entryPrice, takeProfitPrice, stopLossPrice, bars } = options;
  const startIndex = bars.findIndex((bar) => bar.low <= entryPrice + EPSILON && bar.high >= entryPrice - EPSILON);
  if (startIndex < 0) {
    return {
      hasEntry: false,
      startIndex: -1,
      endIndex: -1,
      endPrice: entryPrice,
      hitType: "rangeEnd",
    };
  }

  for (let i = startIndex; i < bars.length; i += 1) {
    const bar = bars[i];
    const hitTakeProfit = direction === "long"
      ? bar.high >= takeProfitPrice - EPSILON
      : bar.low <= takeProfitPrice + EPSILON;
    const hitStopLoss = direction === "long"
      ? bar.low <= stopLossPrice + EPSILON
      : bar.high >= stopLossPrice - EPSILON;

    if (!hitTakeProfit && !hitStopLoss) {
      continue;
    }

    let hitType: RiskHitType;
    if (hitTakeProfit && hitStopLoss) {
      // 单根K线同时触及时采用确定性规则，避免拖动过程中闪烁。
      if (direction === "long") {
        if (bar.open >= takeProfitPrice) {
          hitType = "takeProfit";
        } else if (bar.open <= stopLossPrice) {
          hitType = "stopLoss";
        } else {
          hitType = bar.open >= entryPrice ? "takeProfit" : "stopLoss";
        }
      } else if (bar.open <= takeProfitPrice) {
        hitType = "takeProfit";
      } else if (bar.open >= stopLossPrice) {
        hitType = "stopLoss";
      } else {
        hitType = bar.open <= entryPrice ? "takeProfit" : "stopLoss";
      }
    } else {
      hitType = hitTakeProfit ? "takeProfit" : "stopLoss";
    }

    return {
      hasEntry: true,
      startIndex,
      endIndex: i,
      endPrice: hitType === "takeProfit" ? takeProfitPrice : stopLossPrice,
      hitType,
    };
  }

  return {
    hasEntry: true,
    startIndex,
    endIndex: bars.length - 1,
    endPrice: bars[bars.length - 1].close,
    hitType: "rangeEnd",
  };
}

function resolveRiskDirectionWithOverlay(
  overlay: { extendData?: unknown },
  fallbackDirection: RiskDirection
): RiskDirection {
  const extendData = ensureRiskExtendData(overlay);
  const direction = normalizeRiskDirection(extendData.direction, fallbackDirection);
  extendData.direction = direction;
  return direction;
}

function setRiskLabelVisibilityState(
  overlay: { extendData?: unknown },
  key: "labelHovered" | "labelSelected",
  value: boolean
): void {
  const extendData = ensureRiskExtendData(overlay);
  extendData[key] = value;
}

function shouldShowRiskLabels(overlay: { extendData?: unknown; isDrawing?: () => boolean }): boolean {
  const extendData = ensureRiskExtendData(overlay);
  const isHovered = extendData.labelHovered === true;
  const isSelected = extendData.labelSelected === true;
  const isDrawing = typeof overlay.isDrawing === "function" ? overlay.isDrawing() : false;
  return isHovered || isSelected || isDrawing;
}

function createRiskRewardOverlay(name: "riskRewardLong" | "riskRewardShort", fallbackDirection: RiskDirection) {
  return {
    name,
    totalStep: 3,
    needDefaultPointFigure: true,
    needDefaultXAxisFigure: true,
    needDefaultYAxisFigure: true,
    onDrawStart: ({ overlay }: { overlay: { extendData?: unknown } }) => {
      resolveRiskDirectionWithOverlay(overlay, fallbackDirection);
      return false;
    },
    onMouseEnter: ({ overlay }: { overlay: { extendData?: unknown } }) => {
      setRiskLabelVisibilityState(overlay, "labelHovered", true);
      return false;
    },
    onMouseLeave: ({ overlay }: { overlay: { extendData?: unknown } }) => {
      setRiskLabelVisibilityState(overlay, "labelHovered", false);
      return false;
    },
    onSelected: ({ overlay }: { overlay: { extendData?: unknown } }) => {
      setRiskLabelVisibilityState(overlay, "labelSelected", true);
      return false;
    },
    onDeselected: ({ overlay }: { overlay: { extendData?: unknown } }) => {
      setRiskLabelVisibilityState(overlay, "labelSelected", false);
      return false;
    },
    performEventMoveForDrawing: function ({
      currentStep,
      points,
      performPoint,
    }: {
      currentStep: number;
      points: Array<Record<string, unknown>>;
      performPoint: Record<string, unknown>;
    }) {
      const direction = resolveRiskDirectionWithOverlay(this as { extendData?: unknown }, fallbackDirection);
      if (currentStep < 2) {
        return;
      }
      if (currentStep === 2) {
        if (!points[1]) {
          points[1] = {};
        }
        points[1].timestamp = performPoint.timestamp;
        points[1].dataIndex = performPoint.dataIndex;
        points[1].value = points[0]?.value;
        ensureRiskPointsStructure(points, direction, { resetRiskLevels: true });
        return;
      }
      ensureRiskPointsStructure(points, direction);
    },
    performEventPressedMove: function ({
      points,
      performPointIndex,
    }: {
      points: Array<Record<string, unknown>>;
      performPointIndex: number;
    }) {
      const direction = resolveRiskDirectionWithOverlay(this as { extendData?: unknown }, fallbackDirection);
      ensureRiskPointsStructure(points, direction);

      if (performPointIndex === 1) {
        points[1].value = points[0]?.value;
        syncPointTime(points[2], points[1]);
        syncPointTime(points[3], points[1]);
      }
      if (performPointIndex === 2 || performPointIndex === 3) {
        syncPointTime(points[2], points[1]);
        syncPointTime(points[3], points[1]);
      }

      ensureRiskPointsStructure(points, direction);
    },
    createPointFigures: ({
      coordinates,
      overlay,
      precision,
      yAxis,
    }: {
      coordinates: { x: number; y: number }[];
      overlay: {
        points?: Array<Record<string, unknown>>;
        extendData?: unknown;
      };
      precision: { price: number };
      yAxis?: { convertToPixel?: (value: number) => number } | null;
    }) => {
      if (coordinates.length < 2) {
        return [];
      }

      const overlayPoints = Array.isArray(overlay.points) ? overlay.points : [];
      if (overlayPoints.length < 2) {
        return [];
      }
      const direction = resolveRiskDirectionWithOverlay(overlay, fallbackDirection);
      const points = overlayPoints as Array<Record<string, unknown>>;
      if (points.length >= 4) {
        ensureRiskPointsStructure(points, direction);
      }

      const entryCoord = coordinates[0];
      const rangeCoord = coordinates[1];
      const takeProfitCoord = coordinates[2];
      const stopLossCoord = coordinates[3];
      const xStart = Math.min(entryCoord.x, rangeCoord.x);
      const xEnd = Math.max(entryCoord.x, rangeCoord.x);
      const entryY = entryCoord.y;
      const entryValue = toFiniteNumber(points[0]?.value);
      if (entryValue === null) {
        return [];
      }
      const takeProfitDefault = getDefaultTakeProfitPrice(entryValue, direction);
      const stopLossDefault = getDefaultStopLossPrice(entryValue, direction);
      const takeProfitValue = normalizeRiskLevel(
        entryValue,
        direction,
        toFiniteNumber(points[2]?.value) ?? takeProfitDefault,
        "takeProfit"
      );
      const stopLossValue = normalizeRiskLevel(
        entryValue,
        direction,
        toFiniteNumber(points[3]?.value) ?? stopLossDefault,
        "stopLoss"
      );

      const toYByPrice = (price: number, fallback: number): number => {
        if (typeof yAxis?.convertToPixel === "function") {
          const y = yAxis.convertToPixel(price);
          if (Number.isFinite(y)) {
            return y;
          }
        }
        return fallback;
      };
      const takeProfitY = takeProfitCoord?.y ?? toYByPrice(takeProfitValue, entryY);
      const stopLossY = stopLossCoord?.y ?? toYByPrice(stopLossValue, entryY);

      const entryTimestamp = toFiniteNumber(points[0]?.timestamp);
      const rangeTimestamp = toFiniteNumber(points[1]?.timestamp);
      const rangeStartTs =
        entryTimestamp !== null && rangeTimestamp !== null
          ? Math.min(entryTimestamp, rangeTimestamp)
          : null;
      const rangeEndTs =
        entryTimestamp !== null && rangeTimestamp !== null
          ? Math.max(entryTimestamp, rangeTimestamp)
          : null;
      const bars =
        rangeStartTs === null || rangeEndTs === null
          ? []
          : getRiskBarsFromOverlay(overlay).filter((bar) => bar.timestamp >= rangeStartTs && bar.timestamp <= rangeEndTs);

      const trade =
        bars.length > 0
          ? resolveRiskTradeWindow({
              direction,
              entryPrice: entryValue,
              takeProfitPrice: takeProfitValue,
              stopLossPrice: stopLossValue,
              bars,
            })
          : {
              hasEntry: false,
              startIndex: -1,
              endIndex: -1,
              endPrice: entryValue,
              hitType: "rangeEnd" as RiskHitType,
            };

      const extendData = ensureRiskExtendData(overlay);
      extendData.execution = {
        hasEntry: trade.hasEntry,
        hitType: trade.hitType,
        endPrice: trade.endPrice,
        startTimestamp: trade.hasEntry ? bars[trade.startIndex]?.timestamp ?? null : null,
        endTimestamp: trade.hasEntry ? bars[trade.endIndex]?.timestamp ?? null : null,
      };

      const figures: Array<Record<string, unknown>> = [];

      const takeTop = Math.min(entryY, takeProfitY);
      const takeBottom = Math.max(entryY, takeProfitY);
      figures.push({
        type: "polygon",
        attrs: { coordinates: createRectCoordinates(xStart, xEnd, takeTop, takeBottom) },
        styles: {
          style: "fill",
          color: RISK_ZONE_COLORS.takeProfitPlan,
          borderSize: 0,
          borderColor: "rgba(0, 0, 0, 0)",
        },
      });
      figures.push({
        type: "line",
        attrs: {
          coordinates: [
            { x: xStart, y: takeProfitY },
            { x: xEnd, y: takeProfitY },
          ],
        },
        styles: {
          style: "dashed",
          size: 1,
          color: "rgba(22, 163, 74, 0.88)",
          dashedValue: [5, 4],
          smooth: false,
        },
      });

      const stopTop = Math.min(entryY, stopLossY);
      const stopBottom = Math.max(entryY, stopLossY);
      figures.push({
        type: "polygon",
        attrs: { coordinates: createRectCoordinates(xStart, xEnd, stopTop, stopBottom) },
        styles: {
          style: "fill",
          color: RISK_ZONE_COLORS.stopLossPlan,
          borderSize: 0,
          borderColor: "rgba(0, 0, 0, 0)",
        },
      });
      figures.push({
        type: "line",
        attrs: {
          coordinates: [
            { x: xStart, y: stopLossY },
            { x: xEnd, y: stopLossY },
          ],
        },
        styles: {
          style: "dashed",
          size: 1,
          color: "rgba(220, 38, 38, 0.9)",
          dashedValue: [5, 4],
          smooth: false,
        },
      });

      let executedColor: string | null = null;
      let executedStartX = xStart;
      let executedEndX = xStart;
      let executedEndY = entryY;
      let executedLabel = "未触发开仓";
      if (trade.hasEntry && bars.length > 0) {
        const ratioByIndex = (index: number) => {
          if (bars.length <= 1) {
            return 0;
          }
          return index / (bars.length - 1);
        };
        const startRatio = ratioByIndex(trade.startIndex);
        const endRatio = ratioByIndex(trade.endIndex);
        executedStartX = xStart + (xEnd - xStart) * startRatio;
        executedEndX = xStart + (xEnd - xStart) * endRatio;
        executedEndY = toYByPrice(trade.endPrice, entryY);
        const isProfit = direction === "long" ? trade.endPrice >= entryValue : trade.endPrice <= entryValue;
        executedColor = isProfit ? RISK_ZONE_COLORS.executedProfit : RISK_ZONE_COLORS.executedLoss;
        executedLabel =
          trade.hitType === "takeProfit"
            ? "触发止盈"
            : trade.hitType === "stopLoss"
              ? "触发止损"
              : "区间末平仓";

        let executedTop = Math.min(entryY, executedEndY);
        let executedBottom = Math.max(entryY, executedEndY);
        if (Math.abs(executedBottom - executedTop) <= Number.EPSILON) {
          const delta = Math.max(Math.abs(entryY) * 0.0005, 0.2);
          executedTop -= delta;
          executedBottom += delta;
        }
        figures.push({
          type: "polygon",
          attrs: { coordinates: createRectCoordinates(executedStartX, executedEndX, executedTop, executedBottom) },
          styles: {
            style: "fill",
            color: executedColor,
            borderSize: 0,
            borderColor: "rgba(0, 0, 0, 0)",
          },
        });
        figures.push({
          type: "line",
          attrs: {
            coordinates: [
              { x: executedStartX, y: entryY },
              { x: executedEndX, y: executedEndY },
            ],
          },
          styles: {
            style: "dashed",
            size: 0.8,
            color: "rgba(30, 41, 59, 0.45)",
            dashedValue: [5, 4],
            smooth: false,
          },
        });
      }

      if (shouldShowRiskLabels(overlay as { extendData?: unknown; isDrawing?: () => boolean })) {
        const textX = xEnd + 8;
        const displayPrecision =
          typeof precision.price === "number" && Number.isFinite(precision.price)
            ? Math.max(0, Math.min(precision.price, 8))
            : 2;
        const texts: Array<{ x: number; y: number; text: string; baseline: string }> = [];

        if (takeProfitValue !== null && coordinates.length >= 3) {
          texts.push({
            x: textX,
            y: takeProfitY,
            text: `止盈 ${takeProfitValue.toFixed(displayPrecision)}`,
            baseline: "middle",
          });
        }
        if (stopLossValue !== null && coordinates.length >= 4) {
          texts.push({
            x: textX,
            y: stopLossY,
            text: `止损 ${stopLossValue.toFixed(displayPrecision)}`,
            baseline: "middle",
          });
        }
        if (entryValue !== null) {
          texts.push({
            x: textX,
            y: entryY,
            text: `开仓 ${entryValue.toFixed(displayPrecision)}`,
            baseline: "middle",
          });
        }
        if (trade.hasEntry && Math.abs(entryValue) > Number.EPSILON) {
          const pnlPct = direction === "long" ? (trade.endPrice - entryValue) / entryValue : (entryValue - trade.endPrice) / entryValue;
          texts.push({
            x: textX,
            y: (entryY + executedEndY) / 2,
            text: `真实区间 ${(pnlPct * 100).toFixed(2)}% (${executedLabel})`,
            baseline: "middle",
          });
        } else {
          texts.push({
            x: textX,
            y: entryY - 14,
            text: "真实区间 未触发开仓",
            baseline: "middle",
          });
        }

        if (texts.length > 0) {
          figures.push({
            type: "text",
            ignoreEvent: true,
            attrs: texts,
          });
        }
      }

      return figures;
    },
  };
}

// 圆形
const circleOverlay = {
  name: "circle",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  styles: {
    circle: {
      color: "rgba(22, 119, 255, 0.15)",
    },
  },
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      const r = getDistance(coordinates[0], coordinates[1]);
      return {
        type: "circle",
        attrs: {
          ...coordinates[0],
          r,
        },
        styles: { style: "stroke_fill" },
      };
    }
    return [];
  },
};

// 矩形
const rectOverlay = {
  name: "rect",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  styles: {
    polygon: {
      color: "rgba(22, 119, 255, 0.15)",
    },
  },
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      return [
        {
          type: "polygon",
          attrs: {
            coordinates: [
              coordinates[0],
              { x: coordinates[1].x, y: coordinates[0].y },
              coordinates[1],
              { x: coordinates[0].x, y: coordinates[1].y },
            ],
          },
          styles: { style: "stroke_fill" },
        },
      ];
    }
    return [];
  },
};

// 平行四边形
const parallelogramOverlay = {
  name: "parallelogram",
  totalStep: 4,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  styles: {
    polygon: {
      color: "rgba(22, 119, 255, 0.15)",
    },
  },
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length === 2) {
      return [
        {
          type: "line",
          ignoreEvent: true,
          attrs: { coordinates },
        },
      ];
    }
    if (coordinates.length === 3) {
      const p4 = {
        x: coordinates[0].x + (coordinates[2].x - coordinates[1].x),
        y: coordinates[2].y,
      };
      return [
        {
          type: "polygon",
          attrs: {
            coordinates: [coordinates[0], coordinates[1], coordinates[2], p4],
          },
          styles: { style: "stroke_fill" },
        },
      ];
    }
    return [];
  },
};

// 三角形
const triangleOverlay = {
  name: "triangle",
  totalStep: 4,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  styles: {
    polygon: {
      color: "rgba(22, 119, 255, 0.15)",
    },
  },
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => [
    {
      type: "polygon",
      attrs: { coordinates },
      styles: { style: "stroke_fill" },
    },
  ],
};

// 十字星（单点，横纵向无限延展）
const crossStarOverlay = {
  name: "crossStar",
  totalStep: 2,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({
    coordinates,
    bounding,
  }: {
    coordinates: { x: number; y: number }[];
    bounding: { width: number; height: number };
  }) => {
    if (coordinates.length > 0) {
      const point = coordinates[0];
      return [
        {
          type: "line",
          attrs: {
            coordinates: [
              { x: 0, y: point.y },
              { x: bounding.width, y: point.y },
            ],
          },
        },
        {
          type: "line",
          attrs: {
            coordinates: [
              { x: point.x, y: 0 },
              { x: point.x, y: bounding.height },
            ],
          },
        },
        {
          type: "circle",
          attrs: {
            x: point.x,
            y: point.y,
            r: 2.5,
          },
          styles: { style: "fill" },
        },
      ];
    }
    return [];
  },
};

// 斐波那契圆
const fibonacciCircleOverlay = {
  name: "fibonacciCircle",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      const baseRadius = getDistance(coordinates[0], coordinates[1]);
      const ratios = [0.236, 0.382, 0.5, 0.618, 0.786, 1];
      const circles: { x: number; y: number; r: number }[] = [];
      const texts: { x: number; y: number; text: string }[] = [];

      ratios.forEach((ratio) => {
        const r = baseRadius * ratio;
        circles.push({ ...coordinates[0], r });
        texts.push({
          x: coordinates[0].x,
          y: coordinates[0].y + r + 6,
          text: `${(ratio * 100).toFixed(1)}%`,
        });
      });

      return [
        {
          type: "circle",
          attrs: circles,
          styles: { style: "stroke" },
        },
        {
          type: "text",
          ignoreEvent: true,
          attrs: texts,
        },
      ];
    }
    return [];
  },
};

// 斐波那契线段
const fibonacciSegmentOverlay = {
  name: "fibonacciSegment",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({
    coordinates,
    overlay,
    precision,
  }: {
    coordinates: { x: number; y: number }[];
    overlay: { points: { value: number }[] };
    precision: { price: number };
  }) => {
    const lines: { coordinates: { x: number; y: number }[] }[] = [];
    const texts: { x: number; y: number; text: string; baseline: string }[] = [];

    if (coordinates.length > 1) {
      const minX = Math.min(coordinates[0].x, coordinates[1].x);
      const ratios = [1, 0.786, 0.618, 0.5, 0.382, 0.236, 0];
      const yDiff = coordinates[0].y - coordinates[1].y;
      const points = overlay.points;
      const valueDiff = points[0].value - points[1].value;

      ratios.forEach((ratio) => {
        const y = coordinates[1].y + yDiff * ratio;
        const price = (points[1].value + valueDiff * ratio).toFixed(precision.price);
        lines.push({
          coordinates: [
            { x: coordinates[0].x, y },
            { x: coordinates[1].x, y },
          ],
        });
        texts.push({
          x: minX,
          y,
          text: `${price} (${(ratio * 100).toFixed(1)}%)`,
          baseline: "bottom",
        });
      });
    }

    return [
      { type: "line", attrs: lines },
      { type: "text", ignoreEvent: true, attrs: texts },
    ];
  },
};

// 斐波那契螺旋（简化版）
const fibonacciSpiralOverlay = {
  name: "fibonacciSpiral",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      const baseRadius = getDistance(coordinates[0], coordinates[1]) / Math.sqrt(24);
      const arcs: { x: number; y: number; r: number; startAngle: number; endAngle: number }[] = [];
      const fibNumbers = [1, 1, 2, 3, 5, 8, 13];
      let currentAngle = 0;

      fibNumbers.forEach((fib) => {
        const r = baseRadius * fib;
        arcs.push({
          x: coordinates[0].x,
          y: coordinates[0].y,
          r,
          startAngle: currentAngle,
          endAngle: currentAngle + Math.PI / 2,
        });
        currentAngle += Math.PI / 2;
      });

      return [
        {
          type: "arc",
          attrs: arcs,
          styles: { style: "stroke" },
        },
      ];
    }
    return [];
  },
};

// 斐波那契速度阻力扇
const fibonacciSpeedResistanceFanOverlay = {
  name: "fibonacciSpeedResistanceFan",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      const lines: { coordinates: { x: number; y: number }[] }[] = [];
      const ratios = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1];
      const yDiff = coordinates[1].y - coordinates[0].y;

      ratios.forEach((ratio) => {
        const endY = coordinates[0].y + yDiff * ratio;
        lines.push({
          coordinates: [coordinates[0], { x: coordinates[1].x, y: endY }],
        });
      });

      return [{ type: "line", attrs: lines }];
    }
    return [];
  },
};

// 斐波那契扩展
const fibonacciExtensionOverlay = {
  name: "fibonacciExtension",
  totalStep: 4,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({
    coordinates,
    overlay,
    precision,
  }: {
    coordinates: { x: number; y: number }[];
    overlay: { points: { value: number }[] };
    precision: { price: number };
  }) => {
    const lines: { coordinates: { x: number; y: number }[] }[] = [];
    const texts: { x: number; y: number; text: string; baseline: string }[] = [];

    if (coordinates.length > 2) {
      const ratios = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1, 1.618, 2.618];
      const yDiff = coordinates[0].y - coordinates[1].y;
      const points = overlay.points;
      const valueDiff = points[0].value - points[1].value;

      ratios.forEach((ratio) => {
        const y = coordinates[2].y + yDiff * ratio;
        const price = (points[2].value + valueDiff * ratio).toFixed(precision.price);
        lines.push({
          coordinates: [
            { x: coordinates[2].x - 50, y },
            { x: coordinates[2].x + 200, y },
          ],
        });
        texts.push({
          x: coordinates[2].x + 205,
          y,
          text: `${price} (${(ratio * 100).toFixed(1)}%)`,
          baseline: "middle",
        });
      });
    }

    return [
      { type: "line", attrs: [...coordinates.slice(0, 3).map((_, i, arr) => i < arr.length - 1 ? { coordinates: [arr[i], arr[i + 1]] } : null).filter(Boolean), ...lines] },
      { type: "text", ignoreEvent: true, attrs: texts },
    ];
  },
};

// 江恩箱
const gannBoxOverlay = {
  name: "gannBox",
  totalStep: 3,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    if (coordinates.length > 1) {
      const lines: { coordinates: { x: number; y: number }[] }[] = [];
      const [p1, p2] = coordinates;

      // 外框
      lines.push({
        coordinates: [p1, { x: p2.x, y: p1.y }, p2, { x: p1.x, y: p2.y }, p1],
      });

      // 对角线
      lines.push({ coordinates: [p1, p2] });
      lines.push({ coordinates: [{ x: p1.x, y: p2.y }, { x: p2.x, y: p1.y }] });

      // 水平分割线
      const yMid = (p1.y + p2.y) / 2;
      lines.push({ coordinates: [{ x: p1.x, y: yMid }, { x: p2.x, y: yMid }] });

      // 垂直分割线
      const xMid = (p1.x + p2.x) / 2;
      lines.push({ coordinates: [{ x: xMid, y: p1.y }, { x: xMid, y: p2.y }] });

      return [{ type: "line", attrs: lines }];
    }
    return [];
  },
};

// XABCD 形态
const xabcdOverlay = {
  name: "xabcd",
  totalStep: 6,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    const lines: { coordinates: { x: number; y: number }[] }[] = [];
    const texts: { x: number; y: number; text: string; baseline: string }[] = [];
    const labels = ["X", "A", "B", "C", "D"];

    coordinates.forEach((coord, index) => {
      if (index < coordinates.length - 1) {
        lines.push({ coordinates: [coord, coordinates[index + 1]] });
      }
      if (index < labels.length) {
        texts.push({
          x: coord.x,
          y: coord.y - 10,
          text: labels[index],
          baseline: "bottom",
        });
      }
    });

    return [
      { type: "line", attrs: lines },
      { type: "text", ignoreEvent: true, attrs: texts },
    ];
  },
};

// ABCD 形态
const abcdOverlay = {
  name: "abcd",
  totalStep: 5,
  needDefaultPointFigure: true,
  needDefaultXAxisFigure: true,
  needDefaultYAxisFigure: true,
  createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
    const lines: { coordinates: { x: number; y: number }[] }[] = [];
    const texts: { x: number; y: number; text: string; baseline: string }[] = [];
    const labels = ["A", "B", "C", "D"];

    coordinates.forEach((coord, index) => {
      if (index < coordinates.length - 1) {
        lines.push({ coordinates: [coord, coordinates[index + 1]] });
      }
      if (index < labels.length) {
        texts.push({
          x: coord.x,
          y: coord.y - 10,
          text: labels[index],
          baseline: "bottom",
        });
      }
    });

    return [
      { type: "line", attrs: lines },
      { type: "text", ignoreEvent: true, attrs: texts },
    ];
  },
};

// 通用波浪形态
function createWaveOverlay(name: string, waveCount: number) {
  return {
    name,
    totalStep: waveCount + 1,
    needDefaultPointFigure: true,
    needDefaultXAxisFigure: true,
    needDefaultYAxisFigure: true,
    createPointFigures: ({ coordinates }: { coordinates: { x: number; y: number }[] }) => {
      const lines: { coordinates: { x: number; y: number }[] }[] = [];
      const texts: { x: number; y: number; text: string; baseline: string }[] = [];

      coordinates.forEach((coord, index) => {
        if (index < coordinates.length - 1) {
          lines.push({ coordinates: [coord, coordinates[index + 1]] });
        }
        texts.push({
          x: coord.x,
          y: coord.y - 10,
          text: `${index + 1}`,
          baseline: "bottom",
        });
      });

      return [
        { type: "line", attrs: lines },
        { type: "text", ignoreEvent: true, attrs: texts },
      ];
    },
  };
}

// 注册所有自定义 overlay
export function registerCustomOverlays(): void {
  // 形状
  registerOverlay(circleOverlay as never);
  registerOverlay(rectOverlay as never);
  registerOverlay(parallelogramOverlay as never);
  registerOverlay(triangleOverlay as never);
  registerOverlay(crossStarOverlay as never);

  // 斐波那契
  registerOverlay(fibonacciCircleOverlay as never);
  registerOverlay(fibonacciSegmentOverlay as never);
  registerOverlay(fibonacciSpiralOverlay as never);
  registerOverlay(fibonacciSpeedResistanceFanOverlay as never);
  registerOverlay(fibonacciExtensionOverlay as never);
  registerOverlay(gannBoxOverlay as never);

  // 波浪形态
  registerOverlay(xabcdOverlay as never);
  registerOverlay(abcdOverlay as never);
  registerOverlay(createWaveOverlay("threeWaves", 3) as never);
  registerOverlay(createWaveOverlay("fiveWaves", 5) as never);
  registerOverlay(createWaveOverlay("eightWaves", 8) as never);
  registerOverlay(createWaveOverlay("anyWaves", 10) as never); // 任意浪最多10个点

  // 风险收益区（多头/空头）
  registerOverlay(createRiskRewardOverlay("riskRewardLong", "long") as never);
  registerOverlay(createRiskRewardOverlay("riskRewardShort", "short") as never);
}

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

      fibNumbers.forEach((fib, index) => {
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
          text: `${(ratio * 100).toFixed(1)}%`,
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
  registerOverlay(circleOverlay);
  registerOverlay(rectOverlay);
  registerOverlay(parallelogramOverlay);
  registerOverlay(triangleOverlay);

  // 斐波那契
  registerOverlay(fibonacciCircleOverlay);
  registerOverlay(fibonacciSegmentOverlay);
  registerOverlay(fibonacciSpiralOverlay);
  registerOverlay(fibonacciSpeedResistanceFanOverlay);
  registerOverlay(fibonacciExtensionOverlay);
  registerOverlay(gannBoxOverlay);

  // 波浪形态
  registerOverlay(xabcdOverlay);
  registerOverlay(abcdOverlay);
  registerOverlay(createWaveOverlay("threeWaves", 3));
  registerOverlay(createWaveOverlay("fiveWaves", 5));
  registerOverlay(createWaveOverlay("eightWaves", 8));
  registerOverlay(createWaveOverlay("anyWaves", 10)); // 任意浪最多10个点
}

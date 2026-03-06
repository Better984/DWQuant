# AI助手知识库

## 1. 目标
- 将用户自然语言需求转换为**平台可执行**的 `strategyConfig`。
- 必须输出严格 JSON，不允许 markdown 包裹，不允许额外解释文本。

## 2. 输出协议（必须严格遵循）
- 顶层必须是：
```json
{
  "assistantReply": "给用户看的中文说明",
  "strategyConfig": { ... },
  "suggestedQuestions": ["可选的后续快捷提问，最多3条"]
}
```
- 若用户只是咨询，不要求生成策略：`strategyConfig = null`。
- 若用户要求生成策略：`strategyConfig` 必须为对象，且必须可执行。
- `suggestedQuestions` 可为空数组；若返回，最多 3 条，且必须是简短中文问题。

## 3. strategyConfig 结构（必须）
- 顶层：`trade`、`logic`、`runtime`。
- `trade` 必填字段：`exchange`、`symbol`、`timeframeSec`、`positionMode`、`openConflictPolicy`、`sizing`、`risk`。
- `logic` 必填字段：`entry`、`exit`。每个 side（long/short）都要有：
  - `enabled`、`minPassConditionContainer`、`containers`、`onPass`。
  - `entry.long` 与 `entry.short` 的 `containers[].checks.groups[].conditions[]` **不能为空**。
  - `exit.long` 与 `exit.short` 可按策略风格保留空 `groups`，但 `onPass` 动作结构必须完整。
- `runtime` 必填字段：`scheduleType`、`outOfSessionPolicy`、`templateIds`、`templates`、`custom`。

## 4. 条件与动作的真实映射（按项目代码）
### 4.1 条件节点（StrategyMethod）
- 字段：`enabled`、`required`、`method`、`args`、`param`。
- `args`：值引用数组（对象结构见 4.3）。
- `param`：字符串数组，作为窗口/阈值等辅助参数。
### 4.2 动作节点（MakeTrade）
- `method` 固定 `MakeTrade`。
- 标准写法：`param: ["Long"|"Short"|"CloseLong"|"CloseShort"]`。
- `args` 必须为空数组，禁止写成 `["Long"]` 这类字符串数组。
### 4.3 值引用（StrategyValueRef）
- 字段：`refType`、`indicator`、`timeframe`、`input`、`params`、`output`、`offsetRange`、`calcMode`。
- `refType`：`Field` / `Indicator` / `Const`。
- `input` 可用值（来自 TalibCalcRules）：`OPEN`、`HIGH`、`LOW`、`CLOSE`、`VOLUME`、`HL2`、`HLC3`、`OHLC4`、`OC2`、`HLCC4`（大小写不敏感）。
- `timeframe` 可为空；为空时自动使用 `trade.timeframeSec` 对应周期。
- `timeframe` 推荐格式：`1m/3m/5m/15m/30m/1h/2h/4h/6h/8h/12h/1d/3d/1w/1mo`。
- `output`：对于单输出指标，推荐 `Real` 或 `Value`；多输出指标必须使用表中输出键/Hint。
- `calcMode` 推荐 `OnBarClose`。

## 5. 条件方法白名单（必须使用以下 method）
### 5.1 实现依据（必须按代码语义）
- `SeverTest/Modules/StrategyEngine/Domain/ConditionMethodRegistry.cs`
- `SeverTest/Modules/StrategyEngine/Application/IndicatorValueResolver.cs`
- 只允许使用 `ConditionMethodRegistry.Methods` 字典内登记的方法名，禁止自造 method。

### 5.2 通用取值与参数解析规则（非常关键）
- `args` 优先，`param` 兜底：
  - 引擎取值时先读 `args[i]`。
  - 若 `args` 不足，会继续读取 `param`（按 `IndicatorValueResolver.TryResolveValues` 规则）。
- `param` 的类型是 **字符串数组**（`StrategyMethod.Param: string[]`）：
  - 正确示例：`"param": ["20", "0.5"]`。
  - `args[].params` 才是数值数组（如 `[15]`）。
- 偏移规则：
  - 解析值时使用 `effectiveOffset = baseOffset + offsetAdd`。
  - `baseOffset` 来自 `args[].offsetRange` 的最小值（最小值再做 `>=0` 约束）。
  - 交叉类会分别读取 `offsetAdd=0`（当前）和 `offsetAdd=1`（前一根）。
- `calcMode = "OnBarClose"` 时，非收线任务会拒绝取值（条件会失败）。
- 常量来源：
  - 可用 `refType = "Const"` 的 `args`。
  - 或把常量写在 `param`（字符串数值）中。

### 5.3 方法清单与精确语义（28 个，含别名）
| method | 语义（严格按代码） | args 要求 | param 用法 | 判定公式 |
|---|---|---|---|---|
| GreaterThanOrEqual | 大于等于 | 至少 2 个值（A,B） | 可兜底 A/B | `A >= B` |
| GreaterThan | 大于 | 至少 2 个值（A,B） | 可兜底 A/B | `A > B` |
| LessThan | 小于 | 至少 2 个值（A,B） | 可兜底 A/B | `A < B` |
| LessThanOrEqual | 小于等于 | 至少 2 个值（A,B） | 可兜底 A/B | `A <= B` |
| Equal | 等于（浮点容差） | 至少 2 个值（A,B） | 可兜底 A/B | `abs(A-B) < 1e-10` |
| NotEqual | 不等于（浮点容差） | 至少 2 个值（A,B） | 可兜底 A/B | `abs(A-B) >= 1e-10` |
| CrossUp | 上穿 | 必须 2 个序列（A,B） | 不建议用 param | `A[-1] <= B[-1] && A[0] > B[0]` |
| CrossOver | 上穿别名，等价 CrossUp | 同 CrossUp | 同 CrossUp | 同 CrossUp |
| CrossDown | 下穿 | 必须 2 个序列（A,B） | 不建议用 param | `A[-1] >= B[-1] && A[0] < B[0]` |
| CrossUnder | 下穿别名，等价 CrossDown | 同 CrossDown | 同 CrossDown | 同 CrossDown |
| CrossAny | 任意穿越 | 必须 2 个序列（A,B） | 不建议用 param | `(A[-1] <= B[-1] && A[0] > B[0]) || (A[-1] >= B[-1] && A[0] < B[0])` |
| Between | 区间内 | 3 个值（X,Low,High） | 可兜底 | `X >= min(Low,High) && X <= max(Low,High)` |
| Outside | 区间外 | 3 个值（X,Low,High） | 可兜底 | `X < min(Low,High) || X > max(Low,High)` |
| Rising | 连续上涨（严格递增） | arg0=主序列 | `param[0]=N`（可由 arg1 常量兜底） | 连续 N 步满足 `S[i] > S[i+1]` |
| Falling | 连续下跌（严格递减） | arg0=主序列 | `param[0]=N`（可由 arg1 常量兜底） | 连续 N 步满足 `S[i] < S[i+1]` |
| AboveFor | 连续高于阈值 | arg0=主序列，arg1=阈值序列可选 | `param[0]=N`；若无 arg1 则 `param[1]=阈值` | 连续 N 根满足 `Value > Threshold` |
| BelowFor | 连续低于阈值 | arg0=主序列，arg1=阈值序列可选 | `param[0]=N`；若无 arg1 则 `param[1]=阈值` | 连续 N 根满足 `Value < Threshold` |
| ROC | 变化率阈值 | arg0=主序列，arg1=阈值可选，arg2=周期可选 | `param[0]=N`，`param[1]=阈值` | `current/previous(N)-1 > threshold` |
| Slope | 线性回归斜率阈值 | arg0=主序列，arg1=阈值可选，arg2=周期可选 | `param[0]=N`，`param[1]=阈值` | `LinearRegressionSlope(N) > threshold` |
| TouchUpper | 触碰上轨 | 至少 2 个值（Price,Upper） | 可兜底 | `Price >= Upper` |
| TouchLower | 触碰下轨 | 至少 2 个值（Price,Lower） | 可兜底 | `Price <= Lower` |
| BreakoutUp | 向上突破 | 至少 2 个值（Price,Upper） | 可兜底 | `Price > Upper` |
| BreakoutDown | 向下跌破 | 至少 2 个值（Price,Lower） | 可兜底 | `Price < Lower` |
| ZScore | Z 分数阈值 | arg0=主序列，arg1=阈值可选，arg2=周期可选 | `param[0]=N`，`param[1]=阈值` | `Z=(S[0]-mean(N))/std(N) > threshold` |
| StdDevGreater | 标准差大于阈值 | arg0=主序列，arg1=阈值可选，arg2=周期可选 | `param[0]=N`，`param[1]=阈值` | `std(N) > threshold` |
| StdDevLess | 标准差小于阈值 | arg0=主序列，arg1=阈值可选，arg2=周期可选 | `param[0]=N`，`param[1]=阈值` | `std(N) < threshold` |
| BandwidthExpand | 带宽扩张 | arg0=上轨，arg1=下轨，arg2=中轨，arg3=周期可选 | `param[0]=N` | 连续 N 步满足 `BW[i] > BW[i+1]`，`BW=(Upper-Lower)/Middle` |
| BandwidthContract | 带宽收敛 | arg0=上轨，arg1=下轨，arg2=中轨，arg3=周期可选 | `param[0]=N` | 连续 N 步满足 `BW[i] < BW[i+1]`，`BW=(Upper-Lower)/Middle` |

### 5.4 可直接套用的 StrategyMethod 示例（按代码规则）
- 比较类（GreaterThan 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "GreaterThan",
  "args": [
    { "refType": "Field", "input": "Close", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] },
    { "refType": "Indicator", "indicator": "EMA", "input": "Close", "params": [20], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": []
}
```
- 交叉类（CrossUp 示例，CrossOver 同理）：
```json
{
  "enabled": true,
  "required": false,
  "method": "CrossUp",
  "args": [
    { "refType": "Indicator", "indicator": "EMA", "input": "Close", "params": [15], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" },
    { "refType": "Indicator", "indicator": "SMA", "input": "Close", "params": [50], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": []
}
```
- 区间类（Between 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "Between",
  "args": [
    { "refType": "Field", "input": "Close", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] },
    { "refType": "Const", "input": "28000", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] },
    { "refType": "Const", "input": "32000", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] }
  ],
  "param": []
}
```
- 连续类（Rising 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "Rising",
  "args": [
    { "refType": "Indicator", "indicator": "EMA", "input": "Close", "params": [20], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": ["3"]
}
```
- 连续阈值类（AboveFor 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "AboveFor",
  "args": [
    { "refType": "Indicator", "indicator": "RSI", "input": "Close", "params": [14], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": ["5", "70"]
}
```
- ROC（变化率）：
```json
{
  "enabled": true,
  "required": false,
  "method": "ROC",
  "args": [
    { "refType": "Indicator", "indicator": "EMA", "input": "Close", "params": [20], "output": "Real", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": ["10", "0.01"]
}
```
- Slope（斜率）：
```json
{
  "enabled": true,
  "required": false,
  "method": "Slope",
  "args": [
    { "refType": "Field", "input": "Close", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] }
  ],
  "param": ["20", "0"]
}
```
- 通道触碰/突破（BreakoutUp 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "BreakoutUp",
  "args": [
    { "refType": "Field", "input": "Close", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] },
    { "refType": "Indicator", "indicator": "BBANDS", "input": "Close", "params": [20, 2, 2, 0], "output": "Real Upper Band", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": []
}
```
- ZScore / StdDev（ZScore 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "ZScore",
  "args": [
    { "refType": "Field", "input": "Close", "output": "Value", "offsetRange": [0, 0], "calcMode": "OnBarClose", "params": [] }
  ],
  "param": ["20", "1.5"]
}
```
- 带宽类（BandwidthExpand 示例）：
```json
{
  "enabled": true,
  "required": false,
  "method": "BandwidthExpand",
  "args": [
    { "refType": "Indicator", "indicator": "BBANDS", "input": "Close", "params": [20, 2, 2, 0], "output": "Real Upper Band", "offsetRange": [0, 0], "calcMode": "OnBarClose" },
    { "refType": "Indicator", "indicator": "BBANDS", "input": "Close", "params": [20, 2, 2, 0], "output": "Real Lower Band", "offsetRange": [0, 0], "calcMode": "OnBarClose" },
    { "refType": "Indicator", "indicator": "BBANDS", "input": "Close", "params": [20, 2, 2, 0], "output": "Real Middle Band", "offsetRange": [0, 0], "calcMode": "OnBarClose" }
  ],
  "param": ["3"]
}
```

## 6. EMA15 与 MA50（SMA）可执行示例（完整结构）
- 语义：EMA15 上穿 SMA50 开多；EMA15 下穿 SMA50 开空。
- 示例只展示 `logic` 片段（可直接塞到 `strategyConfig.logic`）。
- `entry/exit` 四个分支的结构都要完整，且 `entry` 的 groups 不能空。
```json
{
  "entry": {
    "long": {
      "enabled": true,
      "minPassConditionContainer": 1,
      "containers": [
        {
          "checks": {
            "enabled": true,
            "minPassGroups": 1,
            "groups": [
              {
                "enabled": true,
                "minPassConditions": 1,
                "conditions": [
                  {
                    "enabled": true,
                    "required": false,
                    "method": "CrossUp",
                    "args": [
                      {
                        "refType": "Indicator",
                        "indicator": "EMA",
                        "timeframe": "",
                        "input": "Close",
                        "params": [15],
                        "output": "Real",
                        "offsetRange": [0, 0],
                        "calcMode": "OnBarClose"
                      },
                      {
                        "refType": "Indicator",
                        "indicator": "SMA",
                        "timeframe": "",
                        "input": "Close",
                        "params": [50],
                        "output": "Real",
                        "offsetRange": [0, 0],
                        "calcMode": "OnBarClose"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
      ],
      "onPass": {
        "enabled": true,
        "minPassConditions": 1,
        "conditions": [
          {
            "enabled": true,
            "required": false,
            "method": "MakeTrade",
            "args": [],
            "param": ["Long"]
          }
        ]
      }
    },
    "short": {
      "enabled": true,
      "minPassConditionContainer": 1,
      "containers": [
        {
          "checks": {
            "enabled": true,
            "minPassGroups": 1,
            "groups": [
              {
                "enabled": true,
                "minPassConditions": 1,
                "conditions": [
                  {
                    "enabled": true,
                    "required": false,
                    "method": "CrossDown",
                    "args": [
                      {
                        "refType": "Indicator",
                        "indicator": "EMA",
                        "timeframe": "",
                        "input": "Close",
                        "params": [15],
                        "output": "Real",
                        "offsetRange": [0, 0],
                        "calcMode": "OnBarClose"
                      },
                      {
                        "refType": "Indicator",
                        "indicator": "SMA",
                        "timeframe": "",
                        "input": "Close",
                        "params": [50],
                        "output": "Real",
                        "offsetRange": [0, 0],
                        "calcMode": "OnBarClose"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
      ],
      "onPass": {
        "enabled": true,
        "minPassConditions": 1,
        "conditions": [
          {
            "enabled": true,
            "required": false,
            "method": "MakeTrade",
            "args": [],
            "param": ["Short"]
          }
        ]
      }
    }
  },
  "exit": {
    "long": {
      "enabled": true,
      "minPassConditionContainer": 1,
      "containers": [
        {
          "checks": {
            "enabled": true,
            "minPassGroups": 1,
            "groups": []
          }
        }
      ],
      "onPass": {
        "enabled": true,
        "minPassConditions": 1,
        "conditions": [
          {
            "enabled": true,
            "required": false,
            "method": "MakeTrade",
            "args": [],
            "param": ["CloseLong"]
          }
        ]
      }
    },
    "short": {
      "enabled": true,
      "minPassConditionContainer": 1,
      "containers": [
        {
          "checks": {
            "enabled": true,
            "minPassGroups": 1,
            "groups": []
          }
        }
      ],
      "onPass": {
        "enabled": true,
        "minPassConditions": 1,
        "conditions": [
          {
            "enabled": true,
            "required": false,
            "method": "MakeTrade",
            "args": [],
            "param": ["CloseShort"]
          }
        ]
      }
    }
  }
}
```

## 7. 指标白名单（全量，来自 Config/talib_indicators_config.json）
- 只允许使用下表中的 `code`。
- `params` 的顺序必须与 `options` 一致。
- 若 options 含 $ref:#/common/maType，其可选值为：MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3。
- `output` 必须使用 `outputs` 中的 key 或 hint（单输出可用 `Real`/`Value` 走第一个输出）。

| code | inputs.shape | inputs.series | options(params 顺序) | outputs(key/hint) |
|---|---|---|---|---|
| ABANDONEDBABY | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| ACCBANDS | HLC | High, Low, Close | Time Period | Real Upper Band/UpperLimit<br/>Real Middle Band/Line<br/>Real Lower Band/LowerLimit |
| ACOS | Real | Real | 无 | Real/Line |
| AD | HLCV | High, Low, Close, Volume | 无 | Real/Line |
| ADD | Real2 | Real, Real | 无 | Real/Line |
| ADOSC | HLCV | High, Low, Close, Volume | Fast Period<br/>Slow Period | Real/Line |
| ADVANCEBLOCK | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| ADX | HLC | High, Low, Close | Time Period | Real/Line |
| ADXR | HLC | High, Low, Close | Time Period | Real/Line |
| APO | Real | Real | Fast Period<br/>Slow Period<br/>MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3 | Real/Line |
| AROON | HL | High, Low | Time Period | Aroon Down/DashLine<br/>Aroon Up/Line |
| AROONOSC | HL | High, Low | Time Period | Real/Line |
| ASIN | Real | Real | 无 | Real/Line |
| ATAN | Real | Real | 无 | Real/Line |
| ATR | HLC | High, Low, Close | Time Period | Real/Line |
| AVGDEV | Real | Real | Time Period | Real/Line |
| AVGPRICE | OHLC | Open, High, Low, Close | 无 | Real/Line |
| BBANDS | Real | Real | Time Period<br/>Nb Dev Up<br/>Nb Dev Dn<br/>MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3 | Real Upper Band/UpperLimit<br/>Real Middle Band/Line<br/>Real Lower Band/LowerLimit |
| BELTHOLD | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| BETA | Real2 | Real, Real | Time Period | Real/Line |
| BOP | OHLC | Open, High, Low, Close | 无 | Real/Line |
| BREAKAWAY | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| CCI | HLC | High, Low, Close | Time Period | Real/Line |
| CEIL | Real | Real | 无 | Real/Line |
| CLOSINGMARUBOZU | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| CMO | Real | Real | Time Period | Real/Line |
| CONCEALINGBABYSWALLOW | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| CORREL | Real2 | Real, Real | Time Period | Real/Line |
| COS | Real | Real | 无 | Real/Line |
| COSH | Real | Real | 无 | Real/Line |
| COUNTERATTACK | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| DARKCLOUDCOVER | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| DEMA | Real | Real | Time Period | Real/Line |
| DIV | Real2 | Real, Real | 无 | Real/Line |
| DOJI | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| DOJISTAR | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| DRAGONFLYDOJI | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| DX | HLC | High, Low, Close | Time Period | Real/Line |
| EMA | Real | Real | Time Period | Real/Line |
| ENGULFING | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| EVENINGDOJISTAR | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| EVENINGSTAR | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| EXP | Real | Real | 无 | Real/Line |
| FLOOR | Real | Real | 无 | Real/Line |
| GAPSIDEBYSIDEWHITELINES | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| GRAVESTONEDOJI | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HAMMER | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HANGINGMAN | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HARAMI | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HARAMICROSS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HIGHWAVE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HIKKAKE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear/PatternStrength |
| HIKKAKEMODIFIED | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear/PatternStrength |
| HOMINGPIGEON | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| HTDCPERIOD | Real | Real | 无 | Real/Line |
| HTDCPHASE | Real | Real | 无 | Real/Line |
| HTPHASOR | Real | Real | 无 | In Phase/Line<br/>Quadrature/DashLine |
| HTSINE | Real | Real | 无 | Sine/Line<br/>Lead Sine/DashLine |
| HTTRENDLINE | Real | Real | 无 | Real/Line |
| HTTRENDMODE | Real | Real | 无 | Integer/Line |
| IDENTICALTHREECROWS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| INNECK | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| INVERTEDHAMMER | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| KAMA | Real | Real | Time Period | Real/Line |
| KICKING | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| KICKINGBYLENGTH | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| LADDERBOTTOM | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| LINEARREG | Real | Real | Time Period | Real/Line |
| LINEARREGANGLE | Real | Real | Time Period | Real/Line |
| LINEARREGINTERCEPT | Real | Real | Time Period | Real/Line |
| LINEARREGSLOPE | Real | Real | Time Period | Real/Line |
| LN | Real | Real | 无 | Real/Line |
| LOG10 | Real | Real | 无 | Real/Line |
| LONGLEGGEDDOJI | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| LONGLINE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| MA | Real | Real | Time Period<br/>MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3 | Real/Line |
| MACD | Real | Real | Fast Period<br/>Slow Period<br/>Signal Period | MACD/Line<br/>MACD Signal/DashLine<br/>MACD Hist/Histo |
| MACDEXT | Real | Real | Fast Period<br/>Fast MA Type<br/>Slow Period<br/>Slow MA Type<br/>Signal Period<br/>Signal MA Type | MACD/Line<br/>MACD Signal/DashLine<br/>MACD Hist/Histo |
| MACDFIX | Real | Real | Signal Period | MACD/Line<br/>MACD Signal/DashLine<br/>MACD Hist/Histo |
| MAMA | Real | Real | Fast Limit<br/>Slow Limit | MAMA/Line<br/>FAMA/DashLine |
| MARUBOZU | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| MATCHINGLOW | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| MATHOLD | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| MAVP | Custom | Real, Periods | Min Period<br/>Max Period<br/>MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3 | Real/Line |
| MAX | Real | Real | Time Period | Real/Line |
| MAXINDEX | Real | Real | Time Period | Integer/Line |
| MEDPRICE | HL | High, Low | 无 | Real/Line |
| MFI | HLCV | High, Low, Close, Volume | Time Period | Real/Line |
| MIDPOINT | Real | Real | Time Period | Real/Line |
| MIDPRICE | HL | High, Low | Time Period | Real/Line |
| MIN | Real | Real | Time Period | Real/Line |
| MININDEX | Real | Real | Time Period | Integer/Line |
| MINMAX | Real | Real | Time Period | Min/Line<br/>Max/Line |
| MINMAXINDEX | Real | Real | Time Period | Min Idx/Line<br/>Max Idx/Line |
| MINUSDI | HLC | High, Low, Close | Time Period | Real/Line |
| MINUSDM | HL | High, Low | Time Period | Real/Line |
| MOM | Real | Real | Time Period | Real/Line |
| MORNINGDOJISTAR | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| MORNINGSTAR | OHLC | Open, High, Low, Close | Penetration | IntType/PatternBullBear |
| MULT | Real2 | Real, Real | 无 | Real/Line |
| NATR | HLC | High, Low, Close | Time Period | Real/Line |
| OBV | Custom | Real, Volume | 无 | Real/Line |
| ONNECK | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| PIERCINGLINE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| PLUSDI | HLC | High, Low, Close | Time Period | Real/Line |
| PLUSDM | HL | High, Low | Time Period | Real/Line |
| PPO | Real | Real | Fast Period<br/>Slow Period<br/>MA Type 枚举：SMA/EMA/WMA/DEMA/TEMA/TRIMA/KAMA/MAMA/T3 | Real/Line |
| RICKSHAWMAN | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| RISINGFALLINGTHREEMETHODS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| ROC | Real | Real | Time Period | Real/Line |
| ROCP | Real | Real | Time Period | Real/Line |
| ROCR | Real | Real | Time Period | Real/Line |
| ROCR100 | Real | Real | Time Period | Real/Line |
| RSI | Real | Real | Time Period | Real/Line |
| SAR | HL | High, Low | Acceleration<br/>Maximum | Real/Line |
| SAREXT | HL | High, Low | Start Value<br/>Offset On Reverse<br/>Acceleration Init Long<br/>Acceleration Long<br/>Acceleration Max Long<br/>Acceleration Init Short<br/>Acceleration Short<br/>Acceleration Max Short | Real/Line |
| SEPARATINGLINES | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| SHOOTINGSTAR | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| SHORTLINE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| SIN | Real | Real | 无 | Real/Line |
| SINH | Real | Real | 无 | Real/Line |
| SMA | Real | Real | Time Period | Real/Line |
| SPINNINGTOP | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| SQRT | Real | Real | 无 | Real/Line |
| STALLED | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| STDDEV | Real | Real | Time Period<br/>Nb Dev | Real/Line |
| STICKSANDWICH | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| STOCH | HLC | High, Low, Close | Fast K Period<br/>Slow K Period<br/>Slow K MA Type<br/>Slow D Period<br/>Slow D MA Type | Slow K/DashLine<br/>Slow D/DashLine |
| STOCHF | HLC | High, Low, Close | Fast K Period<br/>Fast D Period<br/>Fast D MA Type | Fast K/Line<br/>Fast D/Line |
| STOCHRSI | Real | Real | Time Period<br/>Fast K Period<br/>Fast D Period<br/>Fast D MA Type | Fast K/Line<br/>Fast D/Line |
| SUB | Real2 | Real, Real | 无 | Real/Line |
| SUM | Real | Real | Time Period | Real/Line |
| T3 | Real | Real | Time Period<br/>V Factor | Real/Line |
| TAKURILINE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| TAN | Real | Real | 无 | Real/Line |
| TANH | Real | Real | 无 | Real/Line |
| TASUKIGAP | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| TEMA | Real | Real | Time Period | Real/Line |
| THREEBLACKCROWS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THREEINSIDE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THREELINESTRIKE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THREEOUTSIDE | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THREESTARSINSOUTH | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THREEWHITESOLDIERS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| THRUSTING | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| TRANGE | HLC | High, Low, Close | 无 | Real/Line |
| TRIMA | Real | Real | Time Period | Real/Line |
| TRISTAR | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| TRIX | Real | Real | Time Period | Real/Line |
| TSF | Real | Real | Time Period | Real/Line |
| TWOCROWS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| TYPPRICE | HLC | High, Low, Close | 无 | Real/Line |
| ULTOSC | HLC | High, Low, Close | Time Period 1<br/>Time Period 2<br/>Time Period 3 | Real/Line |
| UNIQUETHREERIVER | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| UPDOWNSIDEGAPTHREEMETHODS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| UPSIDEGAPTWOCROWS | OHLC | Open, High, Low, Close | 无 | IntType/PatternBullBear |
| VAR | Real | Real | Time Period | Real/Line |
| WCLPRICE | HLC | High, Low, Close | 无 | Real/Line |
| WILLR | HLC | High, Low, Close | Time Period | Real/Line |
| WMA | Real | Real | Time Period | Real/Line |

## 8. 生成流程（强制顺序）
- 第 1 步：识别用户意图。若不是“生成策略”，输出 `strategyConfig: null`。
- 第 2 步：先填 `trade` 和 `runtime`，再填 `logic`，禁止跳字段。
- 第 3 步：从指标白名单中选 `indicator`，按表中 `options` 顺序写 `params`。
- 第 4 步：从条件方法白名单中选 `method`，并按方法需求填 `args/param`。
- 第 5 步：至少保证 `entry.long` 与 `entry.short` 的 `groups[].conditions[]` 非空。
- 第 6 步：`onPass` 必须是 `MakeTrade`，方向只能是 `Long/Short/CloseLong/CloseShort`。

## 9. 生成约束（强制）
- `entry.long` 与 `entry.short` 禁止输出空条件结构：`groups: []` 或 `conditions: []`。
- 生成策略时，`entry.long` 与 `entry.short` 必须至少有一条可执行条件。
- `exit.long` 与 `exit.short` 可以按策略风格配置条件；若要“无条件平仓动作”，保持 `groups: []` 但 `onPass` 动作必须完整。
- 条件 method、indicator、output 必须来自本知识库白名单。
- 字段命名必须使用 camelCase（如 `refType`、`offsetRange`、`minPassConditions`）。
- `args[].params` 必须是数值数组；`method.param` 必须是字符串数组（数值请写成字符串）。
- MakeTrade 方向建议同时写 `param` 与 `args`（字符串数组）以提升兼容性。


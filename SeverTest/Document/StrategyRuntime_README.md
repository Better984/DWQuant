# 策略运行说明

## 概述

该模块用于在实盘中运行策略。运行流程是：行情任务 -> 指标更新 -> 条件判断 -> 触发动作任务（入队）。

## 执行顺序

1. `exit.long`
2. `entry.long`

说明：`exit.short` 与 `entry.short` 当前在代码中仍是注释状态，启用时请同步改代码。

## 条件结构

```
StrategyLogicBranch
  ├─ enabled
  ├─ minPassConditionContainer
  ├─ containers (ConditionContainer[])
  │    └─ checks (ConditionGroupSet)
  └─ onPass (ActionSet) // 满足条件后只触发一次
```

### 规则
- `enabled=false` 不参与执行。
- `containers` 里的每个容器仅包含 `checks`。
- `ConditionGroupSet` 通过才认为该容器通过。
- 容器通过数量 >= `minPassConditionContainer` 时，触发 `onPass` 一次。

### ConditionGroupSet
- `minPassGroups` 表示至少通过多少个条件组。
- 条件组内 `required=true` 的条件必须全部通过。
- 非必须条件满足数量 >= `minPassConditions` 才算该组通过。

## 条件缓存

- 条件结果按 Key 缓存，Key 包含交易维度和条件参数。
- 同一 K 线时间戳内复用缓存结果，避免重复计算。
- 缓存内容包含：结果 bool + Message 字符串。

## 动作执行

`onPass` 不直接下单，只会生成 `StrategyActionTask` 入队，后续由消费者处理。

## 配置结构（最新）

```
{
  "trade": { ... },
  "logic": {
    "entry": {
      "long": {
        "enabled": true,
        "minPassConditionContainer": 1,
        "containers": [ { "checks": { ... } } ],
        "onPass": { ... }
      },
      "short": { ... }
    },
    "exit": {
      "long": { ... },
      "short": { ... }
    }
  }
}
```

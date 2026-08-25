# 定点攻击回中相等容忍设计

## 背景

实机会话 `3a3a332a-191d-4d34-abbb-d25f3b19be53` 在第 23 轮以 `MOVEMENT_RETURN_UNSATISFIED` 停止。该轮从 `-53ms` 开始，第一段到达 `-7ms`，第二段因真实释放抖动回到 `-53ms`。偏移没有越过 `±120ms`，也没有比轮次开始更差，但旧规则把“相等”与“变差”一并停止。

## 方案比较

1. 仅允许相等：最终绝对偏移小于或等于开始值时继续，变大时停止。改动最小，仍保留即时恶化保护。
2. 固定容差：允许最终绝对偏移比开始值多 `20ms`。误停更少，但会放宽真实漂移。
3. 连续失败计数：多次未回中才停止。对瞬时抖动最宽容，但新增跨轮状态和阈值配置。

采用方案 1。它准确覆盖本次日志，同时不改变硬边界、下一轮预算、回中分区、方向顺序或随机抽样。

## 行为边界

- `ReturnTowardCenter` 轮次结束时，`abs(final) < abs(start)`：继续。
- `ReturnTowardCenter` 轮次结束时，`abs(final) == abs(start)`：视为没有恶化并继续。
- `ReturnTowardCenter` 轮次结束时，`abs(final) > abs(start)`：以 `MOVEMENT_RETURN_UNSATISFIED` 停止。
- `MOVEMENT_OFFSET_EXCEEDED`、`INITIAL_FACING_BUDGET_EXHAUSTED` 和 Broker 真实计时校验保持不变。

## 验证

单元测试复现实机会话的相等结果并证明可以完成轮次；另一个测试证明绝对偏移变大仍会停止。随后运行全量 .NET、React 测试和 Windows x64 发布校验。

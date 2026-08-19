# 一期定点攻击详细设计

## 1. 组件边界

```text
React/Ant Design 配置窗口
        │ versioned bridge（配置、意图、状态）
        ▼
Windows Host (.NET 8 win-x64)
  ├─ WindowLocator / ForegroundSession
  ├─ StationarySessionController
  ├─ AttackTriggerStrategy
  ├─ StationaryMovementPlanner
  ├─ RhythmSampler + monotonic deadline clock
  ├─ BrokerClient / InputSafetyCoordinator
  ├─ RhythmStatePublisher / NotificationService
  └─ PreviewWindowHost（独立原生窗口）
        │ authenticated named pipe
        ▼
管理员 Input Broker
        │
        ▼
Windows keybd_event
```

核心控制器不直接调用 Win32 输入；所有输入必须经过 BrokerClient 和安全门。React 不产生随机值，也不决定何时释放按键。

## 2. 状态机

状态必须是显式枚举，禁止用多个布尔值拼接：

```text
Idle
 -> LocatingWindow
 -> ArmingBroker
 -> Running.AttackHolding
 -> Running.MoveFirst
 -> Running.MoveGap
 -> Running.MoveSecond
 -> Running.Stabilizing
 -> Running.Resting (optional)
 -> Running.AttackHolding
 -> Stopped
```

任意运行状态都可以进入 `Stopped`。进入 `Stopped` 必须先取消当前延迟/任务，再按活动键逐一释放，最后调用 Broker `ReleaseAll`，然后发布最终状态。

## 3. 第二方向不能被跳过

实现必须满足以下时序不变量：

1. 第一方向 `KeyDown` 成功后，必须等待其保持时间结束并收到 `KeyUp` 成功结果。
2. 间隔任务完成前，不能发送第二方向。
3. 第二方向必须独立完成 `KeyDown -> 等待 -> KeyUp`；任一结果失败，当前会话停止。
4. 第二方向 `KeyUp` 返回成功后，还必须完成稳定等待。
5. 稳定等待完成后，才允许抽样下一轮攻击时长并发送新的攻击 `KeyDown`。
6. 控制器测试必须断言完整事件序列，而不是只断言方法被调用。

不得把“取消第二方向后继续攻击”当成容错。第二方向失败是安全停止条件。

## 4. 会话级移动算法

会话状态包含：

```text
sessionAnchor（逻辑起点，不要求识别绝对像素坐标）
relativeOffsetMs（程序自己造成的累计相对位移）
maxLateralMoveMs（每侧最大阈值，默认 250）
```

启动会话时 `relativeOffsetMs = 0`。向左按压使 offset 减少，向右按压使 offset 增加；方向符号可反过来，但整个实现必须统一。

每次移动抽样流程：

1. 根据 `relativeOffsetMs` 计算左、右剩余预算。
2. 只有有足够预算的方向才可被首方向随机选择。
3. 在该方向的剩余预算和配置的 `[minHoldMs,maxHoldMs]` 交集内按 1ms 粒度抽样。
4. 完成第一段后抽样间隔并等待。
5. 第二段必须是相反方向；根据更新后的 offset 重新计算该方向预算，再独立抽样。
6. 更新 offset，但不把它修正为 0。
7. 完成稳定等待后进入下一阶段。

示例不是固定脚本：

```text
左 123ms -> 右 87ms  => offset -36ms
右 104ms -> 左 91ms  => offset -23ms
```

只要全过程不超过 `[-250,+250]`（或用户配置值），保留非零净位移就是正确行为。

## 5. 可靠按键与租约

- 攻击持续时间最多 `60,000ms`，UI、配置、Host、协议和 Broker 验证器必须使用同一个硬上限。
- 长按期间使用单一逻辑按键租约，不通过重复物理 `keybd_event` 制造点击；如需心跳/租约刷新，必须验证不会产生重复按下或提前释放。
- 移动和攻击不能重叠；每个动作必须有成对的 `KeyDown/KeyUp`。
- Broker 断开、心跳超时、窗口身份变化或安全门失败时，由 Broker watchdog 和 Host 双重释放。

## 6. 攻击触发策略接口

定义独立接口，例如：

```text
IAttackTriggerStrategy.ShouldAttack(ObservationContext context)
```

一期使用 `AlwaysAttackTriggerStrategy`，不依赖图像。未来的 `MonsterInRangeTriggerStrategy` 可以根据 Self/Monster/距离/置信度返回是否触发攻击，可能使用短按或其他攻击动作，但不能改变 `StationaryMovementPlanner` 的会话级移动规则。

识别模式在一期 UI 中保留但禁用；后端拒绝直接运行未启用策略。

## 7. 配置

配置必须带 schemaVersion、来源、更新时间和校验结果。最小字段：

```json
{
  "attackKey": "Ctrl",
  "attackBands": [
    { "minMs": 1000, "maxMs": 10000, "weight": 5 },
    { "minMs": 10000, "maxMs": 20000, "weight": 10 },
    { "minMs": 20000, "maxMs": 40000, "weight": 60 },
    { "minMs": 40000, "maxMs": 60000, "weight": 25 }
  ],
  "maxLateralMoveMs": 250,
  "moveHoldMinMs": 80,
  "moveHoldMaxMs": 125,
  "moveGapMinMs": 30,
  "moveGapMaxMs": 120,
  "stabilizeMinMs": 80,
  "stabilizeMaxMs": 150,
  "restEnabled": true,
  "restProbabilityPercent": 25,
  "restMinMs": 2000,
  "restMaxMs": 5000,
  "attackTriggerMode": "always"
}
```

校验要求：权重总和 100；所有范围为正且 min 不大于 max；攻击最大值不超过 60,000ms；移动抽样不能越过会话阈值；disabled 的 `monsterInRange` 不能启动。

## 8. 倒计时消息

后端发布 `stationary.rhythm.updated`：

```json
{
  "schemaVersion": 1,
  "sessionId": "…",
  "cycleId": 12,
  "phase": "attackHolding",
  "sampledDurationMs": 27438,
  "phaseStartedMonoMs": 123456789,
  "phaseDeadlineMonoMs": 123484227,
  "remainingMs": 18420,
  "updatedAtMonoMs": 123465807,
  "earlyReleaseReason": null
}
```

每次重新抽样必须产生新 `cycleId` 或明确的新阶段事件，前端必须以新的 deadline 重置倒计时。停止和异常事件必须使旧 session 的倒计时失效，不能继续递减。

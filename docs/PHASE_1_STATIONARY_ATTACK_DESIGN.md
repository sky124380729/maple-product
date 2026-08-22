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
  └─ PreviewWindowHost（独立原生窗口，可独立于攻击会话启动）
        │ authenticated named pipe
        ▼
管理员 Input Broker
        │
        ▼
Windows keybd_event
```

核心控制器不直接调用 Win32 输入；所有输入必须经过 BrokerClient 和安全门。React 不产生随机值，也不决定何时释放按键。

Windows 发布基线为 Windows 10 22H2 x64 或 Windows 11 x64。Windows Host 使用 `net8.0-windows10.0.19041.0` 目标框架并在启动时执行系统版本门检查；低于产品基线时拒绝启动自动攻击。

窗口发现不依赖用户配置的 exe 路径。Host 枚举可见顶层窗口，只接受标题精确为 `冒险岛怀旧服` 且窗口类精确为 `UnityWndClass` 的候选。唯一候选自动绑定；零候选返回 `TARGET_NOT_FOUND`；虽然游戏限制单实例，异常的多候选仍返回 `TARGET_MULTIPLE` 并拒绝输入。选定候选后读取并绑定 HWND、PID、规范化进程路径和进程启动时间，任何一项变化都使会话失效。最小化窗口可以被发现用于明确诊断，但必须在前台安全门阶段恢复并重新校验后才能输入。

Host 在启动 Broker 前先激活并校验目标；由于 UAC 可能改变前台窗口，Broker 握手成功后必须再次激活并校验同一目标，第二次失败时关闭 Broker 连接且不进入控制器。Broker 对 `KeyDown/KeyUp` 执行目标安全门，但 `ReleaseAll/Close` 必须无条件尝试释放活动键，不能因失焦或身份变化被拒绝。

Input Broker 必须使用 Windows GUI 子系统并以隐藏窗口方式启动，不得创建可见控制台窗口。UAC 同意界面可以短暂出现，但握手完成后 Host 必须重新激活游戏；Broker 的启动、运行和退出都不得再改变前台窗口。

## 2. 状态机

状态必须是显式枚举，禁止用多个布尔值拼接：

```text
Idle
 -> LocatingWindow
 -> ArmingBroker
 -> Running.AttackHolding
 -> Running.AttackReleased
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

1. 攻击 `KeyUp` 成功后必须完成固定 `100ms` 的 `AttackReleased` 无按键缓冲，之后才能发送第一方向。
2. 第一方向 `KeyDown` 成功后，必须等待其保持时间结束并收到 `KeyUp` 成功结果。
3. 间隔任务完成前，不能发送第二方向。
4. 第二方向必须独立完成 `KeyDown -> 等待 -> KeyUp`；任一结果失败，当前会话停止。
5. 第二方向 `KeyUp` 返回成功后，还必须完成稳定等待。
6. 稳定等待完成后，才允许抽样下一轮攻击时长并发送新的攻击 `KeyDown`。
7. 控制器测试必须断言完整事件序列，而不是只断言方法被调用。

不得把“取消第二方向后继续攻击”当成容错。第二方向失败是安全停止条件。

## 4. 会话级移动算法

会话状态包含：

```text
sessionAnchor（逻辑起点，不要求识别绝对像素坐标）
relativeOffsetMs（程序自己造成的累计相对位移）
maxLateralMoveMs（每侧最大阈值，默认 80）
initialFacing（本次启动时用户确认的 Left/Right）
```

用户点击开始后，React 必须先弹出只有 `←`、`→` 两个选项的朝向确认框。关闭确认框不提交 `startStationary`。选择结果作为启动意图传给 Host，不写入持久化配置。Host 通过 `IInitialFacingProvider` 将启动意图解析为 `FacingResolution`；一期使用人工来源，未来视觉来源可在不修改控制器和移动规划器的前提下替换该实现。解析失败或未来视觉置信度不足时拒绝启动，不发送输入。

启动会话时 `relativeOffsetMs = 0`，并锁定解析后的 `initialFacing`。向左按压使 offset 减少，向右按压使 offset 增加；方向符号可反过来，但整个实现必须统一。

移动规划器使用 `20ms` 固定释放抖动余量，并按绝对偏移占 `maxLateralMoveMs` 的比例选择轮次意图：

- `0%–40%`：无偏随机，接受所有满足安全不变量的候选；
- `>40%–70%`：以 `75%` 概率选择回中意图，以 `25%` 概率选择无偏随机；
- `>70%–100%`：必须选择回中意图。

回中意图只要求轮次结束时的 `abs(relativeOffsetMs)` 严格小于轮次开始值，不要求回到零点，也不指定固定目标。第一段和第二段仍分别执行随机抽样；约束只缩小合法候选集合。任何区域都禁止复制第一段时长、固定两段差值或在每轮末尾修正为零。

每次移动流程：

1. 保存轮次开始偏移并随机选择本轮意图。
2. 第一方向固定为 `initialFacing` 的反方向。按当前真实 offset、配置边界、`20ms` 余量、本轮意图以及“完成第二段后仍给下一轮第一方向保留最小按压与余量”的可行性，计算第一段合法范围并按 1ms 粒度抽样。无合法值时安全停止，不能交换顺序。
3. Host 请求 Broker 按下第一方向，等待计划时长后主动请求 `KeyUp`。Broker 返回成功物理 Down 至成功物理 Up 的 `actualHoldMs` 和 `releaseLatenessMs`。
4. 校验真实时长后立即按方向符号提交第一段真实 offset。缺失、不在 `1–5,000ms` 内或越过配置边界时，以稳定错误码停止并 `ReleaseAll`。
5. 基于更新后的真实 offset 抽样并等待两段间隔。
6. 第二方向固定为 `initialFacing`。根据新的真实 offset、`20ms` 余量、本轮意图和下一轮第一方向预算重新计算合法范围，再按 1ms 粒度抽样；无合法值时安全停止。
7. Host 主动完成第二方向 `KeyDown -> 等待 -> KeyUp`，校验并提交 Broker 返回的真实时长。轮次末 offset 不修正为零，并且必须满足本轮意图和下一轮预算不变量；真实结果未完成回中意图时以 `MOVEMENT_RETURN_UNSATISFIED` 安全停止。
8. 完成稳定等待后进入下一阶段。

示例不是固定脚本：

```text
初始朝右：左 123ms -> 右 87ms  => offset -36ms，最终朝右
初始朝左：右 104ms -> 左 91ms  => offset -23ms，最终朝左
```

只要全过程不超过配置的左右阈值，保留非零净位移就是正确行为。

## 5. 可靠按键与租约

- 攻击持续时间最多 `60,000ms`，UI、配置、Host、协议和 Broker 验证器必须使用同一个硬上限。
- 长按期间使用单一逻辑按键租约，不通过重复物理 `keybd_event` 制造点击；如需心跳/租约刷新，必须验证不会产生重复按下或提前释放。
- 定点左右键由 Host 在计划保持结束后主动 `KeyUp`；Broker 租约仅通过 watchdog 提供超时兜底。Broker 协议必须标记定点请求，使其不进入自动寻路使用的移动截止调度器；寻路请求的现有调度行为保持不变。
- Broker 对定点方向键记录成功物理 Down 返回后的单调时间和成功物理 Up 返回后的单调时间，并在显式 `KeyUp` 响应中返回 `actualHoldMs` 与 `releaseLatenessMs`。控制器逐段提交 `actualHoldMs`，不得按请求时长一次性提交整轮计划。
- `actualHoldMs` 缺失、不在 `1–5,000ms` 内或提交后越过 `[-maxLateralMoveMs,+maxLateralMoveMs]` 时停止并 `ReleaseAll`。`releaseLatenessMs` 用于日志和实机余量校准；只要真实 offset 仍在边界内，单独的迟到量不构成停止条件。
- Broker 的 `keybd_event` 编码沿用 Windows integrated 实机路径：攻击键同时发送虚拟键和 Set-1 扫描码；左右方向键再设置 extended flag。`Ctrl` 必须编码为 `VK_CONTROL (0x11) + scan 0x1D`，不能使用零扫描码。
- 移动和攻击不能重叠；每个动作必须有成对的 `KeyDown/KeyUp`。
- Broker 断开、心跳超时、窗口身份变化或安全门失败时，由 Broker watchdog 和 Host 双重释放。单个动作租约到期只释放活动键，不得解除已经绑定的目标；Host 随后发送的幂等 `KeyUp` 必须成功，后续移动仍可继续。

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
    { "minMs": 1000, "maxMs": 10000, "weight": 97 },
    { "minMs": 10000, "maxMs": 20000, "weight": 1 },
    { "minMs": 20000, "maxMs": 40000, "weight": 1 },
    { "minMs": 40000, "maxMs": 60000, "weight": 1 }
  ],
  "maxLateralMoveMs": 80,
  "moveHoldMinMs": 30,
  "moveHoldMaxMs": 50,
  "moveGapMinMs": 30,
  "moveGapMaxMs": 120,
  "stabilizeMinMs": 80,
  "stabilizeMaxMs": 150,
  "restEnabled": true,
  "restProbabilityPercent": 50,
  "restMinMs": 2000,
  "restMaxMs": 5000,
  "attackTriggerMode": "always"
}
```

校验要求：权重总和 100；所有范围为正且 min 不大于 max；攻击最大值不超过 60,000ms；方向键计划保持最大值不超过 `5,000ms`；`maxLateralMoveMs >= moveHoldMinMs + 20ms`；移动抽样不能越过会话阈值；disabled 的 `monsterInRange` 不能启动。

配置不包含用户可编辑的目标 exe 路径。旧版配置中的 `targetExecutablePath` 只作为向后兼容字段读取和保存，Host 不使用它发现窗口，React 不显示或校验该字段。实际进程路径仅由 Host 在绑定窗口后通过 PID 读取，并作为不可变会话身份的一部分。

`attackKey` 只允许 `Ctrl`、`Shift`、`Space`、`A`、`S`、`D`、`F`、`Z`、`X`、`C`、`V`。该列表由 Core 契约定义并被 UI 和 Broker 复用；未知键必须在保存、启动和 Broker 动作校验三处拒绝。

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
  "relativeOffsetMs": -23,
  "earlyReleaseReason": null
}
```

`relativeOffsetMs` 是 Host 权威的会话级计算偏移。负数表示左、正数表示右；每段移动真实时长提交后，随紧接着的阶段消息立即更新。识别开关不影响该字段。前端在角色信息区域显示带符号值和方向文字，停止后保留最终值，下一次开始时重置为 `0`。

每次重新抽样必须产生新 `cycleId` 或明确的新阶段事件，前端必须以新的 deadline 重置倒计时。停止和异常事件必须使旧 session 的倒计时失效，不能继续递减，但不得清除本次会话最终 `relativeOffsetMs`。

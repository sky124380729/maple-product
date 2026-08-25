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
3. 第一方向 `KeyUp` 成功后先完成固定 `100ms` 的方向释放结算缓冲，再独立抽取并完成配置范围内的随机间隔；两者都完成前不能发送第二方向。
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

回中意图只要求轮次结束时的 `abs(relativeOffsetMs)` 不大于轮次开始值，不要求回到零点，也不指定固定目标。第一段和第二段仍分别执行随机抽样；约束只缩小合法候选集合。任何区域都禁止复制第一段时长、固定两段差值或在每轮末尾修正为零。若真实释放抖动使回中轮反而变差，规划器设置回中债务并强制下一轮继续回中，而不是停止会话。

每次移动流程：

1. 保存轮次开始偏移并随机选择本轮意图。
2. 第一方向固定为 `initialFacing` 的反方向。每个第一段候选都枚举从“计划保持时长”到“计划保持时长 + 20ms”的全部可能实际落点，并逐一检查是否仍存在满足本轮意图、第二段边界和下一轮第一方向预算的合法第二段；只有全区间通过检查的候选才能按 1ms 粒度参与随机抽样。无合法值时不交换顺序，改为随机执行一次仍满足硬边界与余量的 `initialFacing` 方向恢复动作；恢复也无候选时发布 `MOVEMENT_FROZEN_NO_SAFE_RECOVERY` 并跳过本轮移动，攻击会话继续。
3. Host 请求 Broker 按下第一方向，等待计划时长后主动请求 `KeyUp`。Broker 返回成功物理 Down 至成功物理 Up 的 `actualHoldMs` 和 `releaseLatenessMs`。
4. 校验真实时长后立即按方向符号提交第一段真实 offset。缺失、不在 `1–5,000ms` 内或越过配置边界时，以稳定错误码停止并 `ReleaseAll`。
5. 基于更新后的真实 offset，先等待固定 `100ms` 的方向释放结算缓冲，再独立抽样并等待配置的两段随机间隔。权威 `MoveGap` 阶段发布两者之和，随机值本身不被固定缓冲替代。
6. 第二方向固定为 `initialFacing`。根据新的真实 offset、`20ms` 余量、本轮意图和下一轮第一方向预算重新计算合法范围，再按 1ms 粒度抽样；无合法值时安全停止。
7. Host 主动完成第二方向 `KeyDown -> 等待 -> KeyUp`，校验并提交 Broker 返回的真实时长。轮次末 offset 不修正为零；回中轮末绝对偏移变小或相等时清除回中债务，真实结果使绝对偏移变大时设置回中债务并继续会话，下一轮强制回中。
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
- 定点左右键正常由 Host 在计划保持结束后主动 `KeyUp`；Broker 协议以 `HostKeyUp` 标记定点请求，并把当前方向键注册到高精度移动释放调度器，截止为计划保持时长加 `20ms`。截止触发只释放该方向键、缓存真实计时且保持目标已绑定，使 Host 线程恢复后的幂等 `KeyUp` 可以继续当前循环。寻路请求继续使用 `BrokerDeadline` 并按原计划时长截止，不增加定点余量。
- Broker 对定点方向键记录成功物理 Down 返回后的单调时间和成功物理 Up 返回后的单调时间，并在显式 `KeyUp` 响应中返回 `actualHoldMs` 与 `releaseLatenessMs`。若 watchdog 抢先完成物理 Up，Broker 必须缓存该次完成计时，随后的幂等 `KeyUp` 返回同一计时且不得重复物理 Up。控制器逐段提交 `actualHoldMs`，不得按请求时长一次性提交整轮计划。
- `actualHoldMs` 缺失、不在 `1–5,000ms` 内或提交后越过 `[-maxLateralMoveMs,+maxLateralMoveMs]` 时停止并 `ReleaseAll`。`releaseLatenessMs` 用于日志和实机余量校准；只要真实 offset 仍在边界内，单独的迟到量不构成停止条件。
- Broker 的 `keybd_event` 编码沿用 Windows integrated 实机路径：攻击键同时发送虚拟键和 Set-1 扫描码；左右方向键再设置 extended flag。`Ctrl` 必须编码为 `VK_CONTROL (0x11) + scan 0x1D`，不能使用零扫描码。
- 移动和攻击不能重叠；每个动作必须有成对的 `KeyDown/KeyUp`。
- Broker 断开、心跳超时、窗口身份变化或安全门失败时，由 Broker watchdog 和 Host 双重释放。单个动作租约到期只释放活动键，不得解除已经绑定的目标；Host 随后发送的幂等 `KeyUp` 必须成功并携带该方向键的真实按压时长与释放迟到量，后续移动仍可继续。

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

## 9. 视觉增强持续攻击

`AttackTriggerMode.VisualSafeContinuous` 是独立的可运行模式。现有 `StationarySessionController` 和 `StationaryMovementPlanner` 不增加视觉分支；新模式由 `VisualStationarySessionController`、`VisualStationaryMovementPlanner` 和以下纯 Host 组件组成：

- `VisualStationaryProfile`：schema 1 保存旧版名字模板；schema 2 保存捕获宽高、黄色平台外框、蓝色人物外观源框和 1–8 个固定 BGRA 模板。绿色安全核心只由保护带计算，不持久化为用户选择。
- `SelfNameTemplateMatcher`：在平台周围有界区域内输出最佳分数、空间分离的次佳分数和候选中心；每个特征独立计算颜色与边缘分数，稳健分量把单特征最大损失截断为 `0.25`，最终分数为稳健分量均值的 80% 加未截断全量特征均值的 20%，用于容忍宠物造成的小面积局部遮挡，同时保留整体不一致惩罚；评分热路径不得逐候选分配或排序，不得用全帧中心距离选本人。
- `SelfAppearanceTemplateMatcher`：首次获取和普通跟踪先使用保存位置或上一可信位置各轴 `12px` 邻域；首次获取的局部证据不足 `0.70/0.06`，或已有轨迹的局部证据不足 `0.68/0.04` 时，第二次匹配扫描用户黄色平台外框完整区域。第二级搜索不与绿色安全核心、保护带或中心回正带求交，平台黄色外框只限定可重获范围，移动安全分类在候选通过身份稳定后执行。评分在既有抗遮挡颜色/边缘分量上加入采样亮度的归一化结构相关性，候选采样亮度范围不足 `16` 时直接作为低纹理无效证据处理，确保纯背景不能被稳健分量抬到最低跟踪阈值 `0.68`；不得全帧搜索，不得在线更新模板，黄色框外人物不参与最佳/次佳排序。
- `SelfAppearanceTemplateMatcher` 的黄色全域调用使用粗到细搜索：粗搜步长为 `clamp(ceil(4 * frameWidth / 1366), 2, 8)`，覆盖完整候选中心网格；随后分别在粗搜最佳空间峰和排除同一目标后的次佳空间峰周围一个步长内逐像素复核，并按精搜结果重新排序。锚点局部调用保持步长 1。该优化不得跳过空间第二候选或放宽歧义门槛。
- `CharacterAppearanceCalibrator`：冻结帧模板必定保留；框选后先等待 `3s` 供用户切回游戏，再在约 `6s` 内均匀采集 7 张实时帧。采集在原框各轴 `12px` 内对齐，最低校准分数为 `0.60`，用于接收真实左右朝向和施法姿态；与现有任一模板相似度达到 `0.97` 的重复帧丢弃，模板库最多 8 个。
- `SelfIdentityStabilizer`：人物外观首次锁定和轨迹完全重置后的重新锁定要求最佳分数至少 `0.70`、最佳与空间次佳差值至少 `0.06`、连续 3 个递增帧序列且中心跳变不超过 `12px`；旧 schema 1 名字模板继续使用既有阈值。已有可信外观轨迹时，当前锚点各轴 `12px` 内使用 `0.68` 跟踪门槛，形成 `0.68` 跟踪、`0.70` 获取的滞回；黄色框全域搜索得到的远距离恢复候选必须使用 `0.70`、`0.06` 和连续 3 帧重新稳定，稳定前不迁移已提交锚点。较低跟踪门槛不得放宽首次锁定、远距离恢复或歧义要求。同一人物框中心相距不足半个模板宽且半个模板高的相邻峰合并为一个目标，不能制造虚假的次峰歧义；黄色框外第二峰不覆盖当前轨迹，框内空间分离候选才视为歧义。
- `VisualPlatformSafetyGate`：按固定屏幕坐标计算 `Safe`、左右 `Guard`、`Outside` 和 `Untrusted`；保护带在 1366 宽客户区至少 `32px`，并按客户区宽度缩放。
- `VisualStationaryObservationSession`：接收原生捕获帧，组合匹配、稳定和安全状态，向控制器提供不可变最新观察、“等待指定序列后的新可信帧”以及方向动作跟踪窗口接口；每个观察同时保留当前最佳人物候选框和原始匹配分数，供原生预览解释可信、采集中和失信状态，不把候选框用于绕过安全门。人物外观搜索锚点默认锁定，只有控制器开始真实方向动作后至动作后反馈完成前可随可信中心更新；无方向输入期间不得累计漂移。

配置入口位于独立原生预览。第 1/2 步由用户用黄色框选择平台外层安全范围，纵向允许包含人物与平台图像；Host 自动显示绿色保护带内安全核心。绿色核心内允许双向随机移动，黄色外框与绿色核心之间只授权向内修正，黄色外框之外冻结移动。第 2/2 步用蓝框紧贴选择本人头部和上半身，不含名字、宠物和大面积武器/技能特效。视觉配置与跟踪覆盖层只创建有颜色语义的矩形和轻透明填充，不创建任何画面内文字控件；拖拽中的临时框同样无文字。用途图例、当前步骤、采集进度、错误、保存结果和实时匹配分数由画面外工具栏或底部状态栏承载。预览复用视觉攻击控制器使用的同一观察会话，在空闲时也持续处理新帧，并以青色可信框或橙色候选框显示当前最佳位置。UI 合并掉帧时必须捕获同一帧处理后生成的观察快照，禁止在旧位图上读取更新后的候选坐标。人物框在 1366 宽参考客户区不得小于 `24x32` 或大于 `112x144`，按实际客户区宽度等比缩放，并必须具有足够纹理。平台矩形和人物源位置是固定坐标；每次启动仍需重新连续锁定 3 帧。旧 schema 1 名字配置继续读取，重新配置只生成 schema 2 人物外观配置。预览工具栏的配置和清除命令与主窗口使用同一生命周期门及删除路径；攻击或寻路的启动准备及运行期间均拒绝修改，成功清除后销毁空闲观察会话并清空全部覆盖框。

视觉移动规则：

1. `abs(visualOffsetPx) <= guardWidthPx` 的中心带内，方向和保持时长继续从配置合法范围随机抽样。
2. 可信位置超出中心带但仍在外框内时执行随机时长的向内修正；左右保护带同样只允许向内，不追加等长向外抵消段。若向内方向等于初始朝向，一段修正即可结束；若向内方向与初始朝向相反，立即进入 `FacingRestorePending`，在同一攻击间隔内按每段独立随机时长继续向内修正。只有重新进入中心带并安全执行一次随机时长的初始朝向动作后才发布 `VISUAL_FACING_RESTORED` 并允许下一轮攻击。恢复流程不得在保护带内强行向外，也不得把多段修正合并成固定总时长。
3. 每段方向键显式 `KeyUp` 后完成稳定等待，必须取得帧序列更新的可信视觉位置，才能授权下一段移动。动作期间或释放前的旧帧不能授权连续输入。
4. 保护带至少覆盖一次最大单步视觉位移和识别抖动；实测需要更大时本会话只能扩大、不能缩小。扩大时必须在同一个处理锁内立即用最新可信位置重算平台状态并撤销新变危险的方向授权，不能等待下一捕获帧；扩大后无剩余安全区时冻结移动。
5. `UntrustedFrozen` 在连续 `15s` 内不发送左右键但继续随机攻击。达到 `15s` 后，同一个 `VisualStationarySessionController` 切换到普通 `StationaryMovementPlanner`：用 Broker 已提交的真实方向时长维护 `relativeOffsetMs`，按 `maxLateralMoveMs`、初始朝向、释放余量和随机回中规则生成双向移动，不要求视觉 `px/ms` 样本。回退规划器以真实时间模型的符号和数值启动；若该值已超出配置边界，只压到同符号的 `maxLateralMoveMs` 后继续，不得以 `MOVEMENT_OFFSET_EXCEEDED` 停止攻击。普通回退两段之间先固定等待 `100ms`，再完整等待一次配置范围内独立随机间隔；从首段开始到稳定等待完成保持人物动作跟踪窗口开启，使移动后重新连续 3 帧可信时能更新锚点。观察陈旧或捕获/预览故障也从首次连续不可用时刻累计 `15s`；schema 或视口结构错误在启动前拒绝。人物外观重新连续 3 帧可信后，在下一完整移动周期退出普通回退并恢复视觉授权。`OutsideFrozen` 永不回退盲走。人物外观首次锁定阈值为 `0.70`、空间次峰差值为 `0.06`；已有轨迹和局部恢复阈值为 `0.68`、局部次峰差值为 `0.04`，只接受各轴 `12px` 邻域并连续 3 帧恢复。
6. 视觉冻结只冻结移动，窗口和 Broker 正常时攻击节奏继续。`FacingRestorePending` 中连续视觉不可用达到 `15s` 后结束恢复等待，并在下一轮继续随机攻击和普通回退移动，不得因校准不足保持只攻击不移动。失焦、窗口身份变化、Broker 或释放故障仍停止并 `ReleaseAll`。
7. `VisualStationaryObservationSession` 必须在同一个处理锁内返回“最新可信观察 + 具体移动方向 + 该方向撤销令牌”的原子授权。`Safe -> GuardLeft` 撤销旧的向左令牌但保留向右令牌，`Safe -> GuardRight` 反之，`Outside/Untrusted` 撤销两侧；控制器不得先读取旧观察再另取一个不区分方向的通用令牌。
8. 控制器取得方向绑定的可信授权后，若授权在方向键实际 `KeyDown` 前被相邻帧撤销，必须在同一段内等待序列更新的可信观察并重新执行安全判断，累计等待上限为 `750ms`。不得复用已撤销令牌或绕过保护带；等待超时才允许跳过该段，随机保持时长在每次实际授权时仍独立抽样。授权重试、移动反馈和朝向恢复的视觉条件等待都拆成不超过 `100ms` 的片段，每个片段边界重新检查窗口安全门。
9. 中心带内第一段反向方向键 Down/Up 成功后，或中心带外单向修正使人物背离初始朝向后，进入 `FacingRestorePending`。恢复流程每次使用最新可信位置重新计算：仍在中心带外时继续随机向内，回到中心带后重新随机抽样初始朝向移动。控制器与观察事件发布器在恢复子流程中保持 `FacingRestorePending`，成功后显式发布 `VISUAL_FACING_RESTORED`。连续视觉不可用达到 `15s` 是恢复总上限：达到上限后结束等待并在下一轮进入普通持续攻击回退；普通回退以实际朝向恢复段开始，之后继续完整随机双向周期。保护带拒绝、外框越界和不可信状态不得被普通视觉移动绕过。

Host 向 React 只发布配置状态、运行安全状态、匹配分数和相对安全区中心的 `visualOffsetPx`。负数为左、正数为右。模板像素、平台坐标、随机候选和方向动作不能由 React 提交。结构化日志记录随机候选、裁剪原因、动作前后视觉 X、保护带、真实按压毫秒和时间模型 offset。

已保存且通过 schema 与当前视口校验的视觉配置必须在后续开始时复用。启动预检暂时低于人物锁定阈值、歧义、遮挡或局部未找到时允许启动会话，但攻击以外的移动保持冻结并继续逐帧锁定；不得删除配置或要求用户重新框选。配置缺失、schema 非法、像素数据非法或视口尺寸变化仍拒绝启动并要求重新配置。Broker/UAC 准备完成后、首次输入前复查最新观察，新鲜的临时身份失信仍允许启动，陈旧观察或 fatal 状态必须释放并关闭 Broker、不得发送攻击。

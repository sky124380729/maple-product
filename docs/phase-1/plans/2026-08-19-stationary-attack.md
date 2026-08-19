# 一期定点持续攻击 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 从零构建仅支持 Windows x64 的 Maple Product 一期定点持续攻击，完成 React + Ant Design 配置窗口、.NET 8 Host、管理员 Input Broker、`keybd_event` 输入链路、会话级受限随机移动、权威倒计时、异常安全停止和可验证测试。

**Architecture:** 以平台无关的 `Maple.Core` 承载配置契约、校验、随机节奏、移动规划和显式状态机；Windows Host 只通过 `IBrokerInput` 安全门发送输入，并通过版本化 bridge 向 React 发布状态。管理员 Broker 独立运行在命名管道另一端，唯一真实输入实现为 `keybd_event`。React 只提交配置/会话意图和展示后端 deadline，不生成随机数、不发送按键。

**Tech Stack:** .NET 8/C#、xUnit、WPF + WebView2（Windows Host shell）、named pipe、Win32 `keybd_event`、React + TypeScript + Vite、Ant Design、Vitest + Testing Library。

---

## 规格基线与不可变边界

- 行为以 `docs/PRODUCT_SPEC.md` 为准。
- 一期控制器、协议字段和状态机以 `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md` 为准。
- 验证顺序以 `docs/PHASE_1_ACCEPTANCE.md` 为准。
- 旧仓库只读参考，允许阅读并重写概念；禁止合并分支、整目录复制或迁移 Virtual HID、SendInput、PostMessage、旧三栏工作台。
- `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md` 只用于保留 `IAttackTriggerStrategy` 和观察接口边界；一期不启用识别攻击和自动寻路。
- 所有层共享攻击最大值 `60_000ms`。任何 UI、JSON、Host、Broker 或测试夹具超过该值都必须拒绝。
- 生产输入路径只能是普通权限 Host -> 管理员 Broker -> authenticated named pipe -> `keybd_event`。
- macOS 只运行平台无关单元测试、React 测试和静态检查；不能宣称真实游戏输入有效。

## 实现前必须补齐的规格门槛

以下三点尚未在现有规格中定义。开始 Task 1 的生产代码前，必须由用户确认并先更新 `docs/PRODUCT_SPEC.md`、`docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md` 和 `docs/PHASE_1_ACCEPTANCE.md`：

1. **窗口匹配规则。** 推荐基线：用户在首次配置时选择目标 exe；以后按 canonical executable path 查找可见顶层窗口，并以 HWND/PID/path/start time 建立会话身份。找不到或同一路径有多个候选时停止，不按窗口标题猜测。
2. **攻击键允许范围。** 推荐基线：一期 UI 只提供 Broker 明确映射和 Windows 实机验证过的按键白名单，默认 `Ctrl`；不接受任意字符串或任意虚拟键码。需要确认首批白名单内容。
3. **最低 Windows 版本。** `Windows.Graphics.Capture`、Windows App SDK 通知和 .NET 8 的发布/支持边界需要明确的最低版本。计划暂以 `net8.0-windows10.0.19041.0` 作为技术候选，不将它视为已确认产品承诺。

这三个门槛未确认时，可以搭建纯逻辑测试骨架，但不得完成 WindowLocator、按键映射、预览发布或宣称 Windows x64 兼容范围。

## 设计读取

这是面向 Windows 技术操作人员的配置工具，不是营销页或旧式三栏驾驶舱。主窗口采用 Ant Design 表单和紧凑的状态面板，使用克制的 zinc/slate 中性底色和单一低饱和青绿色强调色；统一 8px 间距、8px 输入圆角、12px 状态面板圆角。`DESIGN_VARIANCE=5` 用于轻微层级偏移，`MOTION_INTENSITY=3` 只使用状态反馈和必要的过渡，`VISUAL_DENSITY=6` 允许较高信息密度但不堆叠无意义卡片。倒计时是唯一强视觉焦点；识别攻击选项保持可见、disabled，并写明“后续版本开放”。所有 loading、error、stopped、异常通知和键盘焦点状态必须可见且可操作。

主窗口桌面布局使用单页双区而非三栏：顶部是一行窗口目标、Broker 和会话状态及开始/停止操作；主体在宽度 `>= 960px` 时为 7/5 比例，左侧是基础配置和高级参数折叠区，右侧是会话状态、阶段、输入状态、错误和倒计时。窄于 `960px` 时按“状态 -> 基础配置 -> 高级参数”单列排列。实时预览只通过按钮打开独立原生窗口，主窗口不保留画面占位区。

## 文件边界

### 创建的 .NET 项目

- `MapleProduct.sln`：解决方案。
- `src/Maple.Core/`：配置模型、校验器、随机源抽象、攻击时长采样、移动规划、状态/节奏消息、触发策略接口；不得引用 Windows API。
- `src/Maple.Host/`：会话控制器、窗口定位/前台安全门、Broker 客户端、通知、日志、bridge DTO；通过接口依赖 Windows 实现。
- `src/Maple.InputBroker/`：管理员 Broker 进程、命名管道认证、协议解析、watchdog、`keybd_event` 适配器。
- `src/Maple.WindowsHost/`：Windows x64 WPF/WebView2 外壳、React 静态资源宿主、独立预览窗口和 Windows 发布入口。

### 创建的测试项目

- `tests/Maple.Core.Tests/`：配置、采样、移动和状态机逻辑测试，可在 macOS 运行。
- `tests/Maple.Host.Tests/`：Fake 窗口、Fake Broker、Fake 时钟、Fake 通知和会话安全门测试，可在 macOS 运行。
- `tests/Maple.InputBroker.Tests/`：协议、序号、心跳、ReleaseAll 和身份策略测试；真实 `keybd_event` 只在 Windows 集成测试运行。
- `client/`：React + TypeScript 配置窗口。
- `client/src/`：bridge 类型、状态 reducer、Ant Design 页面、倒计时 hook、校验错误呈现和样式 token。
- `client/src/**/*.test.tsx`：React 组件和倒计时行为测试。

### 创建的文档和验证证据

- `docs/phase-1/IMPLEMENTATION_NOTES.md`：实现后的协议、运行命令、Windows 实机证据索引。
- `docs/phase-1/evidence/`：只保存脱敏日志、测试输出和 Windows 实机记录，不保存实验 BMP 或旧仓库临时发布目录。

---

## Task 0: 建立工具链前置门槛

**Files:**
- Modify: `README.md`
- Create: `global.json`

- [ ] **Step 1: 记录当前环境事实。** 2026-08-19 的开发机已安装 Node `v22.22.3` 和 npm `10.9.8`，但 `dotnet` 命令不存在；在安装 .NET SDK 前不得宣称 .NET restore/test/build 已运行。
- [ ] **Step 2: 安装 .NET 8 SDK 后锁定 SDK。** 用 `dotnet --version` 获取实际安装的 8.0.x 版本，将该完整版本写入 `global.json`，并设置 `rollForward: latestPatch`；不要在未安装 SDK 时猜写版本号。
- [ ] **Step 3: 验证工具链。**

```bash
dotnet --info
node --version
npm --version
```

Expected: `dotnet --info` 显示 .NET 8 SDK；Node/npm 保持可用。Commit: `chore: pin phase one toolchain`。

---

## Task 1: 建立可跨平台测试的解决方案骨架

**Files:**
- Create: `MapleProduct.sln`
- Create: `src/Maple.Core/Maple.Core.csproj`
- Create: `src/Maple.Host/Maple.Host.csproj`
- Create: `src/Maple.InputBroker/Maple.InputBroker.csproj`
- Create: `src/Maple.WindowsHost/Maple.WindowsHost.csproj`
- Create: `tests/Maple.Core.Tests/Maple.Core.Tests.csproj`
- Create: `tests/Maple.Host.Tests/Maple.Host.Tests.csproj`
- Create: `tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj`
- Create: `client/package.json`, `client/vite.config.ts`, `client/tsconfig.json`

- [ ] **Step 1: 创建 solution 和项目，不添加旧仓库文件。**

```bash
dotnet new sln -n MapleProduct
dotnet new classlib -n Maple.Core -o src/Maple.Core -f net8.0
dotnet new classlib -n Maple.Host -o src/Maple.Host -f net8.0
dotnet new console -n Maple.InputBroker -o src/Maple.InputBroker -f net8.0
dotnet new xunit -n Maple.Core.Tests -o tests/Maple.Core.Tests -f net8.0
dotnet new xunit -n Maple.Host.Tests -o tests/Maple.Host.Tests -f net8.0
dotnet new xunit -n Maple.InputBroker.Tests -o tests/Maple.InputBroker.Tests -f net8.0
npm create vite@latest client -- --template react-ts
```

- [ ] **Step 2: 手工创建 Windows WPF 项目，避免假定 macOS 提供 WPF template。** `src/Maple.WindowsHost/Maple.WindowsHost.csproj` 使用 `Microsoft.NET.Sdk`，设置 `TargetFramework=net8.0-windows10.0.19041.0`、`UseWPF=true`、`RuntimeIdentifier=win-x64`、`EnableWindowsTargeting=true`、`OutputType=WinExe`、`WindowsPackageType=None`、`WindowsAppSDKSelfContained=true` 和 `RestorePackagesWithLockFile=true`，然后将全部项目加入 solution。

```bash
dotnet sln MapleProduct.sln add src/Maple.Core/Maple.Core.csproj src/Maple.Host/Maple.Host.csproj src/Maple.InputBroker/Maple.InputBroker.csproj src/Maple.WindowsHost/Maple.WindowsHost.csproj tests/Maple.Core.Tests/Maple.Core.Tests.csproj tests/Maple.Host.Tests/Maple.Host.Tests.csproj tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj
dotnet add src/Maple.Host/Maple.Host.csproj reference src/Maple.Core/Maple.Core.csproj
dotnet add src/Maple.InputBroker/Maple.InputBroker.csproj reference src/Maple.Core/Maple.Core.csproj
dotnet add src/Maple.WindowsHost/Maple.WindowsHost.csproj reference src/Maple.Core/Maple.Core.csproj src/Maple.Host/Maple.Host.csproj
dotnet add tests/Maple.Core.Tests/Maple.Core.Tests.csproj reference src/Maple.Core/Maple.Core.csproj
dotnet add tests/Maple.Host.Tests/Maple.Host.Tests.csproj reference src/Maple.Host/Maple.Host.csproj
dotnet add tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj reference src/Maple.InputBroker/Maple.InputBroker.csproj
```

- [ ] **Step 3: 安装并锁定依赖。** Client 安装 `antd`、`@ant-design/icons`、`vitest`、`jsdom`、`@testing-library/react`、`@testing-library/jest-dom`；WindowsHost 安装 `Microsoft.Web.WebView2` 和 `Microsoft.WindowsAppSDK`；Host 安装 `Microsoft.Extensions.Hosting` 和 `Microsoft.Extensions.Logging.Console`。生成并提交 `packages.lock.json` 和 npm lockfile，不引入 Virtual HID、SendInput、PostMessage 或全局快捷键包。
- [ ] **Step 4: 运行骨架验证并提交。**

```bash
dotnet restore MapleProduct.sln
dotnet test MapleProduct.sln --configuration Release
npm --prefix client run build
```

Expected: .NET 三个测试项目和 React 空壳构建通过；`git diff --check` 无输出。Commit: `chore: scaffold phase one solution`。

### 旧仓库只读审计结论

实现 Task 6-8 前，仅允许按 `docs/LEGACY_REFERENCE.md` 用 `git show` 阅读指定提交。已确认旧实现存在必须重写的差异：攻击租约通过重复 `KeyDown` 刷新、Broker 攻击上限仍为 `30_000ms`、旧控制器在第一移动前多了一段 gap、没有会话级累计 offset、节奏消息缺少 phase start/deadline。新代码不得复制这些行为；测试必须先锁定本计划定义的 60 秒上限、单次物理 KeyDown、攻击后立即第一方向移动、第二方向完整确认和 deadline 消息。

---

## Task 2: 定义版本化配置契约和校验

**Files:**
- Create: `src/Maple.Core/Configuration/StationaryAttackConfig.cs`
- Create: `src/Maple.Core/Configuration/AttackBand.cs`
- Create: `src/Maple.Core/Configuration/StationaryConfigValidator.cs`
- Create: `src/Maple.Core/Configuration/ConfigValidationResult.cs`
- Create: `tests/Maple.Core.Tests/Configuration/StationaryConfigValidatorTests.cs`

- [ ] **Step 1: 先写边界测试。** 覆盖默认四段 `5/10/60/25`、攻击最大值 `60000`、权重总和不是 100、min 大于 max、零/负数、移动阈值不足、`monsterInRange` disabled 启动拒绝、schemaVersion/source/updatedAt 缺失拒绝。测试断言错误字段和错误码，不只断言 false。
- [ ] **Step 2: 定义不可变模型。** 模型至少包含 `SchemaVersion`、`Source`、`UpdatedAtUtc`、`AttackKey`、`AttackBands`、`MaxLateralMoveMs`、移动/间隔/稳定等待范围、休息开关和概率/范围、`AttackTriggerMode`。`AttackTriggerMode` 显式枚举 `Always`、`MonsterInRange`，不要使用多个布尔值拼接模式。
- [ ] **Step 3: 实现单一硬上限校验。** `StationaryConfigValidator.Validate` 统一检查所有层传入的攻击时长和配置；攻击 band 的 `maxMs`、采样结果、桥接 payload 和 Broker action lease 都复用 `AttackDurationLimitMs=60_000` 常量。
- [ ] **Step 4: 运行测试并提交。**

```bash
dotnet test tests/Maple.Core.Tests --filter FullyQualifiedName~StationaryConfigValidator
```

Expected: 所有边界测试 PASS。Commit: `feat: add stationary configuration contract`。

---

## Task 3: 实现 1ms 粒度攻击节奏采样

**Files:**
- Create: `src/Maple.Core/Rhythm/IRandomSource.cs`
- Create: `src/Maple.Core/Rhythm/WeightedAttackDurationSampler.cs`
- Create: `src/Maple.Core/Rhythm/MonotonicClock.cs`
- Create: `tests/Maple.Core.Tests/Rhythm/WeightedAttackDurationSamplerTests.cs`

- [ ] **Step 1: 写确定性测试。** 注入 `SequenceRandomSource`，验证四个权重区间的选择边界、band 内含端点、结果保留毫秒粒度、`27438ms` 不被量化为整秒、权重总和校验和超过 60000ms 的夹具拒绝。
- [ ] **Step 2: 定义采样接口。** `IRandomSource.NextIntInclusive(min,max)` 是唯一随机入口；React 不实现它。`WeightedAttackDurationSampler.Sample(AttackBand[])` 返回 `{ durationMs, bandIndex }`，不接受未经验证的配置。
- [ ] **Step 3: 实现累计权重选择。** 先用 1-100 的整数抽样选择 band，再在 band 的闭区间内用 1ms 抽样；拒绝浮点概率、整秒取整和固定档位。为测试保留可复现 seed，但生产 Host 每次会话使用独立随机源。
- [ ] **Step 4: 运行测试并提交。**

```bash
dotnet test tests/Maple.Core.Tests --filter FullyQualifiedName~WeightedAttackDurationSampler
```

Expected: 边界和 1ms 粒度测试 PASS。Commit: `feat: add weighted stationary rhythm sampler`。

---

## Task 4: 实现会话级受限随机移动规划器

**Files:**
- Create: `src/Maple.Core/Movement/StationaryMovementPlanner.cs`
- Create: `src/Maple.Core/Movement/MovementPlan.cs`
- Create: `src/Maple.Core/Movement/MovementDirection.cs`
- Create: `tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs`

- [ ] **Step 1: 写状态和不变量测试。** 验证会话启动 offset 为 0；每轮不清零；首方向左右均可出现；第二方向始终相反；两段和 gap 独立抽样；移动后允许非零净位移；任何一步不超过 `[-max,+max]`；剩余预算不足最小 hold 时执行 skip/shorten 的明确安全分支。
- [ ] **Step 2: 定义规划模型。** `MovementPlan` 包含第一段方向/时长、gap、第二段方向/时长和预期 offset；Planner 持有 `RelativeOffsetMs`，提供 `StartSession()` 和 `ApplyCompletedPlan()`，停止后由新会话显式重新置零。
- [ ] **Step 3: 实现预算算法。** 首段只从有足够预算的方向选择；每段在该方向剩余预算与 `[moveHoldMinMs,moveHoldMaxMs]` 的交集内按 1ms 抽样；完成首段后根据新 offset 重新计算反向预算，绝不复用首段时长；第二段无法安全执行时返回不可执行结果，控制器必须安全停止而不是越界。
- [ ] **Step 4: 运行测试并提交。**

```bash
dotnet test tests/Maple.Core.Tests --filter FullyQualifiedName~StationaryMovementPlanner
```

Expected: 随机方向、非零净位移和边界保护测试 PASS。Commit: `feat: add session scoped movement planner`。

---

## Task 5: 建立显式状态机、节奏消息和触发策略边界

**Files:**
- Create: `src/Maple.Core/Session/StationarySessionState.cs`
- Create: `src/Maple.Core/Session/StationaryPhase.cs`
- Create: `src/Maple.Core/Session/StationaryRhythmState.cs`
- Create: `src/Maple.Core/Triggers/IAttackTriggerStrategy.cs`
- Create: `src/Maple.Core/Triggers/AlwaysAttackTriggerStrategy.cs`
- Create: `src/Maple.Core/Triggers/MonsterInRangeTriggerStrategy.cs`
- Create: `tests/Maple.Core.Tests/Session/StationarySessionStateTests.cs`

- [ ] **Step 1: 写契约测试。** 状态只允许 `Idle -> LocatingWindow -> ArmingBroker -> Running.* -> Stopped`；任意运行态可进入 Stopped；`Stopped` 先取消任务、再逐键释放、最后 `ReleaseAll`；`MonsterInRange` 策略在一期返回 disabled/不可运行错误，不能静默换成 Always。
- [ ] **Step 2: 定义消息 DTO。** `StationaryRhythmState` 至少包含 `SchemaVersion`、`SessionId`、`CycleId`、`Phase`、`SampledDurationMs`、`PhaseStartedMonoMs`、`PhaseDeadlineMonoMs`、`RemainingMs`、`EarlyReleaseReason`。停止事件携带失效 session id，使前端清除旧倒计时。
- [ ] **Step 3: 实现独立触发接口。** `IAttackTriggerStrategy.ShouldAttack(ObservationContext)` 只定义判定边界；一期注入 `AlwaysAttackTriggerStrategy`。识别策略只能作为未启用实现存在，不能被配置验证器或 Host 启动。
- [ ] **Step 4: 运行测试并提交。**

```bash
dotnet test tests/Maple.Core.Tests --filter FullyQualifiedName~StationarySession
```

Expected: 状态迁移和 disabled 策略测试 PASS。Commit: `feat: define stationary session contracts`。

---

## Task 6: 实现严格攻击/移动时序控制器

**Files:**
- Create: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Create: `src/Maple.Host/Stationary/IStationaryActionSink.cs`
- Create: `src/Maple.Host/Stationary/IStationarySessionDependencies.cs`
- Create: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/Fakes/RecordingActionSink.cs`

- [ ] **Step 1: 写完整事件序列测试。** 用 FakeClock、FakeBroker 和确定性 sampler 断言每轮严格为 `Attack.Down -> 等待截止 -> Attack.Up -> MoveFirst.Down -> MoveFirst.Up -> MoveGap -> MoveSecond.Down -> MoveSecond.Up -> Stabilizing -> optional Rest -> next Attack.Down`；攻击期间没有移动键，移动期间没有攻击键。
- [ ] **Step 2: 写失败路径和配置快照测试。** 第一段 key-up 失败、第二段 key-down/key-up 失败、失焦、Broker heartbeat 超时、窗口身份变化、取消和未处理异常都必须停止；第二方向失败不得进入下一轮；所有路径最终调用 `ReleaseAll`，异常路径仅通知一次。运行中保存的新节奏配置不得改变当前 cycle，必须在下一完整 cycle 抽样前一次性替换配置快照。
- [ ] **Step 3: 实现控制器。** 控制器只依赖 `IStationaryActionSink`、`IStationarySessionDependencies`、单调时钟、sampler、movement planner 和 trigger strategy；每次动作前检查前台窗口、目标身份、心跳和 lease；每个 cycle 开始时读取一份已验证的不可变配置快照；用可取消 delay，停止时先取消所有 delay/task，再释放活动键，最后发布 Stopped。
- [ ] **Step 4: 实现权威 deadline 发布。** 每次攻击抽样生成新 `cycleId`、阶段开始单调时间和 deadline；`remainingMs` 由当前 clock 计算。前端不得根据固定 `-1` 累积计时。
- [ ] **Step 5: 运行测试并提交。**

```bash
dotnet test tests/Maple.Host.Tests --filter FullyQualifiedName~StationarySessionController
```

Expected: 序列、停止和 ReleaseAll 断言全部 PASS。Commit: `feat: add stationary session controller`。

---

## Task 7: 重写 Broker 协议和 `keybd_event` 输入实现

**Files:**
- Create: `src/Maple.Host/Broker/BrokerProtocol.cs`
- Create: `src/Maple.Host/Broker/BrokerProcessLauncher.cs`
- Create: `src/Maple.Host/Broker/BrokerClient.cs`
- Create: `src/Maple.InputBroker/BrokerServer.cs`
- Create: `src/Maple.InputBroker/BrokerAuthenticator.cs`
- Create: `src/Maple.InputBroker/KeybdEventInputAdapter.cs`
- Create: `src/Maple.InputBroker/BrokerWatchdog.cs`
- Create: `tests/Maple.InputBroker.Tests/BrokerProtocolTests.cs`
- Create: `tests/Maple.InputBroker.Tests/BrokerSafetyTests.cs`
- Create: `tests/Maple.InputBroker.Tests/KeybdEventWindowsTests.cs`

- [ ] **Step 1: 写协议测试。** 覆盖 protocol version、递增 sequence、session/action lease、握手身份、心跳超时、重复/乱序消息、未知 key、`ReleaseAll` 幂等性、恰好 `60_000ms` 的攻击 lease 可接受、`60_001ms` 被拒绝，以及拒绝 `MonsterInRange` 启动。协议字段序列化采用显式 schemaVersion，不用匿名 JSON。
- [ ] **Step 2: 定义动作命令。** 只允许 `KeyDown`、`KeyUp`、`Heartbeat`、`ReleaseAll` 和 `Close`；每条动作包含 session id、目标身份摘要、sequence、lease deadline 和 key code。Host 永远不直接调用 Win32 输入。
- [ ] **Step 3: 实现提升启动和当前用户管道认证。** `BrokerProcessLauncher` 使用 Windows `runas` 请求 UAC 提升，并为每次 Host 会话生成不可预测的 pipe name 和一次性握手 secret；Broker 的命名管道 ACL 只允许发起用户连接。握手验证协议版本、用户 SID、secret、目标身份摘要和递增 sequence。认证、心跳或 lease 失败时 Broker 立即释放全部按键并关闭会话。
- [ ] **Step 4: 实现唯一输入适配器。** `KeybdEventInputAdapter` 是唯一调用 `keybd_event` 的文件；禁止出现 `SendInput`、`PostMessage`、Virtual HID 或 React 键盘路径。重复 KeyDown 不生成重复物理按下，KeyUp 和 `ReleaseAll` 必须可重试且幂等。
- [ ] **Step 5: 运行平台无关测试和静态扫描。**

```bash
dotnet test tests/Maple.InputBroker.Tests --filter FullyQualifiedName~Broker
rg -n "SendInput|PostMessage|Virtual HID|RawKeyboard|keybd_event" src client
```

Expected: 只有 `KeybdEventInputAdapter.cs` 的生产实现包含 `keybd_event`；禁止 API 搜索结果为空，测试 PASS。Commit: `feat: add authenticated keybd event broker`。

---

## Task 8: 实现窗口定位、前台校验和安全门

**Files:**
- Create: `src/Maple.Host/Windows/WindowLocator.cs`
- Create: `src/Maple.Host/Windows/ForegroundSession.cs`
- Create: `src/Maple.Host/Windows/WindowIdentity.cs`
- Create: `src/Maple.Host/Safety/InputSafetyCoordinator.cs`
- Create: `src/Maple.Host/Stationary/StationarySessionApplicationService.cs`
- Create: `tests/Maple.Host.Tests/Windows/WindowLocatorTests.cs`
- Create: `tests/Maple.Host.Tests/Safety/InputSafetyCoordinatorTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/StationarySessionApplicationServiceTests.cs`

- [ ] **Step 1: 写 Fake API 测试。** 找不到窗口、候选不唯一、切换前台失败、最小化、HWND/PID/路径/启动时间不匹配、失焦和窗口消失都必须阻止任何 Broker action；重新开始必须重新定位并绑定新身份。
- [ ] **Step 2: 定义身份快照。** `WindowIdentity` 包含 HWND、PID、normalized process path 和 process start time；`ForegroundSession` 在开始时记录快照，在每个动作前复核当前前台窗口和身份。
- [ ] **Step 3: 实现安全门。** `InputSafetyCoordinator.CanSend` 同时检查前台、身份、Broker heartbeat、动作 lease、会话状态和非最小化；失败结果包含机器可读 reason。失焦走静默停止，其他安全异常交给通知服务。
- [ ] **Step 4: 实现启动编排。** `StationarySessionApplicationService.StartAsync` 严格执行 `Locate -> bind HWND/PID/path/start time -> request foreground -> verify foreground/non-minimized -> launch/connect elevated Broker -> arm target -> start controller`；任一步失败都停止流程并证明 Broker 没有收到 KeyDown。每次用户重新点击开始都创建新 session id 并将移动 offset 清零。
- [ ] **Step 5: 运行测试并提交。**

```bash
dotnet test tests/Maple.Host.Tests --filter "FullyQualifiedName~WindowLocator|FullyQualifiedName~InputSafetyCoordinator"
```

Expected: 所有失败条件都证明“未发送输入”。Commit: `feat: add window identity safety gate`。

---

## Task 9: 完成日志、通知、异常终止记录和版本化 bridge

**Files:**
- Create: `src/Maple.Host/Diagnostics/SessionLog.cs`
- Create: `src/Maple.Host/Diagnostics/NotificationService.cs`
- Create: `src/Maple.Host/Diagnostics/LastAbnormalTerminationStore.cs`
- Create: `src/Maple.Host/Bridge/BridgeMessage.cs`
- Create: `src/Maple.Host/Bridge/StationaryBridgeService.cs`
- Create: `src/Maple.Host/Configuration/JsonConfigStore.cs`
- Create: `tests/Maple.Host.Tests/Diagnostics/NotificationServiceTests.cs`
- Create: `tests/Maple.Host.Tests/Bridge/StationaryBridgeServiceTests.cs`
- Create: `tests/Maple.Host.Tests/Configuration/JsonConfigStoreTests.cs`

- [ ] **Step 1: 写通知和日志测试。** 失焦与用户点击停止不发送系统通知；Broker 断开、释放失败、窗口身份变化、运行时异常各发送一次 Windows 通知并写结构化日志；同一 session 的重复故障不能重复通知。
- [ ] **Step 2: 实现诊断记录。** 日志记录 session/cycle/phase、目标身份摘要、Broker sequence、动作结果、停止原因和 `ReleaseAll` 结果；程序启动时读取上次异常终止记录并通过 bridge 展示。
- [ ] **Step 3: 实现 Windows 系统通知。** 非管理员 Host 使用 Windows App SDK `Microsoft.Windows.AppNotifications` 发布本地通知；管理员 Broker 不直接通知，只把故障返回 Host。Host 启动时注册，退出时注销；同一 session/stop reason 只显示一次。
- [ ] **Step 4: 实现配置持久化和 bridge。** `JsonConfigStore` 采用临时文件 + 原子替换保存最后一份验证通过的配置，损坏文件回退安全默认值并返回可见错误。Bridge 只允许 `loadConfig`、`saveConfig`、`startStationary`、`stopStationary`、`openPreview`、`subscribeState` 等配置/意图命令；Host 发布 `stationary.rhythm.updated` 和最终停止事件。React 不接触 pipe、HWND、随机源或按键 API。
- [ ] **Step 5: 运行测试并提交。**

```bash
dotnet test tests/Maple.Host.Tests --filter FullyQualifiedName~Diagnostics|FullyQualifiedName~Bridge
```

Expected: 通知去重、异常记录和 schemaVersion 测试 PASS。Commit: `feat: add diagnostics and versioned bridge`。

---

## Task 10: 构建 Ant Design 配置窗口

**Files:**
- Create: `client/src/bridge/types.ts`
- Create: `client/src/bridge/bridge.ts`
- Create: `client/src/state/sessionReducer.ts`
- Create: `client/src/hooks/useRhythmCountdown.ts`
- Create: `client/src/pages/StationaryAttackPage.tsx`
- Create: `client/src/components/SessionStatusPanel.tsx`
- Create: `client/src/components/AttackModeField.tsx`
- Create: `client/src/components/AdvancedParametersCollapse.tsx`
- Create: `client/src/styles/tokens.css`, `client/src/styles/app.css`
- Create: `client/src/pages/StationaryAttackPage.test.tsx`
- Create: `client/src/hooks/useRhythmCountdown.test.ts`

- [ ] **Step 1: 先写 bridge 类型和 reducer 测试。** 测试 session/cycle/phase 更新、deadline 替换、旧 session 停止后倒计时失效、loading/error/stopped/abnormal 状态和 disabled mode 错误。
- [ ] **Step 2: 实现 Ant Design 表单。** 使用 `Form`、`Select`、`InputNumber`、`Switch`、`Collapse`、`Alert`、`Button`、`Tag` 等 Ant Design 组件；字段标签置于输入上方；所有一期调试参数可编辑、恢复安全默认值、保存前展示字段级错误。
- [ ] **Step 3: 实现模式边界。** “持续攻击”可选；“识别怪物后攻击”可见但 disabled，旁边显示“后续版本开放”，不能被表单序列化成可运行配置，也不能静默改成持续攻击。
- [ ] **Step 4: 实现权威倒计时。** `useRhythmCountdown` 读取 `phaseDeadlineMonoMs` 与收到消息时的 monotonic offset，按显示刷新计算剩余值，展示 `剩余 27.438 秒`、总时长、cycleId、当前/下一阶段。停止、失焦、异常或 session id 改变时清除旧倒计时，不使用固定递减计数器。
- [ ] **Step 5: 应用设计约束。** 使用单一低饱和青绿色 accent、统一圆角和间距；倒计时面板比普通设置更突出但不闪烁；高级参数放入 Collapse；禁用状态不能只靠颜色；键盘 Tab 顺序、焦点环、错误文本和 `prefers-reduced-motion` 全部可见。不要加入营销 hero、AI 紫色渐变、无意义动画、装饰状态点或三栏驾驶舱。
- [ ] **Step 6: 运行 React 测试并提交。**

```bash
npm --prefix client run test -- --run
npm --prefix client run build
```

Expected: reducer、倒计时和表单状态测试 PASS，生产 build PASS。Commit: `feat: add stationary attack configuration ui`。

---

## Task 11: 接入 WPF/WebView2 主窗口和独立预览窗口

**Files:**
- Modify: `src/Maple.WindowsHost/MainWindow.xaml`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Create: `src/Maple.Host/Preview/IFrameCaptureSource.cs`
- Create: `src/Maple.WindowsHost/Preview/WindowsGraphicsCaptureSource.cs`
- Create: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`
- Create: `src/Maple.WindowsHost/Preview/PreviewDiagnostics.cs`
- Create: `tests/Maple.Host.Tests/Preview/PreviewWindowHostTests.cs`

- [ ] **Step 1: 写生命周期测试。** `openPreview` 创建独立原生窗口；关闭、卡顿或崩溃只记录诊断并通知 UI，不停止持续攻击；同一预览重复打开只复用或安全替换句柄。
- [ ] **Step 2: 实现主窗口 bridge。** WPF 只负责承载 React 静态资源和转发版本化 bridge，不把逐帧图像塞进 React 状态；窗口尺寸适配 Windows 桌面和键盘操作。
- [ ] **Step 3: 实现预览最小能力。** `IFrameCaptureSource` 隔离采集接口，Windows 实现使用 `Windows.Graphics.Capture` 并通过绑定的游戏 HWND 创建 capture item；独立原生窗口显示采集画面、FPS、frame age 和 dropped frame 诊断，识别框不实现。采集线程和攻击会话分离，预览异常不能阻塞 controller。
- [ ] **Step 4: 在 Windows 目标框架下构建并提交。**

```bash
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64
```

Expected: Windows Host 编译成功；macOS 只要求 Core/Host 测试与静态检查通过。Commit: `feat: add windows host and isolated preview window`。

---

## Task 12: 完成发布、验证和一期证据

**Files:**
- Create: `docs/phase-1/IMPLEMENTATION_NOTES.md`
- Create: `docs/phase-1/evidence/macos-tests.txt`
- Create: `docs/phase-1/evidence/windows-x64-build.txt`
- Create: `docs/phase-1/evidence/windows-real-input.md`
- Modify: `README.md`

- [ ] **Step 1: 建立 macOS 验证脚本。** `scripts/test-macos.sh` 依次运行 `dotnet test`、React test/build、`git diff --check`、禁用 API 搜索；脚本失败即退出非零。
- [ ] **Step 2: 建立 Windows x64 发布命令。** 使用 `dotnet publish src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64 --self-contained true` 和 Broker 同样的 RID，输出到明确的 `artifacts/phase-1/win-x64/`；只打包完整发布目录或 ZIP，不生成安装程序。
- [ ] **Step 3: 按验收清单记录 Windows 实机证据。** 记录 Broker 管道认证、真实 `keybd_event` 返回值、前台/失焦、窗口身份变化、heartbeat/watchdog、异常通知、长时间运行和游戏画面响应。明确写出 macOS 测试和交叉编译不能证明真实游戏响应。
- [ ] **Step 4: 更新 README 阅读和运行入口。** 说明一期启动顺序、配置文件 schema、Windows x64 限制、预览独立窗口、识别攻击 disabled 和 Windows 实机证据位置。
- [ ] **Step 5: 运行最终验证并提交。**

```bash
./scripts/test-macos.sh
dotnet publish src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/Maple.InputBroker/Maple.InputBroker.csproj -c Release -r win-x64 --self-contained true
```

Expected: 平台无关测试、React 构建、静态禁用 API 检查和 Windows x64 交叉编译通过；Windows 实机记录单独标注未完成项，不以 macOS 结果代替。Commit: `docs: record phase one verification evidence`。

---

## 验收覆盖矩阵

| 验收项 | 计划任务 | 证据 |
|---|---|---|
| A. 采样、1ms 粒度、权重、移动阈值、配置拒绝 | 2-4 | Core xUnit 输出 |
| B. 显式状态机、完整按键序列、第二方向不可跳过、ReleaseAll | 5-6 | Controller 事件序列测试 |
| C. UI 模式、deadline 倒计时、停止清除、配置校验 | 9-10 | React/Vitest + bridge 测试 |
| D. 窗口身份、Broker、`keybd_event`、失焦/异常通知、watchdog | 7-9、12 | Host/Broker 测试 + Windows 实机记录 |
| E. 工具链、macOS 测试、win-x64 构建、完整发布目录 | 0-1、12 | 工具版本、脚本输出和发布目录 |
| 二期边界 | 5、10、11 | `IAttackTriggerStrategy` 存在但识别模式不可运行 |

## 自审结果

- 没有从旧仓库复制文件；所有旧仓库参考只通过重新定义的接口、协议测试和 Windows 实机测试进入实现。
- 没有把识别模式降级为持续攻击；配置、Host 和 UI 三层都拒绝 disabled 策略启动。
- 攻击上限、会话级 offset、第二方向完成确认、deadline 倒计时、失焦静默停止和异常一次通知均有独立任务与测试证据。
- 预览被隔离为独立原生窗口，不嵌入主配置窗口，也不阻塞持续攻击。
- 计划不宣称 macOS 或交叉编译能证明真实游戏响应；真实 `keybd_event` 结果必须来自 Windows x64 实机记录。

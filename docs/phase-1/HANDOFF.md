# 一期定点持续攻击开发交接

日期：2026-08-19  
交接分支：`master`  
开发规则：只允许在 `master` 开发、提交和推送，AI 不得自行创建或切换其他开发分支。  
基线提交：`780ac6e docs: define maple product phase one`

## 1. 交接结论

当前代码已经建立一期定点持续攻击的完整工程骨架，并实现了大部分平台无关核心逻辑、Host 会话控制、Broker 安全协议、React 配置界面和 Windows WPF 外壳。

当前不能宣称一期完成。真实采集预览、通知接线、结构化日志、异常终止恢复、配置启动加载、攻击分段编辑、运行中配置切换、完整发布打包和 Windows 实机验证仍未完成。

## 2. 必须继续遵守的边界

- 成品只支持 Windows x64。macOS 只用于静态检查、前端构建和平台无关测试。
- 生产输入唯一允许：普通权限 Host -> 管理员 Broker -> 当前用户命名管道 -> `keybd_event`。
- 禁止引入 Virtual HID、`SendInput`、`PostMessage` 或 React 原始按键输入路径。
- React 只提交配置和会话意图、显示 Host 状态，不生成随机数、不发送按键、不承载逐帧图像。
- `识别怪物后攻击` 一期必须可见但 disabled，不得静默降级为持续攻击。
- 自动寻路属于二期，只能按 `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md` 预留接口。
- 实时预览必须是独立原生窗口，不嵌入主配置窗口。
- 旧仓库 `/Users/zhengquan/Desktop/_projects_/maple` 只读参考，禁止整分支或整目录复制。
- 产品行为、设计和验收依次以 `docs/PRODUCT_SPEC.md`、`docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`、`docs/PHASE_1_ACCEPTANCE.md` 为准。
- 所有后续开发、提交和推送都必须直接在 `master` 进行，不创建功能分支。

## 3. 已确认的产品决策

- 最低发布目标为 Windows 10 22H2 x64，同时支持 Windows 11 x64。
- 目标游戏通过 Host 原生文件选择器选择 exe。
- 后续窗口定位只按规范化完整 exe 路径进行。零候选或多候选都停止，不发送输入。
- 窗口会话身份绑定 HWND、PID、规范化进程路径和进程启动时间。
- 攻击键白名单为 `Ctrl`、`Shift`、`Space`、`A`、`S`、`D`、`F`、`Z`、`X`、`C`、`V`，默认 `Ctrl`。
- 攻击持续时间硬上限为 `60,000ms`。

## 4. 当前实现范围

### Maple.Core

- `StationaryAttackConfig`、攻击分段和版本化配置校验。
- 默认四段权重 `5/10/60/25`，按 `1ms` 粒度抽样，统一 `60,000ms` 上限。
- 会话级受限左右随机移动规划器，累计位移不在每轮清零。
- 显式会话阶段、周期 ID、单调时钟开始/截止字段和停止原因。
- `AlwaysAttackTriggerStrategy` 已启用，`MonsterInRangeTriggerStrategy` 保持 disabled。
- 长度前缀 JSON Broker 编解码契约。

### Maple.Host

- 严格执行攻击 -> 第一方向移动 -> 无键间隔 -> 相反方向移动 -> 稳定等待 -> 可选休息。
- 第二方向失败会停止并执行 `ReleaseAll`，不会跳过后继续攻击。
- 攻击实际抽样时长会作为 Broker lease 发送。
- 窗口身份、前台状态、Broker 健康度安全门。
- 零窗口和多窗口候选的显式失败结果。
- JSON 配置原子保存和读取基础设施。
- 异常通知策略及去重基础设施。
- UAC Broker 进程启动、命名管道客户端和 heartbeat loop。
- Windows 版本启动门。

### Maple.InputBroker

- 协议版本、递增 sequence、session identity 和一次性握手 secret。
- 当前用户专用命名管道、心跳和独立 watchdog。
- `60,000ms` 攻击租约边界。
- 重复 `KeyDown` 只刷新租约，不重复调用物理输入。
- 幂等 `ReleaseAll`。
- 目标进程路径和启动时间校验。
- 生产代码中只有 `src/Maple.InputBroker/KeybdEventInputAdapter.cs` 调用 `keybd_event`。

### React Client

- React、TypeScript、Vite、Ant Design 单页配置工具。
- 设计读取为 Windows 技术操作工具，视觉参数 `DESIGN_VARIANCE=5`、`MOTION_INTENSITY=3`、`VISUAL_DENSITY=6`。
- 克制的中性色和单一青绿色强调色，统一字体、圆角、间距和键盘可操作组件。
- 持续攻击模式可用，识别攻击模式可见且 disabled。
- 基于 Host deadline 的毫秒倒计时，不用固定递减计数器。
- loading、locating、arming、running、stopped、error 状态 reducer。
- 移动、间隔、稳定等待和休息参数位于高级折叠区。
- WebView2 消息 bridge 和独立预览按钮。

### Maple.WindowsHost

- Windows x64 WPF + WebView2 外壳。
- 原生 exe 选择器。
- 原生顶层窗口枚举、完整路径匹配、前台激活和身份校验。
- 独立原生预览窗口边界。

## 5. 明确未完成项

按继续开发优先级排列：

1. 独立预览窗口目前只是占位文字，尚未接入 `Windows.Graphics.Capture`，没有真实画面、FPS、frame age 或 dropped frames。
2. `WindowsSystemNotificationSink` 已存在，但没有接入会话异常停止流程。
3. 尚无结构化会话日志。
4. 尚未持久化上次异常终止记录，也没有在下次启动时展示。
5. `JsonConfigStore.LoadAsync` 已存在，但 WindowsHost 启动时没有把已保存配置发给 React。
6. React 不能编辑四个攻击时长分段和权重，因此未满足“所有一期调试参数可编辑”。
7. 运行中保存配置不会在下一完整周期切换。当前控制器使用启动时的固定 `ValidatedConfigProvider`。
8. Host/Broker 发布打包未完成。WindowsHost 运行时假设 `Maple.InputBroker.exe` 与 Host 位于同一目录。
9. Windows 实机验证尚未开始，包括 UAC、ACL、`keybd_event` 游戏响应、失焦、身份变化、watchdog、通知和长时间运行。
10. Windows 10 22H2 和 Windows 11 的启动门实机证据未记录。
11. `NamedPipeBrokerClient.DisposeAsync` 先把 `disposed` 置为 1，再调用经过 `IsHealthy` 检查的 `SendAsync(Close)`，因此 `Close` 实际不会发送。当前 Host 会先显式 `ReleaseAll`，Broker watchdog 也是兜底，但此处仍应修正并补测试。
12. 主窗口关闭时需要复核 heartbeat loop 的释放顺序。当前关闭处理取消会话并直接 dispose connection，没有显式等待 heartbeat loop dispose。
13. `PreviewWindowHost` 在每次 bridge 命令中被新建，当前窗口复用字段不能跨命令持久化，可能允许重复打开多个预览窗口。
14. `Maple.WindowsHost.csproj` 中 `client/dist` Content Include 重复，应清理后再做正式发布。
15. React 表单目前主要依赖字段级 required/min/max，跨字段范围、权重总和等完整错误仍由 Host 拒绝，前端错误映射可以进一步细化。

## 6. 已有测试覆盖

- Core：配置边界、攻击权重采样、毫秒粒度、移动边界、Broker wire codec、状态/策略契约。
- Host：严格动作序列、第二方向失败、实际攻击 lease、停止/释放、窗口候选、前台/身份安全门、配置持久化、通知策略。
- Broker：握手、sequence、heartbeat/watchdog、租约边界、重复 KeyDown、幂等 `ReleaseAll`、目标身份。
- React：deadline 倒计时、页面交互、disabled 模式和 session reducer。

本交接提交前的最新验证结果记录在第 9 节。macOS 测试和交叉编译不能替代 Windows 实机证据。

## 7. 推荐继续开发顺序

1. 修复 Broker dispose/Close、heartbeat 关闭顺序和预览单例生命周期，并补回归测试。
2. 完成配置启动加载、攻击分段编辑和跨字段校验。
3. 增加可热更新的配置 provider，确保新配置只在下一完整周期生效。
4. 接入结构化日志、异常终止记录和系统通知。
5. 实现独立原生采集预览及 FPS/frame-age 诊断，保证预览故障不影响攻击会话。
6. 完成 Host + Broker + client 静态资源的 win-x64 发布目录或 ZIP。
7. 在 Windows 10 22H2 x64 和 Windows 11 x64 执行 `docs/PHASE_1_ACCEPTANCE.md` 的 D/E 项，保存脱敏证据到 `docs/phase-1/evidence/`。

## 8. 开发和验证命令

macOS 当前可用的临时 .NET SDK：

```bash
/tmp/maple-dotnet/dotnet --version
```

平台无关验证：

```bash
DOTNET_BIN=/tmp/maple-dotnet/dotnet ./scripts/test-macos.sh
```

Windows 目标交叉构建：

```bash
/tmp/maple-dotnet/dotnet build MapleProduct.sln -c Release -p:EnableWindowsTargeting=true
```

准备 self-contained win-x64 发布前先恢复 RID runtime packs：

```bash
/tmp/maple-dotnet/dotnet restore src/Maple.WindowsHost/Maple.WindowsHost.csproj -r win-x64
/tmp/maple-dotnet/dotnet restore src/Maple.InputBroker/Maple.InputBroker.csproj -r win-x64
```

不要把 `bin/`、`obj/`、`client/dist/`、`client/node_modules/` 或发布目录提交到 Git。

## 9. 本次交接验证结果

交接时已执行的验证仅用于记录当前基线，不表示一期完成：

- .NET SDK：`8.0.424`
- Core tests：26 passed，0 failed
- Host tests：19 passed，0 failed
- Broker tests：7 passed，0 failed
- React tests：6 passed，0 failed
- React build：通过；Ant Design vendor chunk 为 684.36 kB，Vite 给出大 chunk warning
- React lint：命令退出 0；`useRhythmCountdown.ts` 有 2 条 warning（effect 内同步 setState、缺少 `rhythm` dependency）
- Windows target build：通过，0 warnings，0 errors
- RID restore：WindowsHost 和 InputBroker 的 `win-x64` restore 均通过
- Self-contained publish smoke check：WindowsHost 和 InputBroker 分别发布到临时目录成功；尚未组合成正式交付目录或 ZIP
- 禁止 API 静态扫描：未发现 `SendInput`、`PostMessage`、Virtual HID 或 RawKeyboard；只有 Broker adapter 包含 `keybd_event`
- `git diff --check`：通过

以上结果不能替代 Windows 实机验收，也不改变第 5 节的未完成状态。

## 10. 下一位 AI 的首次检查清单

```bash
git status --short --branch
git log -1 --oneline --decorate
sed -n '1,240p' AGENTS.md
sed -n '1,240p' docs/PRODUCT_SPEC.md
sed -n '1,260p' docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md
sed -n '1,160p' docs/PHASE_1_ACCEPTANCE.md
sed -n '1,160p' docs/LEGACY_REFERENCE.md
sed -n '1,320p' docs/phase-1/HANDOFF.md
```

继续写前端时必须再次完整读取：

```text
/Users/zhengquan/Desktop/_workspace_/digital-elevator-subsystem/.agents/skills/design-taste-frontend/SKILL.md
```

不要把交接文档中的“已实现”当成验收勾选。必须用测试、构建和 Windows 实机证据逐项更新 `docs/PHASE_1_ACCEPTANCE.md`。

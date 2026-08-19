# 一期实现记录

## 当前已实现

- `Maple.Core`：配置 schema/校验、四段权重攻击采样、1ms 粒度、会话级受限随机移动、显式节奏状态和 disabled 识别触发策略。
- `Maple.Host`：严格攻击/移动控制器、窗口身份/前台安全门、JSON 配置原子持久化、异常通知策略、Broker 命名管道客户端、UAC 启动入口。
- `Maple.InputBroker`：协议帧编解码、握手 secret、sequence、heartbeat、lease、watchdog、幂等 `ReleaseAll`、显式当前用户 SID 管道 ACL、唯一 `keybd_event` 适配器。
- `client`：Ant Design 单页配置窗口、持续攻击/disabled 识别模式、四段攻击权重编辑、完整跨字段校验、配置恢复、状态面板和 deadline 倒计时。
- `Maple.WindowsHost`：Windows x64 WPF + WebView2 外壳、配置启动加载、周期边界配置热更新、按精确窗口指纹自动发现客户端、窗口枚举/前台校验、结构化日志、异常终止记录、系统通知和独立 Windows Graphics Capture 预览。
- `scripts/publish-windows.ps1`：组合 self-contained Host、Broker 和 React 静态资源，生成完整发布目录与 ZIP。

## 尚未完成的实机证据

- UAC 提升、当前用户命名管道 ACL、Windows 系统通知实际弹出。
- `keybd_event` 对真实游戏的输入效果。
- `Windows.Graphics.Capture` 在目标游戏上的真实画面、FPS、frame age 和掉帧诊断表现。
- Windows 10 22H2 与 Windows 11 x64 实机的失焦、窗口身份变化、watchdog 和长时间运行。

这些项目必须在 Windows 实机记录后才能勾选 `docs/PHASE_1_ACCEPTANCE.md` 的 D/E 项，不能用 macOS 测试或交叉编译替代。

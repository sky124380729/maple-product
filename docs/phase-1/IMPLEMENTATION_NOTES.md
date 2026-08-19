# 一期实现记录

## 当前已实现

- `Maple.Core`：配置 schema/校验、四段权重攻击采样、1ms 粒度、会话级受限随机移动、显式节奏状态和 disabled 识别触发策略。
- `Maple.Host`：严格攻击/移动控制器、窗口身份/前台安全门、JSON 配置原子持久化、异常通知策略、Broker 命名管道客户端、UAC 启动入口。
- `Maple.InputBroker`：协议帧编解码、握手 secret、sequence、heartbeat、lease、watchdog、幂等 `ReleaseAll`、唯一 `keybd_event` 适配器。
- `client`：Ant Design 单页配置窗口、持续攻击/disabled 识别模式、移动与休息高级参数、状态面板、deadline 倒计时、错误/停止/预览状态。攻击分段编辑仍待实现。
- `Maple.WindowsHost`：Windows x64 WPF + WebView2 外壳、原生 exe 选择器、窗口枚举/前台校验、独立预览窗口边界。预览当前仍是采集占位窗口。

## 尚未能在 macOS 证明的项目

- UAC 提升、当前用户命名管道 ACL、Windows 系统通知实际弹出。
- `keybd_event` 对真实游戏的输入效果。
- `Windows.Graphics.Capture` 真实采集画面、FPS、frame age 和掉帧诊断。
- Windows 10 22H2 与 Windows 11 x64 实机的失焦、窗口身份变化、watchdog 和长时间运行。

这些项目必须在 Windows 实机记录后才能勾选 `docs/PHASE_1_ACCEPTANCE.md` 的 D/E 项，不能用 macOS 测试或交叉编译替代。

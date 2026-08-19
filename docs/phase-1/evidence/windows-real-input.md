# Windows x64 实机验收记录

状态：部分完成。不得以自动化测试或构建结果代替游戏画面响应。

## 环境

- Windows 版本：Windows 11 专业版 x64，10.0.26200
- 游戏客户端完整路径：`E:\...\Maplestory_Classic.exe`（已脱敏）
- Host/Broker 构建：Release self-contained win-x64 发布目录
- 测试时间：2026-08-19 16:25 +08:00

## 检查项

- [x] 生产 `NativeWindowLocator` 按标题 `冒险岛怀旧服` + 类 `UnityWndClass` 唯一发现运行中客户端，并绑定 HWND/PID/实际路径/启动时间。
- [ ] 从前台 Host 点击开始后成功切换游戏前台。非交互命令行入口实测被安全拒绝为 `FOREGROUND_SWITCH_FAILED`，期间未启动 Broker、未发送输入。
- [ ] UAC 后 Broker 启动；当前用户命名管道握手、sequence 和 target identity 正常。
- [ ] `keybd_event` 的 Attack Down/Up 在结构化日志中成功，游戏画面产生对应响应。
- [ ] 左右移动严格为第一方向、间隔、相反方向、稳定等待且无攻击重叠。
- [ ] 切换焦点后静默停止并释放按键，不自动恢复、不发送系统通知。
- [ ] 关闭 Host 或中断 heartbeat 后 Broker watchdog 释放全部按键。
- [ ] 窗口消失/身份变化、Broker 故障和释放失败各只通知一次。
- [ ] 独立预览窗口显示实时画面、FPS、frame age 和 dropped frames；关闭预览不影响攻击。
- [ ] Windows 10 22H2 x64 启动门实测。
- [x] Windows 11 x64 启动门实测：发布版 Host 成功打开主窗口并保持响应。
- [ ] 长时间运行期间没有卡键、越界移动、重复 Attack Down 或倒计时遗留。

## 事件摘录

- 2026-08-19 16:28：首次点击开始后，Host 因管理员 Broker 使用 `PipeOptions.CurrentUserOnly` 创建的管道拒绝普通权限客户端而抛出 `UnauthorizedAccessException`；Broker 尚未握手或发送输入，Host 被未捕获异常终止。
- 已改为受保护的显式 DACL，只给当前 Windows 用户 SID `FullControl`，并将同类启动异常降级为 UI 错误；等待再次从前台 Host 点击开始完成跨完整性级别复测。

## 游戏画面结论

待实测。此项是确认真实输入有效性的唯一依据之一，不能由 `dotnet test`、交叉编译或 Broker 单元测试代替。

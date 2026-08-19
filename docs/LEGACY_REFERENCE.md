# 旧仓库参考与迁移边界

旧仓库路径：`/Users/zhengquan/Desktop/_projects_/maple`  
远程：`https://github.com/sky124380729/maple.git`

旧仓库只读。新仓库不得合并旧分支；迁移方式必须是阅读、抽取、重写、重新测试。

## Windows 输入与 Broker

- 分支：`origin/codex/windows-integrated`
- 提交：`6e281e8 fix: allow long attack actions through input broker`
- 参考文件：
  - `src/Maple.Host/BrokerProcessLauncher.cs`
  - `src/Maple.Host/BrokerClient.cs`
  - `src/Maple.Host/BrokerInputAdapter.cs`
  - `src/Maple.InputBroker/`
  - `src/Maple.Input/KeybdEventInputAdapter.cs`
- 可迁移概念：管理员 Broker、随机命名管道、协议版本和递增序号、目标身份校验、心跳、watchdog、`ReleaseAll`、`keybd_event`。
- 必须重新审查：30 秒攻击上限、帧新鲜度与定点攻击的耦合、重复 KeyDown 租约、旧测试对真实游戏响应的缺失。

## Windows 定点控制器

- 原始分支：`origin/codex/windows-runtime`
- 原始提交：`46943b8 feat: 定点`
- 集成分支后续提交：`922c664`、`ec900fc`、`393762c`、`6e281e8`
- 参考文件：`src/Maple.Host/StationaryAttackController.cs`、`src/Maple.Core/StationaryAttackRhythm.cs`。
- 只参考时序和测试意图；新实现必须改成会话级累计移动、严格第二方向完成确认和 1ms 粒度配置。

## 禁止迁移

- Virtual HID 生产路径和相关设备契约。
- SendInput、PostMessage、旧 WinForms/探针路径。
- 旧 master 的虚拟 HID 规格和与新产品冲突的项目主规格。
- 旧三栏实时预览工作台作为主 UI。
- 实验 BMP、临时发布目录、静态探针证据和无产品价值的脚手架。
- 任何未重新测试就直接复制的控制器、契约或安全判断。

## 读取旧代码的命令

```bash
git -C ../maple show origin/codex/windows-integrated:src/Maple.Host/StationaryAttackController.cs
git -C ../maple show origin/codex/windows-integrated:src/Maple.InputBroker/BrokerInputSession.cs
git -C ../maple log origin/codex/windows-integrated --oneline -- src/Maple.Host/StationaryAttackController.cs
```

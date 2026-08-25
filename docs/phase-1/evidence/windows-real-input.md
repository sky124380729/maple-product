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

## 2026-08-23 持续攻击移动预算修正

- 实机会话 `a2ca8122-ec0c-4f57-8a9b-72de1c2c929e` 在 offset `-12ms` 时随机请求第一段左移 `46ms`，Broker 返回实际 `62ms`、释放迟到 `16ms`，offset 更新为 `-74ms`；旧规划器只按计划值预演，随后误报 `MOVEMENT_BUDGET_EXHAUSTED`。
- 修正后第一段候选会验证从计划值到“计划值 + 20ms”的全部允许实际落点均存在合法第二段；随机抽样、固定方向顺序、回中概率和 `80ms` 硬边界保持不变。
- Release 自动验证：Core 43、InputBroker 46、Host 302，共 391 项通过；前端 44 项通过。
- 发布 EXE：`artifacts/phase-1/win-x64-continuous-budget-jitter-fix/MapleProduct/Maple.WindowsHost.exe`
- 核心 `Maple.Core.dll` SHA-256：`014EB448E668AF6E18C99C2E4D379A794220E6851FE2422E3C9941514995D232`
- ZIP：`artifacts/phase-1/win-x64-continuous-budget-jitter-fix/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`38B4107C95C0A3381138410DCBBC572E659CD182C596F89272D8947EDE926EB0`
- 发布目录 499 个文件、ZIP 500 个条目已逐项读取；包内 `Maple.Core.dll` 与本次 Release 测试输出哈希一致。

## 2026-08-23 持续攻击柔性恢复修正

- 实机会话 `57719898-549e-4f61-b3a7-a3e4bb00c98e` 的回中轮从 `+40ms` 经左移真实 `46ms`、右移真实 `47ms` 后落在 `+41ms`，旧逻辑因释放抖动导致绝对偏移增加 `1ms`，以 `MOVEMENT_RETURN_UNSATISFIED` 停止。
- 实机会话 `525dfab3-a602-4848-84f8-590b8ab36d68` 在上一轮落于 `-22ms` 后，下一轮固定第一方向没有完整双段预算，旧逻辑以 `INITIAL_FACING_BUDGET_EXHAUSTED` 停止。
- 修正后，回中轮因真实释放抖动变差时记录回中债务并强制下一轮继续回中；固定第一方向没有安全候选时，随机执行一次初始朝向的安全恢复动作。恢复也没有候选时仅冻结本轮移动，攻击继续。真实时长非法、实际 offset 越过硬边界、窗口和 Broker 故障仍停止。
- Release 自动验证：Core 47、InputBroker 46、Host 303，共 396 项通过；前端 44 项通过。
- 发布 EXE：`artifacts/phase-1/win-x64-continuous-soft-recovery/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- 核心 `Maple.Core.dll` SHA-256：`2B25549B1333B6E99396A77D9B901EFA9DB851943B824CF919C74B22701B31AE`
- ZIP：`artifacts/phase-1/win-x64-continuous-soft-recovery/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`213DE4C43EA74D040FD1D02DB54D73B14F2A4C4DE312C2AE1CA5A53F9FE0DD9B`
- 发布目录 499 个文件、ZIP 500 个条目已逐项读取；包内 `Maple.Core.dll` 与本次 Release 测试输出哈希一致。

## 2026-08-23 视觉人物外观首次锁定修正

- 实战失败截图用当前 schema 2 人物外观配置重放时，旧匹配器最佳分数约 `0.75`，但首次阈值仍硬编码为 `0.88`；同一人物相邻对齐峰还会被当作空间第二候选，导致启动统一报 `VISUAL_SELF_NOT_TRUSTED`。
- 人物外观首次锁定阈值与跟踪统一为 `0.72`，仍要求保存位置各轴 `12px` 邻域和连续 3 个新帧；同一人物半模板范围内的重叠峰合并，空间分离的真实第二候选仍触发冻结。
- 抗遮挡评分加入归一化亮度结构证据，避免纯背景被稳健分量抬过 `0.72`；启动失败改为发布并记录实际识别代码和分数，界面统一使用“人物外观”提示。
- Release 自动验证：Core 47、InputBroker 46、Host 304，共 397 项通过；前端 45 项通过。打包后 `Maple.Host.dll` 重放失败截图，第 1/2 帧为 `VISUAL_SELF_ACQUIRING`，第 3 帧以约 `0.73` 进入 `VISUAL_SAFE`，中心 X=`1033`。
- 发布 EXE：`artifacts/phase-1/win-x64-visual-acquisition-repair/MapleProduct/Maple.WindowsHost.exe`
- 核心 `Maple.Host.dll` SHA-256：`1CED45D5F528BB84EB11F044C2E3081A2566C4763809F28FB1AA1195F1700177`
- ZIP：`artifacts/phase-1/win-x64-visual-acquisition-repair/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`EEF25F8F6D6F5022FB5762904C8225B66C7AD5CEC7974C5B5671D2E89C3D99DE`
- 发布目录 499 个文件、ZIP 500 个条目已逐项读取；包内 `Maple.Host.dll` 与本次 Release 测试输出哈希一致。

## 2026-08-23 视觉失信位置预测回退与配置复用

- 可信视觉移动按左右方向分别记录人物中心 X 变化与 Broker `actualHoldMs`，过滤反向、少于 `2px` 以及 `0.05..2.50 px/ms` 之外的样本；每侧保留最近 8 个并取中位数，左右各至少 2 个有效样本才允许预测回退。
- 连续 15 秒新鲜临时身份失信后，以最后可信 `visualOffsetPx` 为锚点，用真实移动毫秒推进预测位置。候选同时通过平台像素边界、逐段增长的不确定度、`20ms` 释放余量和 `maxLateralMoveMs`；无可证安全候选冻结移动，随机攻击继续。
- 观察陈旧、重复旧帧、捕获/预览/schema/视口故障和 `FacingRestorePending` 禁止进入或继续回退并作废锚点；视觉短暂恢复会重置 15 秒计时，不能用旧 active 状态绕过。恢复为平台外框越界时 UI 显示越界冻结，不保留回退状态。
- 已保存的有效视觉配置在重新开始时复用；3 秒内暂时低于 `0.72` 不再要求重画，而是启动攻击、冻结移动并持续锁定。Broker/UAC 准备完成后、首次输入前再次拒绝陈旧或 fatal 观察。
- Release 自动验证：Core 47、InputBroker 46、Host 331，共 424 项通过；前端 46 项通过。Windows Host Release 构建零警告；前端 lint 无错误，保留 4 条既有 Hook 警告。
- 发布 EXE：`artifacts/phase-1/win-x64-visual-position-fallback/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- 核心 `Maple.Host.dll` SHA-256：`F275EC03DE4C3D43F2CBD7659B760833A91E9EF8B4F697905F1DAE6DA8B79B17`
- ZIP：`artifacts/phase-1/win-x64-visual-position-fallback/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`C421D63A1A0EF1FE1C0CCBFB98CE2392B276E83583EAC2586CF021AF52AAD469`
- 发布目录 499 个文件、ZIP 499 个文件条目已逐项读取；测试输出、发布目录和 ZIP 内 `Maple.Host.dll` 哈希一致。

## 2026-08-24 定点移动精确释放与视觉预览诊断

- 实机会话 `e090cc5e-0459-492c-8651-b3a85f8c60be` 中，右方向计划保持 `34–46ms`，但 Host 调度停顿使物理按压约 `235ms`，最终以 `MOVEMENT_OFFSET_EXCEEDED` 停止。旧 `HostKeyUp` 请求未进入高精度移动调度器，只能等待 250ms 普通 watchdog。
- 修正后，定点 `HostKeyUp` 正常仍由 Host 按随机计划主动释放；Broker 同时为当前方向键注册“计划时长 + `20ms`”精确兜底，截止只释放该方向键并缓存实际时长。Host 恢复后的幂等 `KeyUp` 取得真实计时并继续循环；寻路 `BrokerDeadline` 仍严格按请求时长截止。
- 高优先级移动调度器的连续检查窗口从 `100ms` 收紧为 `20ms`，避免每次短移动长时间占满一个逻辑核心。普通 watchdog 不再抢先处理方向键或连带释放攻击键。
- 原生预览直接标注黄色“平台外边界”、绿色“随机移动安全内区”和蓝色“人物模板”；同一观察会话以青色“实时本人”或橙色“实时候选”显示实际匹配框与分数，并与同一序列捕获帧成对绘制。
- 原生预览新增“清除视觉配置”。主界面与预览共享启动/运行生命周期门：攻击或寻路启动准备及运行时拒绝重新配置和清除；空闲清除会删除持久化配置、观察会话和全部视觉框。
- Release 自动验证：Core 47、InputBroker 46、Host 334，共 427 项通过；前端 46 项通过。Windows Host Release 构建零警告；前端 lint 无错误，保留 4 条既有 Hook 警告。
- 发布 EXE：`artifacts/phase-1/win-x64-stationary-safety-visual-diagnostics/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- `Maple.WindowsHost.dll` SHA-256：`B918C32CD6D41D6BC8E00F2FE2831A0E5C86A74C96FA1F4ADFAA46AF55D49561`
- `Maple.Core.dll` SHA-256：`E6385B2016F6CAE8D7AD4744809665FA88AE51BCD9F07BAE5794BFD43B7D160C`
- `Maple.InputBroker.dll` SHA-256：`B881328E588E182D9DA5F001D1EE285669EBB972CF6E142E3CAE6A24D6C8DCAE`
- ZIP：`artifacts/phase-1/win-x64-stationary-safety-visual-diagnostics/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`0050EFF9E769D31CD604320022E4D0ECAFF131CF035934C36C83E0DCAD078090`
- 发布目录 499 个文件、ZIP 499 个文件条目已逐项读取；测试输出、发布目录和 ZIP 内 Host/Core/Broker DLL 哈希全部一致。

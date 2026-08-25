# 视觉增强持续攻击验收记录

日期：2026-08-22

状态：自动化与 Windows x64 构建完成；真实游戏中的长时间平台边界仍需实机验收，不能由单元测试替代。

## 已完成的自动验证

- [x] 新模式 `VisualSafeContinuous` 与原 `Always` 控制器隔离，`MonsterInRange` 继续拒绝启动。
- [x] schema 2 视觉档案校验平台矩形、人物外观尺寸/纹理、固定模板库与捕获尺寸并完成原子保存与加载；schema 1 名字模板继续兼容读取。
- [x] 人物外观固定多模板及水平镜像输出最佳与二维空间次峰；首次锁定固定在保存位置局部范围，恢复固定在最后可信位置局部范围，远处相似峰不能替换本人；低分、重复帧、局部歧义和跳变撤销信任。
- [x] 本人必须连续 3 个递增帧锁定；捕获故障或预览关闭立即撤销，并要求 3 个新帧重新锁定。
- [x] 平台状态覆盖 `Safe`、`GuardLeft`、`GuardRight`、`Outside`、`Untrusted`，动态保护带只增不减且耗尽安全区时冻结。
- [x] 安全状态下移动时长继续随机；保护带只授权向内移动；不可信、越界和超过 500ms 的旧画面不发送方向键。
- [x] 视觉授权在方向键保持期间失效时立即结束保持并执行 KeyUp；攻击节奏不因视觉暂失停止。
- [x] 移动授权原子绑定最新可信观察与具体左右方向；进入单侧保护带或动态扩大保护带时立即撤销新变危险的方向，不等待下一捕获帧。
- [x] 每段移动完成稳定等待后，以当时最新序列作为屏障，再等待一张更晚的可信帧。
- [x] 第一段已执行而第二段未执行时持续保持 `FacingRestorePending`，恢复初始朝向前不开始下一轮攻击；所有视觉条件等待以不超过 100ms 的片段复查窗口安全门。
- [x] React 只提交模式、配置窗口和会话意图；结构化显示匹配分数、保护带和带符号像素偏移，不传输帧或模板像素。
- [x] 原生框选使用冻结首帧，人物框选完成后先倒计时 3 秒供用户切回游戏，再在约 6 秒内采集左右转向和施法动作，最终保存最多 8 张人物外观模板；绘制黄色平台外框、绿色安全核心和蓝色人物头部/上半身框；视口变化或实时帧不推进时保留旧配置。
- [x] 人物外观首次锁定仍要求 `0.88`；已有轨迹与短暂失信后的局部恢复阈值调整为 `0.72`，继续限制在上一可信位置各轴 `12px` 邻域并要求连续 3 个新帧恢复。
- [x] 主窗口提供带确认的“清空视觉配置”按钮；攻击或导航运行时拒绝清空，清空失败不改变原有效配置状态，也不把攻击会话误标为失败。

## 验证命令与结果

- `dotnet test MapleProduct.sln -c Release --no-restore`：通过 390，失败 0（Core 42、InputBroker 46、Host 302）。
- `npm test -- --run`：5 个文件、44 项测试全部通过。
- `npm run lint`：错误 0；存在 4 条既有 React Hook 警告，位于 `useRhythmCountdown.ts` 和 `RecognitionStatus.tsx`。
- `npm run build`：成功；仅有 Ant Design vendor chunk 大于 500kB 的构建提示。
- `dotnet build MapleProduct.sln -c Release --no-restore`：成功，警告 0，错误 0。
- `git diff --check`：退出码 0；仅输出工作区 LF/CRLF 转换提示。

## 仍需真实游戏验收

- [ ] 在原生预览依次框选完整平台活动范围和本人头部/上半身，关闭再打开后确认相同视口自动复用。
- [ ] 让其他玩家进入平台与角色附近，确认不会把相似玩家或中心最近人物升级为本人。
- [ ] 让宠物短时遮挡人物、显示技能特效和关闭预览，确认当前方向键立即释放、攻击继续，连续 3 个可信新帧后随机移动恢复。
- [ ] 分别接近左右保护带，确认只发生随机时长的向内移动；越过外框后不自动猜测回中。
- [ ] 长时间运行并保存预览截图与会话日志，确认人物未越过框定平台；记录最终像素偏移和诊断毫秒偏移范围。
- [ ] 在 Windows 10 22H2 x64 与受支持的 Windows 11 x64 环境完成 Broker、失焦和真实 `keybd_event` 响应复测。

## 发布包

- 2026-08-22 人物外观局部跟踪版 EXE：`artifacts/phase-1/win-x64-visual-character-identity/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`FE55D93FBDF672D38782BDDD1277BF3C818C6BDED6E861056A251762C739E9D0`
- Broker EXE SHA-256：`E50C718E3EE6C31B2F95D44FB3B8D1A86A43C61B05BC57DC0E6FB0200B43B370`
- 核心 `Maple.Host.dll` SHA-256：`4D37CC96FB16ADDB48C05787B2B3758BAB6466A2C619DFF62DCFE76F02434C8E`
- ZIP：`artifacts/phase-1/win-x64-visual-character-identity/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`FE8823EFC59AA668947A8A0730DA44C7CA8067747ABB581B0C228DC05CDC05D0`
- 包内 500 个条目已逐项读取，且已核验存在 `Maple.WindowsHost.exe`、`Maple.InputBroker.exe`、`Maple.Host.dll` 和 `client/index.html`；包内 `Maple.Host.dll` 与新 Release 输出哈希一致。

- 2026-08-22 朝向恢复与方向授权修正版 EXE：`artifacts/phase-1/win-x64-visual-facing-recovery/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`7B0CF76D1EC0B09AF28A68E03F98C78618C24C9A081DAA85D3275BAB8FBB38AF`
- 核心 `Maple.Host.dll` SHA-256：`F6E0268D6C6217582782137B83192FFA2F9FDD6623AA43E722FF8EA9EB8C3A3E`
- ZIP：`artifacts/phase-1/win-x64-visual-facing-recovery/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`1FB21908EF23CA3BEC70299E14D6549BB2438F16FDD93DF7B7BDF5FB2EB6072C`
- 包内 500 个条目已逐项读取，且已核验存在 `Maple.WindowsHost.exe`、`Maple.InputBroker.exe`、`Maple.Host.dll` 和 `client/index.html`。

- 2026-08-23 人物跟踪 72% 与多动作采集版 EXE：`artifacts/phase-1/win-x64-visual-character-track-72/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- Broker EXE SHA-256：`17524E01D94ED1453FF89E48CF7F5190360F3534D5E1FF85076B85C128E1D9F2`
- 核心 `Maple.Host.dll` SHA-256：`1EAA3245DF43FCDC59E5C95C2DC7E7C82B1856B389C7EFC0F5A3C5F39B7B9C90`
- ZIP：`artifacts/phase-1/win-x64-visual-character-track-72/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`0E22C149AE80E97E9856EFD165D62196799365783BB030665B510D797C75B9A5`
- 发布目录 499 个文件、ZIP 500 个条目已逐项读取；关键文件齐全，包内 `Maple.Host.dll` 与本次 Release 输出哈希一致，前端产物包含清空配置命令与独立错误处理。

## 视觉轨迹修正

- 实机日志会话 `3647a387-8a99-45da-af09-2b18144c7c5f` 的首轮实际发送了左右方向键；从第 2 轮开始持续记录 `VISUAL_NAME_AMBIGUOUS`，因此安全门冻结后续移动。
- 根因是已锁定后仍使用全局最佳与次佳分差：远处相似纹理可以撤销仍位于上一可信位置附近的本人候选。
- 修正后首次锁定门槛不变；已有轨迹时选择上一可信位置 `12px` 邻域内距离最近的高分峰，远处峰不影响轨迹，局部等距冲突仍冻结。

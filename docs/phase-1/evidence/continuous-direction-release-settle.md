# 普通持续攻击换向释放结算验收记录

日期：2026-08-24

状态：日志根因、自动化回归与 Windows x64 发布包验证完成；人物在真实游戏中的长期位移仍需实机持续运行验收。

## 日志根因

- 会话 `6b5e21c5-5b90-4dbf-94e2-b2d368621370` 以 `manual:Right` 启动并完成 202 轮。
- 左右方向各执行 202 次，没有漏发第二方向；实际物理按压累计为左 `9541ms`、右 `9588ms`，Host 最终偏移为 `+47ms`。
- 人物实机仍长期向左，说明问题不是输入始终向左，而是初始朝向反方向的第一段与恢复朝向的第二段在游戏内响应不等价。
- 第一段前已有攻击释放固定 `100ms`，第二段前原先只有随机 `40–70ms`；短间隔可能使第二段在游戏输入帧中被削弱。

## 修复行为

- 仅在普通持续攻击控制器中，为第一方向松键后增加固定 `100ms` 结算缓冲。
- 原配置随机间隔继续独立抽样并完整保留，权威 `MoveGap = 100ms + sampledMoveGapMs`。
- 当前随机间隔为 `40–70ms` 时，第二方向前总间隔仍是随机 `140–170ms`。
- 攻击时长、两段移动时长、移动间隔、休息、方向顺序、最终朝向、位移规划和 Broker 输入路径均未改成固定值；视觉增强控制器未修改。

## 红绿测试

- 修复前聚焦测试分别得到“期望 `147ms`、实际 `47ms`”和“期望 `163ms`、实际 `63ms`”，证明缺少固定结算分量。
- 修复后 `Adds_direction_release_settle_to_the_random_move_gap` 两组数据全部通过。
- `dotnet test MapleProduct.sln --no-restore`：429 项全部通过（Core 47、Host 336、InputBroker 46）。
- `pnpm test -- --run`：5 个文件、46 项全部通过。
- `pnpm build`：成功；仅有 vendor chunk 大于 500kB 的既有提示。
- `pnpm lint`：错误 0；存在 4 条既有 React Hook 警告。
- `dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore`：成功，警告 0，错误 0。

## 发布包

- EXE：`artifacts/phase-1/win-x64-continuous-direction-settle/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256：`0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- 核心 `Maple.Host.dll` SHA-256：`FC20F4ECCC09B768DA6D929D8CF293A6D338953FD6E8F8019B002D710B48DF32`
- 核心 `Maple.InputBroker.dll` SHA-256：`B881328E588E182D9DA5F001D1EE285669EBB972CF6E142E3CAE6A24D6C8DCAE`
- ZIP：`artifacts/phase-1/win-x64-continuous-direction-settle/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256：`2209A2409EC774FD39D7F81F3C8E3A0D6790C546C0383D291B3A8BB75170301B`
- 发布目录与 ZIP 各包含 499 个文件；ZIP 条目已逐项读取，包内 Host 与 Broker 核心 DLL 哈希和 Release 输出一致。

## 实机验收

- [ ] 使用朝右初始站位长时间运行，确认日志 `MoveGap` 在配置 `40–70ms` 时稳定落入 `140–170ms`。
- [ ] 对比人物最终肉眼位置与 Host 累计偏移，确认不再持续向初始朝向反方向漂移。
- [ ] 失焦停止一次，确认全部活动键释放且无遗留方向键。

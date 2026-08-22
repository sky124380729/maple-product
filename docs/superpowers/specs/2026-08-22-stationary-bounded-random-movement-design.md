# 定点攻击有界随机移动设计

日期：2026-08-22
状态：待实现
适用范围：一期定点持续攻击

## 1. 背景与根因

当前移动规划器按 Host 请求的方向键保持时长计算 `relativeOffsetMs`。实际方向键由 Host 延迟结束后经命名管道请求 Broker 释放，线程调度、管道传输和游戏帧采样会使真实按压时长与请求值不同。左右两侧的误差不会严格抵消，因此即使计划 offset 始终位于配置边界内，真实输入时间形成的误差仍会随长循环累积。

Broker 已具备测量成功物理 Down 至成功物理 Up 时间差的协议基础，但定点控制器没有把该值用于移动记账。修复必须保留已验证的 Host 主动 `KeyUp` 路径，不能让定点请求进入自动寻路的 Broker 截止调度器。

本设计约束的是程序方向键输入形成的计算偏移，不宣称在没有视觉反馈时能够直接测量游戏世界像素或平台边缘。固定 `20ms` 余量和 Windows 长时间实机验收用于覆盖正常释放抖动；超过余量的异常仍按真实结果停止并记录。

## 2. 目标与非目标

目标：

- 按 Broker 实测方向键时长逐段更新会话 offset；
- 在配置边界内保留随机移动，同时使靠近边界的随机过程逐渐回中；
- 保持每轮固定方向顺序和最终朝向；
- 在角色信息区域显示 Host 权威的当前计算偏移；
- 为越界、计时异常和无合法动作提供稳定停止路径与测试证据。

非目标：

- 不引入视觉定位、平台边缘识别或自动寻路；
- 不恢复定点方向键的 Broker 自动截止；
- 不把随机移动改成固定等长、固定差值或每轮归零；
- React 不计算 offset、不生成随机数、不发送方向键。

## 3. 状态与不变量

会话移动状态增加：

```text
relativeOffsetMs       Broker 实测时长形成的有符号累计偏移
cycleStartOffsetMs     本轮移动开始前的偏移
movementIntent         Unbiased | ReturnTowardCenter
releaseSafetyMarginMs  固定 20ms
```

向左为负，向右为正。新会话从 `0` 开始；攻击、休息和轮次切换不清零。停止消息携带最终值，下一次开始才重置。

必须始终满足：

1. 每段提交后的 `relativeOffsetMs` 位于 `[-maxLateralMoveMs,+maxLateralMoveMs]`。
2. 第一方向固定为初始朝向的反方向，第二方向固定为初始朝向。
3. 第二方向只能在第一方向真实时长提交后规划。
4. 每次向任一边界规划时为释放抖动预留 `20ms`。
5. 轮次结束 offset 必须为下一轮固定第一方向保留 `moveHoldMinMs + 20ms` 空间。
6. `ReturnTowardCenter` 轮次结束后的绝对偏移严格小于 `abs(cycleStartOffsetMs)`。
7. 两段时长分别从当前合法集合随机抽样，不复制、不固定差值、不强制归零。

## 4. 分区随机策略

以轮次开始时 `abs(relativeOffsetMs) / maxLateralMoveMs` 选择意图：

| 区域 | 范围 | 意图 |
|---|---:|---|
| 中心区 | `0%–40%` | `Unbiased` |
| 回中区 | `>40%–70%` | `75% ReturnTowardCenter`，`25% Unbiased` |
| 保护区 | `>70%–100%` | `ReturnTowardCenter` |

`Unbiased` 不偏好左、右或零点，但仍受每段边界、`20ms` 余量和下一轮可行性约束。`ReturnTowardCenter` 只缩小合法时长集合，不指定固定落点。第一段抽样时必须保留至少一个可能完成本轮意图的第二段范围；第一段真实完成后重新计算第二段合法集合。若调度抖动使原本可行的第二段不再存在，则安全停止，不能使用计划值伪造可行性。

本策略允许连续多轮逐渐回中，不要求单轮回到中心区。相同 offset 下仍可能产生不同的第一段、间隔、第二段、稳定等待和最终 offset。

## 5. Broker 与控制器数据流

```text
Planner 根据真实 offset 和轮次意图抽样第一段
-> Host 发送定点 HostKeyUp 模式 KeyDown(leaseMs=requestedHoldMs)
-> Host 等待 requestedHoldMs
-> Host 显式发送 KeyUp
-> Broker 返回 actualHoldMs / releaseLatenessMs
-> Controller 校验并提交第一段真实 offset
-> 发布带新 relativeOffsetMs 的 MoveGap 状态
-> Planner 根据新 offset 抽样第二段
-> 重复实测、校验和提交
-> 发布带最终 relativeOffsetMs 的 Stabilizing 状态
```

Broker 计时起点为成功物理 Down 返回后，终点为成功物理 Up 返回后。`actualHoldMs` 必须位于 `1–5,000ms`，与 Broker 方向动作硬上限一致；`releaseLatenessMs` 必须非负。计时缺失或非法使用 `MOVEMENT_TIMING_INVALID` 停止；提交后越界使用 `MOVEMENT_OFFSET_EXCEEDED` 停止。无合法第一方向使用 `INITIAL_FACING_BUDGET_EXHAUSTED`，无合法第二方向使用 `MOVEMENT_BUDGET_EXHAUSTED`。所有停止路径最终执行 `ReleaseAll`。

释放迟到超过 `20ms` 不单独覆盖真实 offset 判断：控制器先按完整真实时长记账，仍在边界内则允许继续并记录诊断，越界则停止。这样不会通过截断测量值掩盖已经发生的输入。

## 6. UI 与消息契约

`StationaryRhythmState` 增加：

```json
{
  "relativeOffsetMs": -23
}
```

Host 在新会话首个状态中发布 `0`；第一段释放后随 `MoveGap` 发布第一段后的值，第二段释放后随 `Stabilizing` 发布轮次最终值，`Stopped` 发布会话最终值。

React 在现有角色信息区域显示：

- 负数：`-23 ms（左）`；
- 正数：`+17 ms（右）`；
- 零：`0 ms（中心）`；
- 尚无会话数据：`-`。

该字段不依赖识别开关。停止 reducer 清除倒计时活动状态，但保留最终 offset；新会话进入启动状态时清除旧值，收到首个运行状态后显示 `0 ms（中心）`。React 不根据该值产生任何动作。

## 7. 日志与异常

每段移动的结构化日志至少包含：

```text
sessionId, cycleId, direction, intent,
requestedHoldMs, actualHoldMs, releaseLatenessMs,
offsetBeforeMs, offsetAfterMs, maxLateralMoveMs
```

配置在运行中热更新时，从下一完整周期使用新的阈值和移动范围。如果新阈值小于当前真实绝对 offset，下一周期开始前以 `MOVEMENT_OFFSET_EXCEEDED` 停止，不尝试用动作掩盖非法状态。

取消、失焦、Broker 错误和释放失败沿用现有安全停止规则。任何异常都不能把 offset 重置为零；最终状态和日志保留最后一个已成功提交的真实值。

## 8. 测试与验收

纯逻辑测试：

- 第一段请求 `40ms`、Broker 返回 `46ms` 时按 `46ms` 更新，第二段预算基于新值；
- 每段预留 `20ms`，轮次结束为下一轮第一方向保留最小按压与余量；
- 三个分区按指定概率源选择意图，回中轮次严格减少绝对偏移；
- 固定随机序列产生不同的合法两段时长和非零最终 offset；
- 长周期属性测试注入不超过 `20ms` 的释放迟到，任何已执行段都不越过配置边界；
- 缺失、零值、负值、过大实测时长和真实越界均以稳定错误码停止并 `ReleaseAll`。

Broker/Host 测试：

- 定点 `HostKeyUp` 请求不登记导航截止调度，显式 Up 返回实测时长；
- 第一段真实结果在第二段抽样前提交；
- 请求、实测、迟到和 offset 前后值进入日志；
- 停止状态携带最终 offset，新会话重置为零。

React 测试：

- 正、负、零和无数据格式正确；
- 识别关闭时仍显示；
- 第一段、第二段状态依次更新显示；
- 停止保留最终值，重新开始清除旧值。

Windows 实机验收：

- 使用安全平直平台、无怪物击退和无技能位移条件长时间运行；
- 保存每段计划/实测/迟到/offset 日志并核对人物没有持续单向漂移；
- 分别覆盖默认阈值、接近左右边界、失焦停止和手动停止；
- 实机结果只能证明经过测试的系统和游戏环境，不能用单元测试替代。

## 9. 备选方案与取舍

仅按真实时长记账的改动较小，但随机过程可能长期停留在边界附近，因此不足以解决长循环风险。固定等长回正容易实现，却会明显呈现机械节奏并违反随机要求。视觉位置闭环能够直接观察物理位置，但属于更高阶段能力，会引入识别新鲜度和置信度依赖。本设计采用实测时间闭环加分区随机回中，在一期边界内获得更好的长期稳定性，同时保留随机行为。

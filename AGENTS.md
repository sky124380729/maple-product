# Maple Product 开发规则

## 唯一事实来源

- 产品行为以 `docs/PRODUCT_SPEC.md` 为准。
- 一期实现细节以 `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md` 为准。
- 验收以 `docs/PHASE_1_ACCEPTANCE.md` 为准。
- 旧仓库只读参考，来源和禁止迁移项以 `docs/LEGACY_REFERENCE.md` 为准。
- 二期只允许按 `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md` 预留接口。

## 产品边界

- 成品只支持 Windows x64；macOS 仅用于开发、静态检查和平台无关单元测试。
- 生产输入唯一使用 `broker + keybd_event`。不得引入 Virtual HID、SendInput、PostMessage 或 React 原始按键路径。
- React 只提交配置和会话意图，不生成随机数、不发送按键、不承载逐帧图像。
- 主窗口是简单配置窗口；实时预览是可选的独立原生窗口。
- 未经文档明确授权，不得把旧分支整包合并或复制进本仓库。

## 变更纪律

- 永远只在 `master` 分支开发、提交和推送。AI 不得自行创建、切换或使用其他开发分支。
- 先更新规格和验收，再写实现。
- 每个行为必须有状态、边界、异常路径和测试证据。
- 发现聊天内容与文档不一致时，以已确认并提交的文档为准；不能自行猜测。

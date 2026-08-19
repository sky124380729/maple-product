# Maple Product

Windows x64 自动攻击程序。当前文档阶段先建立成品规格，代码实现从零开始。

阅读顺序：

1. `docs/PRODUCT_SPEC.md`
2. `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
3. `docs/PHASE_1_ACCEPTANCE.md`
4. `docs/LEGACY_REFERENCE.md`
5. `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md`

旧 Maple 仓库只作为只读技术参考，不是本仓库的代码基线。

## 开发工具链

- .NET SDK 8.0.424（由 `global.json` 锁定）
- Node.js 22 + npm 10
- macOS 只运行平台无关测试和前端构建
- 最终产品仅发布 Windows 10 22H2 x64 或 Windows 11 x64

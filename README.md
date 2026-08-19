# Maple Product

Maple Product 是面向已授权游戏测试场景的 Windows x64 定点持续攻击工具。生产输入链路固定为普通权限 Host、管理员 Input Broker、当前用户命名管道和 `keybd_event`。

阅读顺序：

1. `docs/PRODUCT_SPEC.md`
2. `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
3. `docs/PHASE_1_ACCEPTANCE.md`
4. `docs/LEGACY_REFERENCE.md`
5. `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md`

旧 Maple 仓库只作为只读技术参考，不是本仓库的代码基线。

## 运行

1. 在 Windows 10 22H2 x64 或 Windows 11 x64 构建发布包：`powershell -File scripts/publish-windows.ps1`。
2. 从 `artifacts/phase-1/win-x64/MapleProduct/` 启动 `Maple.WindowsHost.exe`。
3. 先启动唯一的“冒险岛怀旧服”客户端，再点击开始；Host 会按精确窗口指纹自动发现并绑定客户端，无需选择游戏路径。
4. 持续攻击模式可用；“识别怪物后攻击”一期显示但保持 disabled。

配置保存在 `%LOCALAPPDATA%\MapleProduct\stationary.json`，结构化会话日志位于同目录的 `sessions.jsonl`。实时预览是独立原生窗口，不向 React 传输逐帧图像。

## 验证

- .NET：`dotnet test MapleProduct.sln -c Release`
- React：`npm --prefix client test -- --run` 与 `npm --prefix client run build`
- Windows 发布：`powershell -File scripts/publish-windows.ps1`
- Windows 实机验收记录：[windows-real-input.md](docs/phase-1/evidence/windows-real-input.md)

自动化测试和 win-x64 构建不能证明 `keybd_event` 对真实游戏有效；必须记录 Windows 实机画面响应。

## 开发工具链

- .NET SDK 8.0.424（由 `global.json` 锁定）
- Node.js 22 + npm 10
- macOS 只运行平台无关测试和前端构建
- 最终产品仅发布 Windows 10 22H2 x64 或 Windows 11 x64

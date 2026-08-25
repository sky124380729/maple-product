# 通用地图包自动寻路证据

日期：2026-08-22

## 覆盖范围

- 用户选择任意地图包目录，Host 重新扫描并只允许启动当前目录中的合法 `.mapzip`。
- 固定小地图 ROI 校验、DPI 投影、黄色角色标记定位和当前平台判定。
- 平台/梯子图路径规划、最久未访问平台巡逻、上下梯短动作。
- 包内怪物模板匹配、玩家排除、同平台目标授权、接近和攻击。
- Broker 独占输入、安全门、观察新鲜度、地图失配和异常释放。

## 自动化结果

在 Windows x64 Release 配置执行：

```text
dotnet test MapleProduct.sln -c Release
Maple.Core.Tests:        31/31
Maple.InputBroker.Tests: 46/46
Maple.Host.Tests:       188/188

npm test -- --run
React: 31/31

dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release
0 warnings, 0 errors
```

## 开源包与真实帧

专项集成测试使用开源 `saved_maps` 目录、`沼泽地3(30-45级).mapzip`、真实 Windows Graphics Capture 保存的 `2049×1152` BGRA 帧和对应包内怪物模板：

```text
OpenSourceMapCatalogIntegrationTests.Loads_configured_open_source_package_directory: passed
OpenSourceMapCatalogIntegrationTests.Localizes_configured_real_map_frame: passed
OpenSourceMapCatalogIntegrationTests.Matches_configured_package_monster_in_real_map_frame: passed
SwampNavigationIntegrationTests.Traverses_all_platforms_attacks_authorized_monster_and_resumes_patrol: passed
```

目录集成测试验证 42 个地图包可扫描，并保留文件名不一致包的禁止运行状态。真实帧测试验证地图签名、DPI 投影、角色平台定位和怪物模板候选；闭环测试从平台 3 出发覆盖 7 个平台，包含上梯、下梯、一次授权攻击、攻击后恢复巡逻以及最终 `ReleaseAll`。

2026-08-22 负向实机验收：客户端实际处于“沼泽地2”时启动“沼泽地3”地图包，小地图名称 OCR 连续两帧校验后以 `MAP_NAME_MISMATCH` 停止，动作保持为空，Broker 正常退出，未发送导航输入。

## 尚待实机验收

真实客户端必须重新登录并进入沼泽地3后，完整验证多平台巡逻、上下梯、接近攻击和停止时释放输入。当前证据不把离线帧或模拟闭环计作该项通过。

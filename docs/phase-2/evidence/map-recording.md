# 地图录制阶段证据

## 自动化证据

- `MapFrameGeometryDetectorTests`、`MinimapGeometryDetectorTests` 和 `EnvironmentGeometryClassifierTests`：覆盖棕色背景过滤、小地图内容区、全局平台/梯子、角色标记，以及本地 `environment` 框分类。
- `MapRecorderTests`：覆盖 5 FPS 采样、跨帧稳定、样本/大小/条目上限、角色邻近梯子和落脚平台佐证、连续纵向轨迹、无关梯子隔离、悬空梯子过滤、单行 JSONL 分块、失败导出清理，以及质量结论往返。
- 全量 .NET：Core 31/31、Host 132/132、InputBroker 26/26 通过；React 28/28 通过。
- WindowsHost Release：0 警告、0 错误。
- 真实客户端离线帧：面板边框约束后小地图仍提取 8 个平台、2 个梯子候选、角色坐标约 `(0.50, 0.80)`；重复静态帧因没有纵向移动轨迹返回 `CONNECTIVITY_MISSING`，包仍能重新加载，未误标为可用于规划。

## 当前边界

录制器已接入独立实时预览窗口。点击“开始录制地图”后，预览只保存归一化几何候选和观测时间线，不读取键盘、不连接 Broker、不发送输入。结束录制会在 `%LOCALAPPDATA%/MapleProduct/map-recordings` 生成包。

全局平台以小地图为坐标来源；小地图内容区没有可验证边框时直接放弃该帧，避免深色场景改变坐标。梯子/绳索连接必须由新鲜的识别 Self、角色附近的本地梯子框、落脚平台和连续小地图纵向轨迹共同佐证。导出结果只有在平台覆盖、连接和角色轨迹通过质量门后才标记 `planningReady`，该结论会随地图包重新加载。本记录器不会自动开启寻路；地图匹配、怪物模板绑定和闭环移动属于后续阶段。

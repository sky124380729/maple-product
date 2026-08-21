# 地图录制阶段证据

## 自动化证据

- `MapFrameGeometryDetectorTests`：3/3 通过，覆盖平台、梯子和短噪声过滤。
- `MapRecorderTests`：3/3 通过，覆盖 5 FPS 采样、跨帧稳定、样本上限和可重新加载的 `.mapzip`。
- WindowsHost Release：0 警告、0 错误。

## 当前边界

录制器已接入独立实时预览窗口。点击“开始录制地图”后，预览只保存归一化几何候选和观测时间线，不读取键盘、不连接 Broker、不发送输入。结束录制会在 `%LOCALAPPDATA%/MapleProduct/map-recordings` 生成包。

当前导出的平台/梯子是视觉启发式候选，仍需在目标地图走图后检查候选数量和连接关系；本记录器不会自动开启寻路。地图匹配、怪物模板绑定和闭环移动属于后续阶段。

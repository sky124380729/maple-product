# 地图包观察接口实施计划

## 1. 先写失败测试

- 在 `Maple.Host.Tests/Navigation/MapPackageLoaderTests.cs` 用内存 zip 构造合法包。
- 覆盖完整 DTO 映射、稳定错误代码、路径穿越、重复/悬空引用、归档大小限制和快照不可变性。

## 2. 实现领域 DTO 与加载器

- 在 `Maple.Host/Navigation` 增加不可变地图 DTO、manifest/map JSON 私有反序列化模型和 `MapPackageLoader`。
- 使用 `ZipArchive` 逐条检查路径、数量和解压预算；限制 JSON 深度和文本大小。
- 所有失败映射为 `MapPackageLoadException` 与 `MAP_PACKAGE_INVALID:*` 代码。

## 3. 验证与交付

- 运行 Host、Core、InputBroker 全套 .NET 测试和 React 测试/构建。
- 运行 WindowsHost Release 构建。
- 更新验收文档，说明本阶段只读加载完成，自动寻路仍保持禁用。

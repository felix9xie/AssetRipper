# APK GUID 引用修复总结

## 问题描述

在解包 APK 文件时，ScriptableObject 中的 prefab 引用会丢失。具体表现为：
- ScriptableObject 脚本中的字段引用了其他资源（如 prefab）
- 引用使用 GUID 格式：`m_AssetGUID: 30b6e6ebf780b304f83e144c61a2e054`
- 解包后无法在资源中找到该 GUID 对应的资源

## 根本原因

AssetRipper 的依赖解析机制存在缺陷：

1. **FileIdentifier 使用 GUID 标识**：当 `AssetType` 为 `Meta` 时，`FileIdentifier.GetFilePath()` 返回 GUID 字符串
2. **AssetCollection 只有文件名**：AssetCollection 的 `Name` 属性设置为文件名（如 `CAB-xxx`），而不是 GUID
3. **解析只匹配名称**：`Bundle.ResolveCollection()` 只通过名称匹配，无法匹配 GUID 引用

在 APK 中，特别是包含 Split AssetBundles 和 CAB 文件时，依赖关系通常使用 GUID 而不是文件名来引用。

## 修复方案

本次修复在三个层面解决了问题：

### 1. AssetCollection 添加 GUID 属性

**文件**: `AssetRipper/Source/AssetRipper.Assets/Collections/AssetCollection.cs`

添加了 `Guid` 属性用于存储集合的 GUID：

```csharp
/// <summary>
/// The GUID of this collection, used for resolving dependencies in AssetBundles and APK files.
/// </summary>
/// <remarks>
/// This is particularly important for APK files where dependencies are referenced by GUID instead of file name.
/// </remarks>
public UnityGuid Guid { get; protected set; }
```

### 2. 从文件名解析并存储 GUID

**文件**: `AssetRipper/Source/AssetRipper.Assets/Collections/SerializedAssetCollection.cs`

在 `FromSerializedFile()` 方法中添加了 GUID 解析逻辑：
- 尝试从文件名直接解析 GUID（32位十六进制字符串）
- 处理 `CAB-<guid>` 格式的文件名
- 在文件名中查找 32 字符的十六进制 GUID 模式

关键改动：
```csharp
Guid = TryParseGuidFromFileName(file.NameFixed),
```

支持的文件名格式：
- `30b6e6ebf780b304f83e144c61a2e054` （纯 GUID）
- `CAB-30b6e6ebf780b304f83e144c61a2e054` （CAB 前缀）
- 任何包含 32 字符十六进制字符串的文件名

### 3. 添加基于 GUID 的依赖解析

**文件**: `AssetRipper/Source/AssetRipper.Assets/Bundles/Bundle.cs`

修改了 `ResolveCollection(FileIdentifier identifier)` 方法：

1. **首先尝试名称匹配**：保持向后兼容，先尝试原有的基于文件名的解析
2. **回退到 GUID 匹配**：如果名称匹配失败且 `FileIdentifier` 包含 GUID（`AssetType.Meta`），则使用 GUID 匹配
3. **新增 `ResolveCollectionByGuid()` 方法**：在整个 Bundle 层次结构中通过 GUID 查找 AssetCollection

关键逻辑：
```csharp
// 首先尝试名称解析
AssetCollection? result = ResolveCollection(identifier.GetFilePath());
if (result is not null)
{
    return result;
}

// 如果失败且有 GUID，尝试 GUID 解析
if (identifier.Type == AssetType.Meta && !identifier.Guid.IsZero)
{
    return ResolveCollectionByGuid(identifier.Guid);
}
```

## 修改的文件

1. `AssetRipper/Source/AssetRipper.Assets/Collections/AssetCollection.cs`
   - 添加 `Guid` 属性
   - 添加 `using AssetRipper.Primitives;`

2. `AssetRipper/Source/AssetRipper.Assets/Collections/SerializedAssetCollection.cs`
   - 添加 `using AssetRipper.Primitives;`
   - 在 `FromSerializedFile()` 中设置 `Guid`
   - 添加 `TryParseGuidFromFileName()` 方法
   - 添加 `IsHexString()` 辅助方法
   - 添加 `ParseGuid()` 辅助方法

3. `AssetRipper/Source/AssetRipper.Assets/Bundles/Bundle.cs`
   - 添加 `using AssetRipper.Primitives;`
   - 修改 `ResolveCollection(FileIdentifier identifier)` 方法
   - 添加 `ResolveCollectionByGuid()` 方法

## 测试步骤

### 1. 编译项目

```bash
cd AssetRipper
dotnet build
```

### 2. 解包 APK 文件

使用修复后的 AssetRipper 解包你的 APK 文件：
- 确保所有 CAB 文件都被提取
- 如果有 OBB 文件，一起加载
- 检查日志中是否有 "Dependency '...' wasn't found" 警告

### 3. 验证修复

检查 ScriptableObject 资源：
1. 找到包含 `m_AssetGUID: 30b6e6ebf780b304f83e144c61a2e054` 引用的 ScriptableObject
2. 验证引用的 prefab 是否正确解析
3. 检查导出的 YAML 文件中引用是否完整

### 4. 查看日志

在 AssetRipper 日志中应该能看到：
- 成功加载的 CAB 文件及其 GUID
- 依赖解析的详细信息
- 如果仍有缺失的依赖，会有明确的警告

## 预期结果

修复后应该：
- ✅ ScriptableObject 中的 GUID 引用能正确解析到对应的资源
- ✅ 导出的 YAML 文件包含完整的引用信息
- ✅ 减少或消除 "Dependency not found" 警告
- ✅ 向后兼容：对于使用文件名的正常依赖解析仍然正常工作

## 可能的问题和解决方案

### 问题 1：GUID 仍然无法解析

**可能原因**：
- 对应的文件没有被加载
- 文件名中不包含 GUID
- GUID 格式不标准

**解决方案**：
- 确保所有相关文件（CAB、Split APK 等）都被加载
- 检查文件名格式
- 在日志中查找具体的警告信息

### 问题 2：某些引用仍然丢失

**可能原因**：
- 引用的资源在不同的 Bundle 中
- 文件加载顺序问题

**解决方案**：
- 确保所有相关的 Bundle 和 OBB 文件都被加载
- 检查依赖链是否完整

## 技术细节

### UnityGuid 格式

Unity GUID 由 4 个 uint32 值组成，共 128 位。在文件名中通常表示为 32 个十六进制字符（不带连字符）：
- 标准格式：`30b6e6ebf780b304f83e144c61a2e054`
- 解析为：`0x30b6e6eb, 0xf780b304, 0xf83e144c, 0x61a2e054`

### AssetType.Meta

当 `FileIdentifier.Type == AssetType.Meta` 时，表示这是一个元数据引用，通常用于：
- 导入的资源（Imported Assets）
- AssetBundle 中的资源
- 跨 Bundle 的引用

在这种情况下，`FileIdentifier.GetFilePath()` 返回 GUID 字符串而不是文件路径。

## 贡献者

本次修复解决了 AssetRipper 在处理 APK 文件时的一个重要问题，特别是对于使用 Split AssetBundles 的现代 Unity 游戏。

## 相关 Issue

本修复解决的问题与以下类似：
- ScriptableObject 引用丢失
- APK 依赖解析失败
- CAB 文件引用无法解析

---

**修复日期**: 2025-11-04
**AssetRipper 版本**: Latest (master branch)


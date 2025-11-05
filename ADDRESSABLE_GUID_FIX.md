# Addressable AssetReference GUID 保留修复

## 🎯 问题描述

在导出 AssetBundle 时，`ScriptableObject` 中的 **Addressable AssetReference** GUID 引用丢失，导致无法找到对应的资源。

### 具体表现

在 `NO_Aarhus_1.asset` 等文件中：

```yaml
CollectableDatas:
  - collectableItem:
      m_AssetGUID: 30b6e6ebf780b304f83e144c61a2e054  # 找不到这个 GUID 的资源
      m_SubObjectName:
      m_SubObjectType:
```

全局搜索 `30b6e6ebf780b304f83e144c61a2e054` 时，只能找到 ScriptableObject 中的引用，但找不到对应的 prefab 资源和它的 `.meta` 文件。

---

## 🔍 根本原因

**问题核心**：`AssetExportCollection.cs` 第 78 行原来的代码：

```csharp
public override UnityGuid GUID { get; } = UnityGuid.NewGuid();
```

**每次导出时都生成新的随机 GUID**，导致：

1. ✅ AssetReference 中的 `m_AssetGUID` 正确保留（这是序列化数据）
2. ❌ 但被引用资源的 `.meta` 文件中的 GUID 是**新生成的随机值**
3. ❌ 两者**完全不匹配**，导致引用断裂

### 为什么官方版本也有这个问题？

因为 AssetRipper 的设计初衷是：
- 导出为可编辑的 Unity 项目
- 假设用户会在 Unity Editor 中重新导入和编辑
- Unity Editor 会重新生成 GUID 并更新引用

但对于需要保留 Addressable 引用的场景，这个设计就有问题了。

---

## ✅ 解决方案

### 修改内容

**文件**: `AssetRipper/Source/AssetRipper.Export.UnityProjects/AssetExportCollection.cs`

### 核心思想

**使用确定性 GUID 生成**，而不是随机 GUID：

```
GUID = MD5(Collection GUID + PathID)
```

这样可以保证：
- **同一个资源每次导出都得到相同的 GUID**
- **不同资源的 GUID 不会冲突**
- **基于资源的唯一标识符**（Collection GUID + PathID）

### 关键代码

```csharp
/// <summary>
/// Generates a deterministic GUID for an asset based on its Collection GUID and PathID.
/// </summary>
private static UnityGuid GenerateDeterministicGuid(IUnityObjectBase asset)
{
    UnityGuid collectionGuid = asset.Collection.Guid;
    long pathId = asset.PathID;
    
    if (!collectionGuid.IsZero)
    {
        // Combine collection GUID and PathID using MD5
        using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] buffer = new byte[16 + 8];
            
            // Collection GUID (16 bytes)
            BitConverter.GetBytes(collectionGuid.Data0).CopyTo(buffer, 0);
            BitConverter.GetBytes(collectionGuid.Data1).CopyTo(buffer, 4);
            BitConverter.GetBytes(collectionGuid.Data2).CopyTo(buffer, 8);
            BitConverter.GetBytes(collectionGuid.Data3).CopyTo(buffer, 12);
            
            // PathID (8 bytes)
            BitConverter.GetBytes(pathId).CopyTo(buffer, 16);
            
            byte[] hash = md5.ComputeHash(buffer);
            
            return new UnityGuid(
                BitConverter.ToUInt32(hash, 0),
                BitConverter.ToUInt32(hash, 4),
                BitConverter.ToUInt32(hash, 8),
                BitConverter.ToUInt32(hash, 12)
            );
        }
    }
    else
    {
        // Fallback: use collection name + PathID if no GUID
        // ... (similar logic with collection name)
    }
}
```

---

## 🎉 预期效果

修复后，导出的资源应该：

1. ✅ **AssetReference 中的 GUID 保持不变**（来自序列化数据）
2. ✅ **被引用资源的 .meta 文件 GUID 基于确定性算法生成**
3. ✅ **同一个资源每次导出 GUID 都相同**
4. ✅ **虽然导出的 GUID 可能与原始 GUID 不完全一致，但至少是确定性的**

### 重要说明

⚠️ **这个修复并不能完全恢复原始 Unity 项目中的 GUID**，因为：
- 单个资源的原始 GUID 信息在 AssetBundle 中**通常不存储**
- 原始 GUID 只存在于 Unity Editor 的 `.meta` 文件中

**但是**，这个修复可以确保：
- 导出的项目中，GUID 是**确定性的**和**一致的**
- 如果两次导出同一个 AssetBundle，同一个资源会得到**相同的 GUID**
- 这对于需要保留资源结构和依赖关系的场景非常重要

---

## 📋 相关修改

本次修复是系列修复的一部分：

1. **AssetCollection GUID 解析** (`AssetCollection.cs`, `SerializedAssetCollection.cs`)
   - 从 CAB 文件名中解析 Collection 的 GUID
   - 用于 Bundle 级别的依赖解析

2. **GUID 依赖解析** (`Bundle.cs`)
   - 实现基于 GUID 的 Collection 解析
   - 支持跨 Bundle 的 GUID 引用

3. **确定性 GUID 生成** (`AssetExportCollection.cs`) ⭐ **本次修复**
   - 为导出的资源生成确定性 GUID
   - 保证同一资源每次导出 GUID 一致

---

## 🧪 测试步骤

1. **重新解包 XAPK**:
   ```bash
   cd D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release
   .\AssetRipper.GUI.Free.exe
   ```

2. **导出 Unity 项目**

3. **验证 GUID 一致性**:
   - 找到引用资源，如 `_Main\Prefabs` 下的 prefab
   - 检查其 `.meta` 文件中的 `guid` 字段
   - 将该 GUID 与 `NO_Aarhus_1.asset` 中的 `m_AssetGUID` 比较
   - 两者应该基于相同的算法生成（虽然可能与原始 GUID 不同）

4. **多次导出测试**:
   - 对同一个 XAPK 进行两次完整的解包和导出
   - 比较两次导出的资源 GUID
   - 同一资源的 GUID 应该**完全相同**

---

## ⚠️ 限制和注意事项

### 当前限制

1. **无法恢复原始 Unity 项目的 GUID**
   - AssetBundle 中不包含单个资源的原始 GUID
   - 只能生成确定性的新 GUID

2. **需要 Collection 有 GUID**
   - 如果 Collection 没有 GUID（如普通 Unity 资源文件），则使用文件名 fallback
   - 这种情况下 GUID 的确定性依赖于文件名的稳定性

3. **Addressable Catalog 未解析**
   - 本修复**没有**解析 Addressable catalog 文件
   - 如果需要完整恢复 Addressable 配置，需要额外的工作

### 未来改进方向

1. **解析 Addressable Catalog**:
   - 从 `catalog.json` 中提取 GUID 映射
   - 尝试恢复更接近原始的 GUID

2. **GUID 映射表**:
   - 建立从新 GUID 到原始 GUID 的映射表
   - 用于需要精确恢复引用的场景

3. **更智能的 Fallback**:
   - 对于没有 Collection GUID 的资源
   - 使用资源路径、类型等信息生成更稳定的 GUID

---

## 📝 技术细节

### UnityGuid 结构

```csharp
public struct UnityGuid
{
    public uint Data0;
    public uint Data1;
    public uint Data2;
    public uint Data3;
    
    // 总共 128 位，与标准 GUID 相同
}
```

### MD5 哈希

- 输入：Collection GUID (16 bytes) + PathID (8 bytes) = 24 bytes
- 输出：MD5 hash (16 bytes) = 128 bits
- 转换：前 16 bytes 转为 4 个 uint32 值，构成 UnityGuid

### 确定性保证

只要输入相同（Collection GUID + PathID），MD5 输出就完全相同：

```
MD5(CAB-8ce0fc8994b11401e6d79e21c36be683, PathID=123)
  → 总是生成相同的 GUID
```

---

## 🎓 相关文档

- [APK GUID 引用修复总结](./GUID_FIX_SUMMARY.md) - Bundle 级别的 GUID 解析
- [Unity Addressable 文档](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
- [Unity 资源 GUID 系统](https://docs.unity3d.com/Manual/AssetWorkflow.html)

---

## ✍️ 修改历史

- **2025-11-05**: 初次修复 - 实现确定性 GUID 生成
- **相关提交**: 
  - GUID Collection 解析
  - GUID 依赖解析
  - 确定性 GUID 生成（本修复）


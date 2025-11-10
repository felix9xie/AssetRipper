# 快速开始：修复Addressables GUID

## 🚀 一键解决方案

### 步骤1：重新导出游戏

使用修改后的AssetRipper重新导出游戏：

```bash
# 方式1：使用GUI（推荐）
"D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\AssetRipper.GUI.Free.exe"

# 方式2：命令行
cd "D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release"
AssetRipper.GUI.Free.exe "D:\Work\UnPacker\Triple Match City\Triple Match City_2.9.0_APKPure" -o "D:\Output\Fixed_Export"
```

### 步骤2：查看日志确认提取成功

在导出过程中，你会看到：

```
[Processing] === Bundle GUID Extraction Started ===
[Processing] Indexed 50000 assets
[Processing] Found 15000 unique GroupIds
[Processing] Scanned 3624 leveldata files
[Processing] Extracted 12000 AssetReference GUIDs
[Processing] Built 8000 GUID mappings  ← 关键指标！
[Processing] === Bundle GUID Extraction Completed ===
```

### 步骤3：在Unity中验证

1. 用Unity打开导出的项目
2. 打开 `Assets/Resources/leveldata/Tutorial_0.asset`
3. 检查 `CollectableDatas` 中的引用是否正常显示

**修复前**：
- collectableItem: `None (Addressable Asset)` ❌

**修复后**：
- collectableItem: `fisherman_0 (GameObject)` ✅

## 🔍 故障排查

### 问题1：GUID映射数量为0

```
[Processing] Built 0 GUID mappings  ← 有问题！
```

**可能原因**：
- leveldata文件不在预期位置
- AssetReference字段名称不同（不是`m_AssetGUID`）
- 资源名称格式不匹配

**解决方案**：
检查leveldata文件的位置和格式：
```bash
find "导出目录/Assets" -name "*.asset" -path "*/leveldata/*" | head -5
```

### 问题2：部分资源仍然找不到

**正常现象**：
- 不是所有资源都会在leveldata中被引用
- 未被引用的资源会使用确定性GUID（基于路径）
- 这不影响这些资源的正常使用

**检查方法**：
```csharp
// 在导出后检查特定资源
grep "fisherman_0" ExportedProject/.../fisherman_0.prefab.meta
```

### 问题3：引用仍然显示为None

**检查清单**：
1. ✅ GUID是否匹配？
   - 对比 `Tutorial_0.asset` 中的 `m_AssetGUID`
   - 和 `fisherman_0.prefab.meta` 中的 `guid`

2. ✅ 资源是否已添加到Addressables？
   - 使用我们的 `AddReferencedAssetsToAddressables.cs` 脚本
   - 或手动添加到Addressables组

3. ✅ Addressables地址是否正确？
   - 默认使用文件名（不含扩展名）
   - 如 `fisherman_0`

## 📊 预期结果

### Tutorial_0关卡的示例

**修复前的状态**：
```yaml
# Tutorial_0.asset
Stages:
  - GroupId: fisherman_0  # ✓ 正常
  - GroupId: tent_0       # ✓ 正常
  - GroupId: boat_0       # ✓ 正常

CollectableDatas:
  - collectableItem:
      m_AssetGUID: 662b44a898afe7840a044dcf6bfc8120  # ✗ 找不到
```

**Unity Inspector显示**: `None (Addressable Asset)` ❌

---

**修复后的状态**：
```yaml
# Tutorial_0.asset (保持不变)
CollectableDatas:
  - collectableItem:
      m_AssetGUID: 662b44a898afe7840a044dcf6bfc8120

# fisherman_0.prefab.meta (新GUID)
fileFormatVersion: 2
guid: 662b44a898afe7840a044dcf6bfc8120  # ✓ 匹配！
```

**Unity Inspector显示**: `fisherman_0 (GameObject)` ✅

## 🎯 成功标志

你会知道修复成功，当：

1. **日志显示正确的统计**
   ```
   Built 8000+ GUID mappings  ← 应该有数千个
   ```

2. **Unity中引用正常显示**
   - 不再是 "None"
   - 显示正确的资源名称和类型

3. **游戏可以正常运行**
   - Addressables能加载资源
   - 没有"Missing Reference"错误

## 💡 提示

1. **第一次导出会较慢**
   - 需要扫描所有资源并建立索引
   - 3000+关卡的项目可能需要5-10分钟

2. **检查关键关卡**
   - 优先验证Tutorial关卡
   - 确认主要prefab的引用正确

3. **保存映射表**
   - 可以导出`bundle_guid_mappings.txt`用于调试
   - 包含完整的GUID映射关系

## 🆘 需要帮助？

如果遇到问题，请提供：
1. AssetRipper的完整日志
2. 具体哪个资源的引用失败
3. 该资源的.meta文件内容
4. leveldata中的对应引用

---

**恭喜！你现在拥有一个能够保留Addressables引用的AssetRipper！** 🎉


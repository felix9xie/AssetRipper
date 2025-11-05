# Triple Match City 解包问题分析和解决方案

## 当前状态分析

根据日志信息：

### ✅ 成功的部分
- ✅ 程序正常启动
- ✅ XAPK 文件成功解压
- ✅ 文件结构识别为 "Mixed game structure"
- ✅ 资产处理流程完成

### ⚠️ 关键警告
```
Import : Files use the 'Unknown' scripting backend.
```

**这是主要问题！** 意味着 AssetRipper 没有找到游戏的脚本程序集（DLL 文件）。

## 为什么资源没有完全解包？

### 1. **缺少脚本程序集（主要原因）**

当 scripting backend 显示为 "Unknown" 时，会导致：
- ❌ MonoBehaviour 脚本无法正确反序列化
- ❌ ScriptableObject 可能无法正确解析
- ❌ 自定义组件的数据丢失
- ❌ 某些资源引用可能断裂

**解决方案：**
需要提取游戏的 DLL 文件：

#### 对于 IL2CPP 游戏：
```
1. 检查 xapk/apk 中是否有：
   - lib/arm64-v8a/libil2cpp.so
   - assets/bin/Data/Managed/Metadata/global-metadata.dat

2. 使用 Il2CppDumper 或 Cpp2IL 生成 DLL：
   https://github.com/Perfare/Il2CppDumper
   或
   https://github.com/SamboyCoding/Cpp2IL

3. 将生成的 DLL 文件夹与 xapk 一起导入 AssetRipper
```

#### 对于 Mono 游戏：
```
1. 检查是否有：
   - assets/bin/Data/Managed/*.dll

2. 确保这些 DLL 文件被正确提取
3. 将 DLL 文件夹与 xapk 一起导入
```

### 2. **配置设置可能需要调整**

当前配置：
```
ScriptContentLevel: Level2          ← 可以尝试 Level1 或 Level0
ScriptExportMode: Hybrid            ← 尝试 Decompiled
ExportUnreadableAssets: False       ← 改为 True 可能会导出更多
```

**建议的配置更改：**

1. **启用导出不可读资源**：
   - `ExportUnreadableAssets: True`
   - 这会尝试导出即使无法完全解析的资源

2. **调整脚本导出模式**：
   - `ScriptExportMode: Decompiled`
   - 强制反编译所有脚本（如果有 DLL）

3. **检查 BundledAssetsExportMode**：
   - 当前是 `DirectExport`
   - 可以尝试 `GroupByAssetType` 或 `GroupByBundleName`

### 3. **GUID 引用问题（我们的修复）**

检查是否有类似的日志：
```
Warning: Dependency 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' wasn't found
```

如果看到这类警告：
1. 统计警告数量
2. 记录具体的 GUID
3. 检查对应的文件是否存在于 xapk 中

## 完整的重新解包步骤

### 步骤 1: 准备 DLL 文件

```powershell
# 1. 检查游戏类型
# 在解压后的目录中查找：
cd "D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\temp\3129\b00e1ea1"
dir -Recurse | Where-Object {$_.Extension -in ".so", ".dll", ".dat"}

# 2. 如果找到 libil2cpp.so 和 global-metadata.dat
# 使用 Il2CppDumper 生成 DLL：
# Il2CppDumper.exe libil2cpp.so global-metadata.dat output_folder
```

### 步骤 2: 修改配置

在 AssetRipper Web UI 中（http://127.0.0.1:52334）：
1. 点击 "Settings"
2. 修改以下设置：
   - **ExportUnreadableAssets**: `True`
   - **ScriptExportMode**: `Decompiled`（如果有 DLL）
   - **ScriptContentLevel**: `Level1`（尝试不同级别）

### 步骤 3: 重新导入

1. 停止当前进程（Ctrl+C）
2. 删除临时文件
3. 同时导入：
   - Triple Match City_2.9.0_APKPure.xapk
   - DLL 文件夹（如果生成了）

```powershell
# 重新启动
cd D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release
.\AssetRipper.GUI.Free.exe
```

## 如何验证我们的 GUID 修复是否生效

### 1. 查看日志中的依赖警告

在 Web UI 的日志中搜索：
```
Dependency '...' wasn't found
```

**修复前**：应该会看到很多 GUID 相关的警告
**修复后**：这些警告应该显著减少

### 2. 检查 ScriptableObject

1. 在导出的资源中找到 ScriptableObject 文件
2. 打开 .asset 文件（文本格式）
3. 查找 `m_AssetGUID` 字段
4. 验证引用是否正确解析

### 3. 统计导出的资源

```powershell
# 统计各类资源数量
cd "D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\Ripped"

# 统计所有文件
(Get-ChildItem -Recurse -File | Measure-Object).Count

# 按扩展名统计
Get-ChildItem -Recurse -File | Group-Object Extension | Select-Object Name, Count | Sort-Object Count -Descending
```

## 预期结果对比

### 没有 DLL 的情况（当前）
- ✅ 纹理（Texture2D）
- ✅ 音频（AudioClip）
- ✅ 网格（Mesh）
- ✅ 材质（Material）- 部分
- ⚠️ 预制体（Prefab）- 不完整
- ❌ MonoBehaviour - 缺少脚本引用
- ❌ ScriptableObject - 可能缺少数据

### 有 DLL + GUID 修复的情况（目标）
- ✅ 所有基础资源
- ✅ 完整的预制体结构
- ✅ MonoBehaviour 脚本引用
- ✅ ScriptableObject 完整数据
- ✅ 正确的资源依赖关系

## 常见问题

### Q: 为什么 ScriptableObject 的引用还是丢失？
A: 
1. 确认对应的 GUID 文件是否存在于 xapk 中
2. 检查文件名是否包含该 GUID
3. 查看日志是否有 "Dependency not found" 警告

### Q: 如何判断是否是 IL2CPP 游戏？
A: 查找以下文件：
```
lib/arm64-v8a/libil2cpp.so          ← IL2CPP
assets/bin/Data/Managed/*.dll       ← Mono
```

### Q: Cpp2IL 和 Il2CppDumper 哪个更好？
A: 
- **Cpp2IL**: 更现代，支持更新的 Unity 版本，推荐优先使用
- **Il2CppDumper**: 成熟稳定，对旧版本支持更好

## 下一步行动

1. **立即执行**：检查游戏类型和提取 DLL
   ```powershell
   cd "D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\temp\3129\b00e1ea1"
   Get-ChildItem -Recurse -Include *.so,*.dll,global-metadata.dat
   ```

2. **如果找到 IL2CPP 文件**：
   - 下载 Cpp2IL
   - 生成 DLL
   - 重新导入

3. **调整配置**：
   - ExportUnreadableAssets = True
   - 尝试不同的 ScriptExportMode

4. **验证修复**：
   - 查看依赖警告是否减少
   - 检查 ScriptableObject 引用

---

**需要我帮你检查临时目录中的文件结构吗？** 这样可以确定游戏使用的是 IL2CPP 还是 Mono，然后提供更具体的解决方案。


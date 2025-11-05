# AssetRipper 运行和测试指南

## 🚀 快速启动

### 方法 1：直接运行编译好的程序
```powershell
cd D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release
.\AssetRipper.GUI.Free.exe
```

### 方法 2：使用 dotnet run（推荐用于开发测试）
```powershell
cd D:\Work\Tools\AssetRipper-master\AssetRipper
dotnet run --project Source\AssetRipper.GUI.Free\AssetRipper.GUI.Free.csproj -c Release
```

## 📱 访问 Web 界面

启动后，在控制台中会看到：
```
Now listening on: http://127.0.0.1:xxxxx
```

**在浏览器中打开该地址** 即可使用 AssetRipper！

## 🧪 测试 GUID 修复

### 步骤 1：导入文件
1. 在 Web 界面点击 "Import"
2. 选择你的 XAPK/APK 文件
3. 等待导入完成

### 步骤 2：查看日志
在导入过程中，查看日志中的关键信息：

**好的信号：**
```
✅ Import : Files use the 'IL2Cpp' scripting backend.
✅ Import : Files use the 'Mono' scripting backend.
```

**需要注意：**
```
⚠️ Import : Files use the 'Unknown' scripting backend.
   → 需要提供 DLL 文件
```

**GUID 修复验证：**
```
# 修复前：应该有很多这样的警告
⚠️ Warning: Dependency '30b6e6ebf780b304f83e144c61a2e054' wasn't found

# 修复后：这类警告应该显著减少或消失
```

### 步骤 3：导出资源
1. 导入完成后，点击 "Export"
2. 选择导出格式和位置
3. 等待导出完成

### 步骤 4：验证修复效果

#### A. 检查 ScriptableObject 引用
```powershell
# 在导出目录中搜索 ScriptableObject
cd "导出目录路径"
Get-ChildItem -Recurse -Filter "*.asset" | Select-Object -First 5

# 打开一个 .asset 文件，查找 m_AssetGUID
# 应该能看到完整的引用信息
```

#### B. 统计导出的资源
```powershell
# 统计各类资源数量
Get-ChildItem -Recurse -File | Group-Object Extension | Select-Object Name, Count | Sort-Object Count -Descending

# 常见扩展名：
# .png, .jpg  - 纹理
# .wav, .mp3  - 音频
# .fbx, .obj  - 模型
# .prefab     - 预制体
# .asset      - ScriptableObject
```

#### C. 检查依赖警告数量
```powershell
# 在导出的日志文件中搜索
Select-String -Path "日志文件路径" -Pattern "Dependency.*wasn't found" | Measure-Object

# 对比修复前后的数量
```

## 📊 预期结果

### 修复生效的标志：
1. ✅ 日志中 "Dependency not found" 警告减少
2. ✅ ScriptableObject 文件中包含完整的 GUID 引用
3. ✅ Prefab 的引用链完整
4. ✅ 资源之间的依赖关系正确

### 如果仍有问题：

#### 问题 1：`Unknown scripting backend`
**原因**：缺少 DLL 文件  
**解决**：
1. 提取 libil2cpp.so 和 global-metadata.dat
2. 使用 Cpp2IL 生成 DLL
3. 重新导入

#### 问题 2：某些 GUID 仍然找不到
**原因**：对应的文件可能在其他位置  
**解决**：
1. 检查是否有 OBB 文件
2. 检查是否有 Split APK
3. 确保所有相关文件都被导入

#### 问题 3：资源导出不完整
**调整配置**：
```
Settings → ExportUnreadableAssets: True
Settings → ScriptContentLevel: Level1
Settings → ScriptExportMode: Decompiled (如果有 DLL)
```

## 🔍 调试技巧

### 1. 启用详细日志
在配置中查找日志级别设置，设置为 Debug 或 Verbose

### 2. 逐个导入文件
如果问题复杂，可以：
- 先导入主 APK
- 再导入 OBB（如果有）
- 最后导入 DLL 文件夹

### 3. 使用命令行版本（可选）
```powershell
# 如果需要批处理或自动化
AssetRipper.CLI.exe export "输入路径" "输出路径"
```

## 📝 测试报告模板

完成测试后，记录以下信息：

```
【测试信息】
游戏名称：Triple Match City
版本：2.9.0
文件格式：XAPK

【导入结果】
Scripting Backend: Unknown/Mono/IL2CPP
总文件数：xxxx
成功解析：xxxx
失败数量：xxxx

【GUID 修复验证】
修复前警告数：xxx
修复后警告数：xxx
改善率：xx%

【导出资源统计】
纹理：xxx 个
音频：xxx 个
模型：xxx 个
预制体：xxx 个
ScriptableObject：xxx 个

【问题记录】
1. ...
2. ...

【结论】
修复效果：成功/部分成功/需要进一步优化
```

## 🎯 成功案例对比

### 修复前（预期）：
```
Warning: Dependency '30b6e6ebf780b304f83e144c61a2e054' wasn't found
Warning: Dependency 'a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6' wasn't found
...（可能有数百个这样的警告）

导出的 ScriptableObject：
  collectableItem: {fileID: 0, guid: 00000000000000000000000000000000, type: 0}
  ↑ 引用丢失
```

### 修复后（目标）：
```
所有依赖成功解析
或
仅剩少量真正缺失的文件警告

导出的 ScriptableObject：
  collectableItem: {fileID: 123456, guid: 30b6e6ebf780b304f83e144c61a2e054, type: 3}
  ↑ 引用完整
```

---

## 快速命令参考

```powershell
# 1. 启动工具
cd D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release
.\AssetRipper.GUI.Free.exe

# 2. 检查临时文件
cd Release\temp
dir

# 3. 查看导出结果
cd Release\Ripped
Get-ChildItem -Recurse | Group-Object Extension

# 4. 搜索特定 GUID
Get-ChildItem -Recurse | Select-String "30b6e6ebf780b304f83e144c61a2e054"

# 5. 重新编译（如果需要）
cd D:\Work\Tools\AssetRipper-master\AssetRipper
dotnet build -c Release
```

---

**祝测试顺利！** 🎉

如有任何问题，查看 `TROUBLESHOOTING_XAPK.md` 和 `GUID_FIX_SUMMARY.md` 获取更多信息。


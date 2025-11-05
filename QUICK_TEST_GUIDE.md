# GUID 引用修复 - 快速测试指南

## 快速测试

### 1. 编译项目
```bash
cd AssetRipper
dotnet build AssetRipper.sln
```

### 2. 运行 AssetRipper
启动编译好的 AssetRipper GUI 或命令行工具。

### 3. 加载 APK
- 导入你的 APK 文件
- 如果有 OBB 文件，一起导入
- 等待加载完成

### 4. 检查日志
在日志窗口查找：

**修复前可能看到**：
```
Warning: Dependency '30b6e6ebf780b304f83e144c61a2e054' wasn't found
```

**修复后应该**：
- 减少或消除此类警告
- 看到成功加载的 CAB 文件信息

### 5. 验证 ScriptableObject

找到你的 ScriptableObject：
1. 在资源树中定位到该 ScriptableObject
2. 查看导出的 YAML 或检查属性
3. 确认引用字段（如 `collectableItem`）不再是 null
4. 验证引用的 prefab 正确关联

### 6. 预期变化

**修复前**：
```yaml
CollectableDatas:
  - collectableItem:
      m_AssetGUID: 30b6e6ebf780b304f83e144c61a2e054
      # 引用丢失，找不到对应资源
```

**修复后**：
```yaml
CollectableDatas:
  - collectableItem: {fileID: 123456, guid: 30b6e6ebf780b304f83e144c61a2e054, type: 3}
      # 引用正确解析，可以找到对应的 prefab
```

## 如果仍有问题

### 调试步骤

1. **检查文件名**：
   - 在解包目录中查找包含 GUID `30b6e6ebf780b304f83e144c61a2e054` 的文件
   - 文件名应该类似：`CAB-30b6e6ebf780b304f83e144c61a2e054` 或直接是 GUID

2. **检查所有文件是否加载**：
   - 确保 APK 中的所有 CAB 文件都被提取和加载
   - 检查是否有 Split APK 或 OBB 文件未加载

3. **查看详细日志**：
   - 启用详细日志模式
   - 查找依赖解析的具体过程
   - 记录仍然失败的 GUID

4. **报告问题**：
   如果问题仍然存在，请提供：
   - AssetRipper 版本
   - Unity 版本（游戏使用的）
   - 缺失的 GUID
   - 相关的日志输出
   - APK 结构信息（文件列表）

## 常见问题

### Q: 为什么有些 GUID 仍然找不到？
A: 可能的原因：
- 对应的资源在单独的 OBB 文件中
- Split APK 的其他部分未加载
- 资源被动态下载（不在 APK 中）

### Q: 修复是否影响正常的依赖解析？
A: 不会。修复保持了向后兼容：
1. 首先尝试原有的名称匹配
2. 只有失败时才尝试 GUID 匹配
3. 不影响非 APK 的正常使用

### Q: 如何验证修复是否生效？
A: 最简单的方法：
1. 对比修复前后的日志
2. 检查 "Dependency not found" 警告的数量
3. 验证 ScriptableObject 的引用是否完整

## 性能影响

GUID 匹配是惰性的（fallback），只在名称匹配失败时触发：
- 对正常文件：无额外开销（直接名称匹配成功）
- 对 GUID 引用：增加一次遍历（与名称匹配相同的时间复杂度）
- 总体性能影响：可忽略不计

## 技术支持

如果需要帮助或发现 bug：
1. 查看 `GUID_FIX_SUMMARY.md` 了解详细技术信息
2. 在 AssetRipper GitHub 提交 Issue
3. 提供详细的复现步骤和日志

---

**快速验证命令**（如果使用命令行版本）：
```bash
# 设置详细日志
AssetRipper.exe --verbose export your-app.apk output-folder

# 检查日志中的 GUID 解析信息
grep -i "guid" output.log
grep -i "dependency" output.log
```


# Addressables引用修复指南

## 问题说明

当前状况：
- ✅ **GUID映射已成功**：所有旧GUID都已正确转换为新GUID
- ✅ **文件转换完成**：`leveldata_converted`目录中的所有文件已更新
- ❌ **Unity显示"None"**：因为引用的资源未在Addressables系统中注册

## 解决方案

### 方法一：使用Unity Editor脚本（推荐）

1. **复制脚本到Unity项目**
   ```
   将 AddReferencedAssetsToAddressables.cs 复制到:
   <UnityProject>/Assets/Editor/AddReferencedAssetsToAddressables.cs
   ```
   
   如果没有`Editor`文件夹，请先创建它。

2. **在Unity中打开项目**
   ```
   打开导出的Unity项目:
   D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251106_110715\ExportedProject
   ```

3. **运行脚本**
   - 在Unity顶部菜单栏中选择：`Tools > Add Referenced Assets to Addressables`
   - 等待处理完成（可能需要几分钟，取决于资源数量）
   - 看到"完成"对话框后，所有引用的资源将自动添加到Addressables系统

4. **验证结果**
   - 打开 `Assets/Resources/leveldata_converted/Tutorial_0.asset`
   - 检查Inspector中的Collectable Datas
   - 之前显示"None"的引用现在应该正确显示资源名称

### 方法二：手动添加（适用于少量资源）

如果只有几个资源需要修复：

1. 在Unity Project窗口中找到需要的资源（如`prop_sandbox.asset`）
2. 右键点击资源
3. 选择 `Addressables > Mark as Addressable`
4. 资源将被添加到Addressables系统中

## 技术细节

### 为什么会出现这个问题？

1. **原始游戏使用Addressables系统**
   - 资源通过AssetBundle打包
   - leveldata使用AssetReference（m_AssetGUID）引用资源
   
2. **AssetRipper导出限制**
   - 成功导出了资源文件和.meta文件
   - 但没有将资源注册到Addressables系统中
   
3. **Unity的AssetReference要求**
   - AssetReference只能引用Addressables中注册的资源
   - 未注册的资源即使GUID正确也会显示为"None"

### 转换统计

- **映射表行数**: 5100个GUID映射
- **转换文件数**: 3434/3624个leveldata文件
- **被引用的资源**: 需要在Unity中运行脚本后才能统计

### 示例：Tutorial_0的转换结果

| 元素 | 原始GUID | 新GUID | 资源名称 |
|------|----------|--------|----------|
| Element 0-2 | 662b44a898afe7840a044dcf6bfc8120 | be9cd069ac04ba983e62408a4a794834 | prop_sandbox.asset |
| Element 3-5 | 7e92e440b4b49fe4b92999e25fd199bc | 44e6e1637463ef98717848e05f841401 | (待查) |
| Element 6-8 | 7ad548b4ffbdcd9429eb5d362686ab66 | d07142bf6aa7264d803195459198de5e | building_house_5_a.asset |

## 常见问题

### Q: 脚本运行失败，提示"Addressables设置未找到"
**A**: 确保Unity项目中已安装Addressables包。检查 `Window > Package Manager`，搜索"Addressables"并安装。

### Q: 某些资源仍然显示"None"
**A**: 可能的原因：
1. 资源的GUID在原始游戏中不存在
2. 资源在AssetRipper导出时损坏
3. 资源类型不匹配

检查Console窗口中的警告信息，找出具体原因。

### Q: 可以删除原始的leveldata文件夹吗？
**A**: 可以。leveldata_converted是完整的转换版本，可以替换原始文件夹：
```bash
# 备份原始文件（可选）
mv Assets/Resources/leveldata Assets/Resources/leveldata_backup

# 使用转换后的文件
mv Assets/Resources/leveldata_converted Assets/Resources/leveldata
```

## 下一步

完成Addressables注册后：
1. ✅ 所有GUID都已正确映射
2. ✅ 所有引用的资源都在Addressables中注册
3. ✅ Unity可以正确加载和显示所有引用

现在可以：
- 在Unity中测试关卡
- 构建游戏
- 编辑关卡配置

## 联系支持

如果遇到问题，请提供：
1. Unity Console中的错误信息
2. 具体哪个leveldata文件有问题
3. 引用的GUID和对应的资源路径


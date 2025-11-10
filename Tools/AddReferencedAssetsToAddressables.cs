using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AssetRipper.Tools
{
    /// <summary>
    /// Unity Editor工具：将leveldata中引用的所有资源添加到Addressables系统
    /// 使用方法：将此脚本放入Unity项目的Editor文件夹，然后在Unity菜单中选择 Tools > Add Referenced Assets to Addressables
    /// </summary>
    public class AddReferencedAssetsToAddressables
    {
        [MenuItem("Tools/Add Referenced Assets to Addressables")]
        public static void AddAllReferencedAssets()
        {
            try
            {
                // 1. 获取Addressables设置
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                {
                    Debug.LogError("Addressables设置未找到！请先初始化Addressables系统。");
                    return;
                }

                // 2. 获取或创建默认组
                AddressableAssetGroup defaultGroup = settings.DefaultGroup;
                if (defaultGroup == null)
                {
                    Debug.LogError("找不到默认Addressables组！");
                    return;
                }

                // 3. 扫描所有leveldata文件，提取GUID
                string leveldataPath = "Assets/Resources/leveldata_converted";
                if (!Directory.Exists(leveldataPath))
                {
                    // 尝试原始路径
                    leveldataPath = "Assets/Resources/leveldata";
                    if (!Directory.Exists(leveldataPath))
                    {
                        Debug.LogError($"找不到leveldata目录！路径: {leveldataPath}");
                        return;
                    }
                }

                HashSet<string> referencedGuids = new HashSet<string>();
                var files = Directory.GetFiles(leveldataPath, "*.asset", SearchOption.AllDirectories);
                
                Debug.Log($"开始扫描 {files.Length} 个leveldata文件...");
                
                foreach (var file in files)
                {
                    var content = File.ReadAllText(file);
                    // 提取所有 m_AssetGUID
                    var matches = Regex.Matches(content, @"m_AssetGUID:\s*([0-9a-fA-F]{32})");
                    foreach (Match match in matches)
                    {
                        var guid = match.Groups[1].Value.ToLower();
                        referencedGuids.Add(guid);
                    }
                }

                Debug.Log($"找到 {referencedGuids.Count} 个唯一的引用GUID");

                // 4. 对每个GUID，找到对应的资源并添加到Addressables
                int addedCount = 0;
                int alreadyAddedCount = 0;
                int notFoundCount = 0;

                foreach (var guid in referencedGuids)
                {
                    // 尝试从GUID找到资源路径
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        Debug.LogWarning($"找不到GUID对应的资源: {guid}");
                        notFoundCount++;
                        continue;
                    }

                    // 检查是否已经在Addressables中
                    var entry = settings.FindAssetEntry(guid);
                    if (entry != null)
                    {
                        alreadyAddedCount++;
                        continue;
                    }

                    // 添加到Addressables
                    var newEntry = settings.CreateOrMoveEntry(guid, defaultGroup, false, false);
                    if (newEntry != null)
                    {
                        // 设置地址为文件名（不含扩展名）
                        string fileName = Path.GetFileNameWithoutExtension(assetPath);
                        newEntry.SetAddress(fileName);
                        addedCount++;
                        
                        if (addedCount % 100 == 0)
                        {
                            Debug.Log($"进度: 已添加 {addedCount} 个资源...");
                        }
                    }
                }

                // 5. 保存更改
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
                AssetDatabase.SaveAssets();

                // 6. 输出统计信息
                Debug.Log("=== 添加完成 ===");
                Debug.Log($"新添加: {addedCount} 个资源");
                Debug.Log($"已存在: {alreadyAddedCount} 个资源");
                Debug.Log($"未找到: {notFoundCount} 个GUID");
                Debug.Log($"总计: {referencedGuids.Count} 个引用");

                EditorUtility.DisplayDialog("完成", 
                    $"成功添加 {addedCount} 个资源到Addressables系统！\n" +
                    $"已存在: {alreadyAddedCount}\n" +
                    $"未找到: {notFoundCount}", 
                    "确定");
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"处理失败: {ex.Message}", "确定");
            }
        }
    }
}
#endif


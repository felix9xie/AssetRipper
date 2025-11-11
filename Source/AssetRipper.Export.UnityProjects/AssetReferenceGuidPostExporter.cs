using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// 修复 AssetReference 中的 m_AssetGUID 字段
/// 将 Addressable catalog 格式的 GUID 转换为 Unity .meta 文件格式
/// </summary>
public sealed class AssetReferenceGuidPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "=== AssetReference GUID Fix Started ===");
		Console.WriteLine("=== Fixing AssetReference GUIDs ===");

		string assetsPath = Path.Combine(settings.ExportRootPath, "ExportedProject", "Assets");
		
		if (!Directory.Exists(assetsPath))
		{
			Logger.Warning(LogCategory.Export, $"Assets directory not found: {assetsPath}");
			return;
		}

		// 获取所有 catalog GUID 映射
		var catalogGuids = BundleGuidExtractor.GetAllCatalogGuids();
		
		if (catalogGuids.Count == 0)
		{
			Logger.Warning(LogCategory.Export, "No catalog GUIDs found. Skipping AssetReference GUID fix.");
			Console.WriteLine("⚠️ No catalog GUIDs found. This might not be an Addressable project.");
			return;
		}
		
		Logger.Info(LogCategory.Export, $"Found {catalogGuids.Count} catalog GUIDs");
		Console.WriteLine($"📋 Found {catalogGuids.Count} catalog GUIDs to process");

		// 遍历所有 .asset 文件
		int totalFiles = 0;
		int processedFiles = 0;
		int fixedGuids = 0;

		foreach (string assetFile in Directory.EnumerateFiles(assetsPath, "*.asset", SearchOption.AllDirectories))
		{
			totalFiles++;
			
			try
			{
				int fixedCount = ProcessAssetFile(assetFile, catalogGuids);
				if (fixedCount > 0)
				{
					processedFiles++;
					fixedGuids += fixedCount;
					
					if (processedFiles <= 10) // 只打印前 10 个文件
					{
						string relativePath = Path.GetRelativePath(assetsPath, assetFile);
						Console.WriteLine($"  ✓ {relativePath}: {fixedCount} GUID(s) fixed");
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to process {assetFile}: {ex.Message}");
			}
		}

		Logger.Info(LogCategory.Export, $"AssetReference GUID Fix completed: {processedFiles}/{totalFiles} files modified, {fixedGuids} GUIDs fixed");
		Console.WriteLine($"\n✅ Fixed {fixedGuids} AssetReference GUIDs in {processedFiles} files (scanned {totalFiles} files)");
		Console.WriteLine("=== AssetReference GUID Fix Completed ===\n");
	}

	/// <summary>
	/// 处理单个 .asset 文件，转换其中的 m_AssetGUID
	/// </summary>
	private int ProcessAssetFile(string assetFilePath, IReadOnlyDictionary<string, string> catalogGuids)
	{
		string content = File.ReadAllText(assetFilePath, Encoding.UTF8);
		
		// 查找所有的 m_AssetGUID 字段
		// 格式：  m_AssetGUID: 662b44a898afe7840a044dcf6bfc8120
		var regex = new Regex(@"^\s*m_AssetGUID:\s+([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline);
		var matches = regex.Matches(content);
		
		if (matches.Count == 0)
		{
			return 0; // 没有 AssetReference
		}

		int fixedCount = 0;
		var replacements = new Dictionary<string, string>();

		foreach (Match match in matches)
		{
			string catalogGuid = match.Groups[1].Value.ToLower();
			
			// 检查是否在 catalog 中存在
			if (catalogGuids.ContainsKey(catalogGuid))
			{
				// 转换为 .meta 格式
				string metaGuid = BundleGuidExtractor.ConvertCatalogGuidToMetaGuid(catalogGuid);
				
				if (metaGuid != catalogGuid)
				{
					replacements[catalogGuid] = metaGuid;
					fixedCount++;
				}
			}
		}

		// 如果有需要替换的 GUID
		if (fixedCount > 0)
		{
			// 执行替换
			string newContent = content;
			foreach (var kvp in replacements)
			{
				string oldPattern = $"m_AssetGUID: {kvp.Key}";
				string newPattern = $"m_AssetGUID: {kvp.Value}";
				newContent = newContent.Replace(oldPattern, newPattern);
				
				// 也处理大写版本
				oldPattern = $"m_AssetGUID: {kvp.Key.ToUpper()}";
				newPattern = $"m_AssetGUID: {kvp.Value}";
				newContent = newContent.Replace(oldPattern, newPattern);
			}
			
			// 写回文件
			File.WriteAllText(assetFilePath, newContent, Encoding.UTF8);
		}

		return fixedCount;
	}
}


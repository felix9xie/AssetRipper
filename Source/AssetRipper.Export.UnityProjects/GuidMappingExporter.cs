using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Bundles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// 导出原始 GUID 到新 GUID 的映射表
/// 用于解决 Addressable AssetReference 的 GUID 引用问题
/// </summary>
public static class GuidMappingExporter
{
	private static readonly Dictionary<string, GuidMappingEntry> _mappings = new();
	private static readonly object _lock = new();

	public class GuidMappingEntry
	{
		[JsonPropertyName("original_guid")]
		public string? OriginalGuid { get; set; }

		[JsonPropertyName("new_guid")]
		public string NewGuid { get; set; } = string.Empty;

		[JsonPropertyName("collection_guid")]
		public string CollectionGuid { get; set; } = string.Empty;

		[JsonPropertyName("path_id")]
		public long PathId { get; set; }

		[JsonPropertyName("asset_path")]
		public string? AssetPath { get; set; }

		[JsonPropertyName("asset_type")]
		public string? AssetType { get; set; }
	}

	public class CatalogEntry
	{
		[JsonPropertyName("guid")]
		public string Guid { get; set; } = string.Empty;

		[JsonPropertyName("internal_id")]
		public string InternalId { get; set; } = string.Empty;
	}

	/// <summary>
	/// 记录一个资源的 GUID 映射
	/// </summary>
	public static void RecordMapping(IUnityObjectBase asset, UnityGuid newGuid, string? exportPath = null)
	{
		lock (_lock)
		{
			var collectionGuid = asset.Collection.Guid;
			var pathId = asset.PathID;
			
			var key = $"{collectionGuid}_{pathId}";
			
			if (!_mappings.ContainsKey(key))
			{
				_mappings[key] = new GuidMappingEntry
				{
					NewGuid = newGuid.ToString(),
					CollectionGuid = collectionGuid.ToString(),
					PathId = pathId,
					AssetPath = exportPath,
					AssetType = asset.GetType().Name
				};
			}
		}
	}

	/// <summary>
	/// 从 Addressable catalog 加载原始 GUID 映射
	/// </summary>
	public static void LoadCatalogMappings(string catalogPath)
	{
		if (!File.Exists(catalogPath))
		{
			Console.WriteLine($"[GuidMapping] Catalog not found: {catalogPath}");
			return;
		}

		try
		{
			Console.WriteLine($"[GuidMapping] Loading catalog: {catalogPath}");
			
			// 解析 catalog.json
			var catalogMappings = ParseCatalog(catalogPath);
			
			Console.WriteLine($"[GuidMapping] Found {catalogMappings.Count} catalog entries");

			// 尝试将 catalog 中的 GUID 关联到已记录的资源
			int matchedCount = 0;
			foreach (var catalogEntry in catalogMappings)
			{
				// InternalId 格式示例：
				// - {hash}[path]
				// - Assets/...
				// - hash
				
				// 尝试从 InternalId 提取有用信息
				// 这里需要根据实际情况调整匹配逻辑
				// 暂时先记录，后续可以通过 AssetPath 匹配
			}

			Console.WriteLine($"[GuidMapping] Matched {matchedCount} catalog entries to assets");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GuidMapping] Error loading catalog: {ex.Message}");
		}
	}

	/// <summary>
	/// 导出 GUID 映射表到文件
	/// </summary>
	public static void ExportMappings(string outputPath)
	{
		try
		{
			var outputDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
			{
				Directory.CreateDirectory(outputDir);
			}

			// 导出为 JSON 格式
			var json = JsonSerializer.Serialize(_mappings.Values, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(outputPath, json);

			Console.WriteLine($"[GuidMapping] Exported {_mappings.Count} GUID mappings to: {outputPath}");

			// 同时导出简单的文本格式
			var txtPath = Path.ChangeExtension(outputPath, ".txt");
			using (var writer = new StreamWriter(txtPath, false, Encoding.UTF8))
			{
				writer.WriteLine("# GUID Mapping Table");
				writer.WriteLine($"# Generated: {DateTime.Now}");
				writer.WriteLine($"# Total: {_mappings.Count} entries");
				writer.WriteLine();
				writer.WriteLine("New GUID\t\tCollection GUID\t\tPathID\t\tAsset Path");
				writer.WriteLine(new string('=', 150));

				foreach (var entry in _mappings.Values.OrderBy(e => e.NewGuid))
				{
					writer.WriteLine($"{entry.NewGuid}\t{entry.CollectionGuid}\t{entry.PathId}\t{entry.AssetPath}");
				}
			}

			Console.WriteLine($"[GuidMapping] Exported text format to: {txtPath}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GuidMapping] Error exporting mappings: {ex.Message}");
		}
	}

	/// <summary>
	/// 清空已记录的映射
	/// </summary>
	public static void Clear()
	{
		lock (_lock)
		{
			_mappings.Clear();
		}
	}

	/// <summary>
	/// 简单的 catalog.json 解析器
	/// </summary>
	private static List<CatalogEntry> ParseCatalog(string catalogPath)
	{
		// 这里复用之前的 catalog 解析逻辑
		// 为了简化，暂时返回空列表
		// 完整实现需要解析 catalog.json 的二进制数据
		
		return new List<CatalogEntry>();
	}
}


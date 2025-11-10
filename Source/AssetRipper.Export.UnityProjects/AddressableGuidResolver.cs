using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// 解析 Addressable catalog，尝试恢复原始 GUID
/// </summary>
public static class AddressableGuidResolver
{
	// Collection GUID + PathID → 原始 GUID
	private static readonly Dictionary<string, UnityGuid> _originalGuidMap = new();
	
	// 用于调试的映射记录（线程安全）
	private static readonly ConcurrentDictionary<string, MappingRecord> _mappingRecords = new();
	
	private static bool _initialized = false;
	private static readonly object _lock = new();

	public class CatalogData
	{
		[JsonPropertyName("m_InternalIds")]
		public string[]? InternalIds { get; set; }

		[JsonPropertyName("m_KeyDataString")]
		public string? KeyDataString { get; set; }

		[JsonPropertyName("m_BucketDataString")]
		public string? BucketDataString { get; set; }
	}

	public class MappingRecord
	{
		public string CollectionGuid { get; set; } = "";
		public long PathId { get; set; }
		public string FinalGuid { get; set; } = "";
		public string? OriginalGuid { get; set; }
		public bool UsedOriginal { get; set; }
	}

	/// <summary>
	/// 初始化：扫描并加载所有 Addressable catalog 文件
	/// </summary>
	public static void Initialize(string projectDirectory)
	{
		lock (_lock)
		{
			if (_initialized) return;

		try
		{
			Console.WriteLine("[AddressableGuid] Searching for Addressable catalogs...");

			// 限制搜索范围到 assets 目录以提高性能
			var catalogFiles = new List<string>();
			string assetsPath = Path.Combine(projectDirectory, "assets");
			
			if (Directory.Exists(assetsPath))
			{
				try
				{
					catalogFiles.AddRange(Directory.GetFiles(assetsPath, "catalog*.json", SearchOption.TopDirectoryOnly));
					
					// 也检查 aa 子目录（Addressables 的常见位置）
					string aaPath = Path.Combine(assetsPath, "aa");
					if (Directory.Exists(aaPath))
					{
						catalogFiles.AddRange(Directory.GetFiles(aaPath, "catalog*.json", SearchOption.AllDirectories));
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[AddressableGuid] Error searching assets directory: {ex.Message}");
				}
			}
			
			// 作为后备，也检查根目录（仅顶层）
			try
			{
				catalogFiles.AddRange(Directory.GetFiles(projectDirectory, "catalog*.json", SearchOption.TopDirectoryOnly));
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[AddressableGuid] Error searching root directory: {ex.Message}");
			}

				foreach (var catalogPath in catalogFiles)
				{
					try
					{
						LoadCatalog(catalogPath);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[AddressableGuid] Error loading {catalogPath}: {ex.Message}");
					}
				}

				Console.WriteLine($"[AddressableGuid] Loaded {_originalGuidMap.Count} original GUID mappings");
				_initialized = true;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[AddressableGuid] Initialization error: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 尝试查找资源的原始 GUID
	/// </summary>
	public static bool TryFindOriginalGuid(IUnityObjectBase asset, out UnityGuid originalGuid)
	{
		// 方法 1: 通过 Collection+PathID 直接查找（已建立映射的资源）
		var key = GetAssetKey(asset);
		if (_originalGuidMap.TryGetValue(key, out originalGuid))
		{
			return true;
		}

		// 方法 2: 通过 OriginalPath 匹配 catalog 中的路径
		if (!string.IsNullOrEmpty(asset.OriginalPath))
		{
			string normalizedPath = NormalizePath(asset.OriginalPath);
			
			// 尝试精确匹配
			if (_internalIdToGuid.TryGetValue(normalizedPath, out string? guidString))
			{
				if (TryParseGuid(guidString, out originalGuid))
				{
					// 缓存此映射
					_originalGuidMap[key] = originalGuid;
					return true;
				}
			}
			
			// 尝试模糊匹配（去除扩展名）
			string pathWithoutExt = Path.ChangeExtension(normalizedPath, null);
			if (pathWithoutExt != null)
			{
				// 尝试匹配所有可能的扩展名
				var possiblePaths = _internalIdToGuid.Keys.Where(p => 
					Path.ChangeExtension(p, null)?.Equals(pathWithoutExt, StringComparison.OrdinalIgnoreCase) == true
				).ToList();

				if (possiblePaths.Count == 1)
				{
					if (TryParseGuid(_internalIdToGuid[possiblePaths[0]], out originalGuid))
					{
						_originalGuidMap[key] = originalGuid;
						return true;
					}
				}
			}
		}

		originalGuid = default;
		return false;
	}

	/// <summary>
	/// 将字符串 GUID 解析为 UnityGuid
	/// </summary>
	private static bool TryParseGuid(string guidString, out UnityGuid guid)
	{
		try
		{
			if (string.IsNullOrEmpty(guidString) || guidString.Length != 32)
			{
				guid = default;
				return false;
			}

			// Unity GUID 字符串格式：大端序的十六进制表示
			// 直接解析每8个字符为一个uint32，UnityGuid会自动处理字节序
			uint data0 = Convert.ToUInt32(guidString.Substring(0, 8), 16);
			uint data1 = Convert.ToUInt32(guidString.Substring(8, 8), 16);
			uint data2 = Convert.ToUInt32(guidString.Substring(16, 8), 16);
			uint data3 = Convert.ToUInt32(guidString.Substring(24, 8), 16);

			guid = new UnityGuid(data0, data1, data2, data3);
			return true;
		}
		catch
		{
			guid = default;
			return false;
		}
	}

	/// <summary>
	/// 记录映射关系用于调试
	/// </summary>
	public static void RecordMapping(IUnityObjectBase asset, UnityGuid finalGuid)
	{
		var key = GetAssetKey(asset);
		var hasOriginal = _originalGuidMap.TryGetValue(key, out var originalGuid);

		_mappingRecords[key] = new MappingRecord
		{
			CollectionGuid = asset.Collection.Guid.ToString(),
			PathId = asset.PathID,
			FinalGuid = finalGuid.ToString(),
			OriginalGuid = hasOriginal ? originalGuid.ToString() : null,
			UsedOriginal = hasOriginal
		};
	}

	/// <summary>
	/// 导出映射记录到文件（用于调试）
	/// </summary>
	public static void ExportMappingRecords(string outputPath)
	{
		try
		{
			var records = _mappingRecords.Values.OrderBy(r => r.FinalGuid).ToList();
			
			var summary = new
			{
				TotalAssets = records.Count,
				WithOriginalGuid = records.Count(r => r.UsedOriginal),
				Generated = records.Count(r => !r.UsedOriginal)
			};

			Console.WriteLine($"[AddressableGuid] Mapping summary:");
			Console.WriteLine($"  - Total assets: {summary.TotalAssets}");
			Console.WriteLine($"  - Used original GUID: {summary.WithOriginalGuid}");
			Console.WriteLine($"  - Generated GUID: {summary.Generated}");
			
		// Try JSON serialization, fall back to manual if it fails
		string json;
		try
		{
			var jsonOptions = new JsonSerializerOptions 
			{ 
				WriteIndented = true,
				TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine()
			};
			json = JsonSerializer.Serialize(records, jsonOptions);
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
		{
			// Fallback: manual JSON generation for AOT compatibility
			Console.WriteLine($"[AddressableGuid] Using manual JSON fallback due to: {ex.GetType().Name}");
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("[");
			for (int i = 0; i < records.Count; i++)
			{
				var r = records[i];
				sb.AppendLine("  {");
				sb.AppendLine($"    \"CollectionGuid\": \"{r.CollectionGuid}\",");
				sb.AppendLine($"    \"PathId\": {r.PathId},");
				sb.AppendLine($"    \"FinalGuid\": \"{r.FinalGuid}\",");
				sb.AppendLine($"    \"OriginalGuid\": {(r.OriginalGuid != null ? $"\"{r.OriginalGuid}\"" : "null")},");
				sb.AppendLine($"    \"UsedOriginal\": {r.UsedOriginal.ToString().ToLower()}");
				sb.Append("  }");
				if (i < records.Count - 1) sb.AppendLine(",");
				else sb.AppendLine();
			}
			sb.AppendLine("]");
			json = sb.ToString();
		}
			
			File.WriteAllText(outputPath, json);
			Console.WriteLine($"  - Mapping file: {outputPath}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[AddressableGuid] Error exporting records: {ex.Message}");
		}
	}

	// InternalId → 原始GUID 的临时映射（用于后续匹配）
	private static readonly Dictionary<string, string> _internalIdToGuid = new();

	/// <summary>
	/// 加载 Addressable catalog 文件
	/// </summary>
	private static void LoadCatalog(string catalogPath)
	{
		Console.WriteLine($"[AddressableGuid] Loading catalog: {catalogPath}");

	var json = File.ReadAllText(catalogPath);
	CatalogData? catalog;
	try
	{
		var jsonOptions = new JsonSerializerOptions
		{
			TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine()
		};
		catalog = JsonSerializer.Deserialize<CatalogData>(json, jsonOptions);
	}
	catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
	{
		Console.WriteLine($"[AddressableGuid] JSON deserialization failed ({ex.GetType().Name}), catalog loading is optional");
		// For AOT compatibility, could implement manual JSON parsing here if needed
		// For now, just return as catalog loading is optional
		return;
	}

		if (catalog?.KeyDataString == null || catalog.BucketDataString == null)
		{
			Console.WriteLine("[AddressableGuid] Invalid catalog format");
			return;
		}

		// 解码 KeyDataString
		var keyData = Convert.FromBase64String(catalog.KeyDataString);

		// 解码 BucketDataString
		var bucketData = Convert.FromBase64String(catalog.BucketDataString);
		var bucketCount = BitConverter.ToInt32(bucketData, 0);

		int offset = 4;
		int foundCount = 0;

		for (int i = 0; i < bucketCount; i++)
		{
			var dataOffset = BitConverter.ToInt32(bucketData, offset);
			offset += 4;
			var entryCount = BitConverter.ToInt32(bucketData, offset);
			offset += 4;

			// 读取 key
			var keyType = keyData[dataOffset];
			if (keyType == 0) // ASCII String (GUID)
			{
				var keyValueOffset = dataOffset + 1;
				var strLen = BitConverter.ToInt32(keyData, keyValueOffset);
				keyValueOffset += 4;
				var strBytes = new byte[strLen];
				Array.Copy(keyData, keyValueOffset, strBytes, 0, strLen);
				var guidString = Encoding.ASCII.GetString(strBytes);

				// 验证是否是 GUID（32 个十六进制字符）
				if (IsGuid(guidString) && entryCount > 0 && catalog.InternalIds != null)
				{
					// 读取第一个 entry 的 InternalId
					var entryIndex = BitConverter.ToInt32(bucketData, offset);
					if (entryIndex < catalog.InternalIds.Length)
					{
						var internalId = catalog.InternalIds[entryIndex];

						// 提取资源路径
						string resourcePath = ExtractPathFromInternalId(internalId);
						
						// 建立 InternalId → 原始GUID 的映射
						// 使用标准化的路径作为 key
						string normalizedPath = NormalizePath(resourcePath);
						if (!string.IsNullOrEmpty(normalizedPath))
						{
							_internalIdToGuid[normalizedPath] = guidString;
							foundCount++;
						}
					}
				}
			}

			offset += (entryCount * 4);
		}

		Console.WriteLine($"[AddressableGuid] Extracted {foundCount} GUID → Path mappings from catalog");
	}

	/// <summary>
	/// 从 InternalId 中提取资源路径
	/// </summary>
	/// <remarks>
	/// InternalId 的格式可能是：
	/// - {hash}[Assets/path/to/file.prefab]
	/// - Assets/path/to/file.prefab
	/// - hash
	/// </remarks>
	private static string ExtractPathFromInternalId(string internalId)
	{
		if (string.IsNullOrEmpty(internalId))
			return string.Empty;

		// 格式 1: {hash}[path]
		int bracketStart = internalId.IndexOf('[');
		int bracketEnd = internalId.IndexOf(']');
		if (bracketStart >= 0 && bracketEnd > bracketStart)
		{
			return internalId.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
		}

		// 格式 2: 直接是路径
		if (internalId.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
		{
			return internalId;
		}

		// 格式 3: 只有 hash，无法提取路径
		return string.Empty;
	}

	/// <summary>
	/// 标准化路径用于匹配
	/// </summary>
	private static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path))
			return string.Empty;

		// 转换为小写，统一路径分隔符
		return path.Replace('\\', '/').ToLowerInvariant();
	}

	/// <summary>
	/// 生成资源的唯一 key
	/// </summary>
	private static string GetAssetKey(IUnityObjectBase asset)
	{
		return $"{asset.Collection.Guid}_{asset.PathID}";
	}

	/// <summary>
	/// 验证字符串是否为 GUID 格式
	/// </summary>
	private static bool IsGuid(string str)
	{
		if (string.IsNullOrEmpty(str) || str.Length != 32)
			return false;

		foreach (char c in str)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
				return false;
		}

		return true;
	}
}


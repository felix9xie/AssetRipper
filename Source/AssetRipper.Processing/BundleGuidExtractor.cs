using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetRipper.Processing;

/// <summary>
/// 通用的 Addressable GUID 提取器
/// </summary>
/// <remarks>
/// 新策略（基于 Addressable catalog.json）：
/// 1. 查找并解析所有 catalog.json 文件
/// 2. 提取 GUID → InternalId (AssetBundle路径) 映射
/// 3. 索引所有导出的资源及其路径
/// 4. 在导出时，根据资源路径匹配 catalog 中的 GUID
/// 
/// 这是一个通用方案，适用于所有使用 Addressable 的 Unity 项目
/// </remarks>
public class BundleGuidExtractor : IAssetProcessor
{
	// GUID → InternalId (从 catalog.json)
	private static readonly Dictionary<string, string> _guidToInternalId = new();
	
	// InternalId → GUID (反向索引，用于快速查找)
	private static readonly Dictionary<string, string> _internalIdToGuid = new();
	
	// 资源名称（小写） → GUID（用于模糊匹配）
	private static readonly Dictionary<string, List<string>> _nameToGuids = new();
	
	// (CollectionGuid_PathID) → AssetInfo
	private static readonly Dictionary<string, AssetInfo> _assetKeyToInfo = new();
	
	private static readonly object _lock = new();

	public class AssetInfo
	{
		public UnityGuid CollectionGuid { get; set; }
		public long PathID { get; set; }
		public string? Name { get; set; }
		public string? Path { get; set; }
		public string? ClassName { get; set; }
	}
	
	/// <summary>
	/// Unity Addressables ObjectType enum (from SerializationUtilities)
	/// </summary>
	private enum ObjectType : byte
	{
		AsciiString = 0,
		UnicodeString = 1,
		UInt16 = 2,
		UInt32 = 3,
		Int32 = 4,
		Hash128 = 5,
		Type = 6,
		JsonObject = 7
	}
	
	private class CatalogData
	{
		public string[]? InternalIds { get; set; }
		public string? KeyDataString { get; set; }
		public string? BucketDataString { get; set; }
		public string? EntryDataString { get; set; }
	}
	
	/// <summary>
	/// 从 JSON 字符串中提取 catalog 字段（简单解析，避免依赖外部 JSON 库）
	/// </summary>
	private CatalogData? ExtractCatalogFields(string json)
	{
		try
		{
		var catalog = new CatalogData();
		
		// 提取 m_KeyDataString
		catalog.KeyDataString = ExtractJsonStringField(json, "m_KeyDataString");
		
		// 提取 m_BucketDataString
		catalog.BucketDataString = ExtractJsonStringField(json, "m_BucketDataString");
		
		// 提取 m_EntryDataString
		catalog.EntryDataString = ExtractJsonStringField(json, "m_EntryDataString");
		
		// 提取 m_InternalIds 数组
		catalog.InternalIds = ExtractJsonStringArray(json, "m_InternalIds");
		
		return catalog;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Processing, $"Error extracting catalog fields: {ex.Message}");
			return null;
		}
	}
	
	/// <summary>
	/// 从 JSON 中提取字符串字段的值
	/// </summary>
	private string? ExtractJsonStringField(string json, string fieldName)
	{
		string pattern = $"\"{fieldName}\":\"";
		int startIndex = json.IndexOf(pattern);
		if (startIndex < 0) return null;
		
		startIndex += pattern.Length;
		int endIndex = json.IndexOf("\"", startIndex);
		if (endIndex < 0) return null;
		
		return json.Substring(startIndex, endIndex - startIndex);
	}
	
	/// <summary>
	/// 从 JSON 中提取字符串数组
	/// </summary>
	private string[]? ExtractJsonStringArray(string json, string fieldName)
	{
		string pattern = $"\"{fieldName}\":[";
		int startIndex = json.IndexOf(pattern);
		if (startIndex < 0) return null;
		
		startIndex += pattern.Length;
		int endIndex = json.IndexOf("]", startIndex);
		if (endIndex < 0) return null;
		
		string arrayContent = json.Substring(startIndex, endIndex - startIndex);
		
		// 简单解析：分割字符串并移除引号
		var items = new List<string>();
		int pos = 0;
		while (pos < arrayContent.Length)
		{
			// 跳过空格和逗号
			while (pos < arrayContent.Length && (arrayContent[pos] == ' ' || arrayContent[pos] == ','))
				pos++;
			
			if (pos >= arrayContent.Length) break;
			
			// 查找字符串开始
			if (arrayContent[pos] == '"')
			{
				pos++; // 跳过开始引号
				int itemStart = pos;
				
				// 查找字符串结束（处理转义字符）
				while (pos < arrayContent.Length)
				{
					if (arrayContent[pos] == '\\')
					{
						pos += 2; // 跳过转义字符
						continue;
					}
					if (arrayContent[pos] == '"')
					{
						items.Add(arrayContent.Substring(itemStart, pos - itemStart));
						pos++;
						break;
					}
					pos++;
				}
			}
		}
		
		return items.ToArray();
	}

	public void Process(GameData gameData)
	{
		Console.WriteLine("=== Addressable GUID Extraction Started ===");
		Logger.Info(LogCategory.Processing, "=== Addressable GUID Extraction Started ===");
		
		// 阶段1：查找并解析所有 catalog.json 文件
		int catalogCount = ParseAllCatalogs(gameData);
		Console.WriteLine($"Parsed {catalogCount} catalog.json file(s)");
		Console.WriteLine($"Extracted {_guidToInternalId.Count} GUID mappings from catalogs");
		Logger.Info(LogCategory.Processing, $"Parsed {catalogCount} catalogs, extracted {_guidToInternalId.Count} GUID mappings");
		
		// 打印一些示例映射
		int sampleCount = 0;
		foreach (var kvp in _guidToInternalId)
		{
			if (sampleCount++ < 10)
			{
				Console.WriteLine($"  Sample: GUID {kvp.Key} → {kvp.Value}");
			}
		}
		
		// 阶段2：索引所有资源
		int totalAssets = 0;
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			totalAssets++;
			IndexAsset(asset);
		}
		
		Console.WriteLine($"Indexed {totalAssets} assets");
		Console.WriteLine($"Created {_assetKeyToInfo.Count} asset entries");
		Logger.Info(LogCategory.Processing, $"Indexed {totalAssets} assets");

		Console.WriteLine("=== Addressable GUID Extraction Completed ===");
		Logger.Info(LogCategory.Processing, "=== Addressable GUID Extraction Completed ===");
	}
	
	/// <summary>
	/// 查找并解析所有 catalog.json 文件
	/// </summary>
	private int ParseAllCatalogs(GameData gameData)
	{
		int count = 0;
		HashSet<string> searchedPaths = new HashSet<string>();
		
		// 策略1：从 PlatformStructure 的所有文件路径中提取根目录
		if (gameData.PlatformStructure?.Files != null)
		{
			Logger.Info(LogCategory.Processing, "  Searching for catalogs from platform structure files...");
			foreach (var filePair in gameData.PlatformStructure.Files)
			{
				string filePath = filePair.Value; // Value 是完整路径
				if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
				{
					// 向上查找可能的根目录（包含 assets/aa 的目录）
					string? searchRoot = FindAssetsRoot(Path.GetDirectoryName(filePath) ?? "");
					if (searchRoot != null && searchedPaths.Add(searchRoot))
					{
						Logger.Info(LogCategory.Processing, $"  Found potential root from file: {searchRoot}");
						count += SearchAndParseCatalogs(searchRoot);
						
						// 如果找到了 catalog，可以提前返回
						if (count > 0) return count;
					}
				}
			}
		}
		
		// 策略2：从 GameBundle 的第一个 collection 获取路径线索
		var firstCollection = gameData.GameBundle.FetchAssetCollections().FirstOrDefault();
		if (firstCollection != null)
		{
			string collectionName = firstCollection.Name;
			Logger.Info(LogCategory.Processing, $"  First collection name: {collectionName}");
			
			// 尝试从 collection 名称中提取目录信息
			if (collectionName.Contains("/") || collectionName.Contains("\\"))
			{
				string? directory = Path.GetDirectoryName(collectionName);
				if (!string.IsNullOrEmpty(directory))
				{
					string? searchRoot = FindAssetsRoot(directory);
					if (searchRoot != null && searchedPaths.Add(searchRoot))
					{
						Logger.Info(LogCategory.Processing, $"  Searching for catalogs from collection path: {searchRoot}");
						count += SearchAndParseCatalogs(searchRoot);
						if (count > 0) return count;
					}
				}
			}
		}
		
		// 策略3：查找相对于当前执行目录的 temp 子目录
		string currentDir = Directory.GetCurrentDirectory();
		string tempDir = Path.Combine(currentDir, "temp");
		if (Directory.Exists(tempDir) && searchedPaths.Add(tempDir))
		{
			Logger.Info(LogCategory.Processing, $"  Searching for catalogs in: {tempDir}");
			count += SearchAndParseCatalogs(tempDir);
		}
		
		// 策略4：查找系统临时目录（备用）
		string? systemTempDir = Path.GetTempPath();
		if (!string.IsNullOrEmpty(systemTempDir))
		{
			string assetRipperTemp = Path.Combine(systemTempDir, "AssetRipper.GUI.Free.exe");
			if (Directory.Exists(assetRipperTemp) && searchedPaths.Add(assetRipperTemp))
			{
				Logger.Info(LogCategory.Processing, $"  Searching for catalogs in system temp: {assetRipperTemp}");
				count += SearchAndParseCatalogs(assetRipperTemp);
			}
		}
		
		return count;
	}
	
	/// <summary>
	/// 向上查找包含 "assets" 或 "aa" 的根目录
	/// </summary>
	private string? FindAssetsRoot(string directory)
	{
		string current = directory;
		
		for (int i = 0; i < 10; i++) // 最多向上10层
		{
			if (Directory.Exists(Path.Combine(current, "assets", "aa")) ||
			    Directory.Exists(Path.Combine(current, "aa")))
			{
				return current;
			}
			
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent == current)
				break;
			
			current = parent;
		}
		
		return null;
	}
	
	/// <summary>
	/// 在指定目录及其子目录中搜索并解析 catalog.json
	/// </summary>
	private int SearchAndParseCatalogs(string rootDirectory)
	{
		int count = 0;
		
		try
		{
			// 搜索所有 catalog.json 文件
			string[] catalogFiles = Directory.GetFiles(rootDirectory, "catalog.json", SearchOption.AllDirectories);
			
			foreach (string catalogPath in catalogFiles)
			{
				Console.WriteLine($"  Found catalog: {catalogPath}");
				
				if (ParseCatalog(catalogPath))
				{
					count++;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Processing, $"Error searching for catalogs in {rootDirectory}: {ex.Message}");
		}
		
		return count;
	}
	
	/// <summary>
	/// 解析单个 catalog.json 文件
	/// </summary>
	private bool ParseCatalog(string catalogPath)
	{
		try
		{
			string json = File.ReadAllText(catalogPath);
			
			// 简单的 JSON 字段提取
			CatalogData? catalog = ExtractCatalogFields(json);
			
		if (catalog == null || catalog.InternalIds == null || catalog.KeyDataString == null || 
			catalog.BucketDataString == null || catalog.EntryDataString == null)
		{
			Logger.Warning(LogCategory.Processing, $"Failed to parse catalog: {catalogPath}");
			return false;
		}
			
		// 解析 KeyDataString (base64 → binary → keys)
		// 使用 Unity Addressables 的 SerializationUtilities 格式
		var keyData = Convert.FromBase64String(catalog.KeyDataString);
		var keyCount = BitConverter.ToInt32(keyData, 0);
		var keys = new object[keyCount];

		// 注意：与 bucket 系统不同，这里我们需要根据 BucketDataString 来定位 keys
		// 先解析 BucketDataString
		var bucketData = Convert.FromBase64String(catalog.BucketDataString);
		var bucketCount = BitConverter.ToInt32(bucketData, 0);
		
		// 创建 bucket 映射
		var bucketOffsets = new int[bucketCount];
		int bucketOffset = 4;
		for (int i = 0; i < bucketCount; i++)
		{
		bucketOffsets[i] = BitConverter.ToInt32(bucketData, bucketOffset);
		bucketOffset += 4;
		var bucketEntryCount = BitConverter.ToInt32(bucketData, bucketOffset);
		bucketOffset += 4;
		bucketOffset += bucketEntryCount * 4; // skip entry indices
		}

		// 使用 bucket offsets 读取 keys
		for (int i = 0; i < Math.Min(keyCount, bucketOffsets.Length); i++)
		{
			int keyOffset = bucketOffsets[i];
			if (keyOffset >= keyData.Length)
				break;
				
			// 读取 ObjectType (1 byte enum)
			ObjectType objectType = (ObjectType)keyData[keyOffset];
			keyOffset++;

			try
			{
				switch (objectType)
				{
					case ObjectType.AsciiString:
					{
						var strLength = BitConverter.ToInt32(keyData, keyOffset);
						keyOffset += 4;
						keys[i] = Encoding.ASCII.GetString(keyData, keyOffset, strLength);
						break;
					}
					case ObjectType.UnicodeString:
					{
						var strLength = BitConverter.ToInt32(keyData, keyOffset);
						keyOffset += 4;
						keys[i] = Encoding.Unicode.GetString(keyData, keyOffset, strLength);
						break;
					}
					case ObjectType.Int32:
					{
						keys[i] = BitConverter.ToInt32(keyData, keyOffset);
						break;
					}
					case ObjectType.UInt32:
					{
						keys[i] = BitConverter.ToUInt32(keyData, keyOffset);
						break;
					}
					case ObjectType.UInt16:
					{
						keys[i] = BitConverter.ToUInt16(keyData, keyOffset);
						break;
					}
					default:
					{
						Logger.Warning(LogCategory.Processing, $"Unsupported ObjectType {objectType} at key {i}");
						keys[i] = $"<unsupported_{objectType}>";
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Processing, $"Failed to read key {i}: {ex.Message}");
				keys[i] = $"<error_{i}>";
			}
		}

		// 解析 EntryDataString (base64 → binary → entries)
		// 但我们不再直接从这里提取映射，而是通过 bucket 系统
		// 因为每个 Entry 可能有多个 keys (GUID 和名称)
		
		// 重新遍历 buckets，为每个 bucket 建立 key → entry indices 的映射
		int bucketOffset2 = 4;
		for (int bucketIdx = 0; bucketIdx < Math.Min(bucketCount, bucketOffsets.Length); bucketIdx++)
		{
			// 跳过 key offset
			bucketOffset2 += 4;
			
			// 读取这个 bucket 包含的 entry indices
			var entryCountInBucket = BitConverter.ToInt32(bucketData, bucketOffset2);
			bucketOffset2 += 4;
			
			var entryIndicesInBucket = new List<int>();
			for (int j = 0; j < entryCountInBucket; j++)
			{
				var entryIdx = BitConverter.ToInt32(bucketData, bucketOffset2);
				bucketOffset2 += 4;
				entryIndicesInBucket.Add(entryIdx);
			}
			
			// 获取这个 bucket 的 key
			if (bucketIdx < keys.Length && keys[bucketIdx] is string keyStr)
			{
				// 对于这个 bucket 关联的每个 entry，建立映射
				foreach (var entryIdx in entryIndicesInBucket)
				{
					if (entryIdx < catalog.InternalIds.Length)
					{
						string internalId = catalog.InternalIds[entryIdx];
						
						// 如果 key 是 GUID，建立 GUID → InternalId 映射
						if (IsGuid(keyStr))
						{
							lock (_lock)
							{
								_guidToInternalId[keyStr] = internalId;
								_internalIdToGuid[internalId.ToLower()] = keyStr;
								
								// 同时建立名称索引（用于模糊匹配）
								string fileName = Path.GetFileNameWithoutExtension(internalId).ToLower();
								if (!string.IsNullOrEmpty(fileName))
								{
									if (!_nameToGuids.ContainsKey(fileName))
									{
										_nameToGuids[fileName] = new List<string>();
									}
									if (!_nameToGuids[fileName].Contains(keyStr))
									{
										_nameToGuids[fileName].Add(keyStr);
									}
								}
							}
						}
						// 如果 key 是名称（如 "fisherman_0"），也建立映射
						else if (!string.IsNullOrEmpty(keyStr) && keyStr.Length < 100)
						{
							// 名称 key 可以帮助我们找到资源
							// 我们需要找到这个 entry 关联的 GUID keys
							// 但现在我们先建立一个名称索引
							lock (_lock)
							{
								string fileName = Path.GetFileNameWithoutExtension(internalId).ToLower();
								string keyLower = keyStr.ToLower();
								
								// 这个名称 key 指向这个 internal ID
								// 稍后我们会在同一个 entry 的其他 buckets 中找到对应的 GUID
							}
						}
					}
				}
			}
		}
		
		return true;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Processing, $"Error parsing catalog {catalogPath}: {ex.Message}");
			return false;
		}
	}
	
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
	
	private void IndexAsset(IUnityObjectBase asset)
	{
		lock (_lock)
		{
			string key = GetAssetKey(asset);
			
			if (!_assetKeyToInfo.ContainsKey(key))
			{
				string? name = GetAssetName(asset);
				
				var info = new AssetInfo
				{
					CollectionGuid = asset.Collection.Guid,
					PathID = asset.PathID,
					Name = name,
					Path = asset.OriginalPath,
					ClassName = asset.ClassName
				};
				
				_assetKeyToInfo[key] = info;
			}
		}
	}
	
	private static string GetAssetKey(IUnityObjectBase asset)
	{
		return $"{asset.Collection.Guid}_{asset.PathID}";
	}
	
	private static string? GetAssetName(IUnityObjectBase asset)
	{
		if (!string.IsNullOrEmpty(asset.OriginalName))
			return asset.OriginalName;
		
		if (!string.IsNullOrEmpty(asset.OriginalPath))
			return Path.GetFileName(asset.OriginalPath);
		
		return null;
	}
	
	/// <summary>
	/// 尝试为资源获取 Addressable GUID
	/// </summary>
	public static bool TryGetAssetGuidFromCatalog(IUnityObjectBase asset, out UnityGuid guid)
	{
		lock (_lock)
		{
			// 策略1：根据资源的原始路径精确匹配
			string? originalPath = asset.OriginalPath;
			if (!string.IsNullOrEmpty(originalPath))
			{
				// 提取文件名（不含扩展名）
				string fileName = Path.GetFileNameWithoutExtension(originalPath).ToLower();
				
				// 尝试通过名称查找
				if (_nameToGuids.TryGetValue(fileName, out var guids) && guids.Count > 0)
				{
					// 如果有多个匹配，选择第一个
					string guidString = guids[0];
					
					if (guids.Count > 1)
					{
						// 如果有多个匹配，尝试通过类型进一步筛选
						string? bestMatch = null;
						foreach (string g in guids)
						{
							if (_guidToInternalId.TryGetValue(g, out string internalId))
							{
								// 优先选择 prefab 类型
								if (asset.ClassName == "GameObject" && internalId.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
								{
									bestMatch = g;
									break;
								}
							}
						}
						
						if (bestMatch != null)
						{
							guidString = bestMatch;
						}
					}
					
					if (TryParseGuid(guidString, out guid))
					{
						Logger.Info(LogCategory.Export, 
							$"✓ Using catalog GUID for {originalPath}: {guidString}");
						return true;
					}
				}
			}
			
			// 策略2：根据资源名称模糊匹配
			string? assetName = GetAssetName(asset);
			if (!string.IsNullOrEmpty(assetName))
			{
				string nameKey = Path.GetFileNameWithoutExtension(assetName).ToLower();
				
				if (_nameToGuids.TryGetValue(nameKey, out var guids) && guids.Count > 0)
				{
					string guidString = guids[0];
					
					if (TryParseGuid(guidString, out guid))
					{
						Logger.Info(LogCategory.Export, 
							$"✓ Using catalog GUID for {assetName} (by name): {guidString}");
						return true;
					}
				}
			}
		}

		guid = default;
		return false;
	}
	
	private static bool TryParseGuid(string guidString, out UnityGuid guid)
	{
		try
		{
			if (string.IsNullOrEmpty(guidString) || guidString.Length != 32)
			{
				guid = default;
				return false;
			}

			// Unity GUID 转换：直接解析，UnityGuid.ToString() 会自动做 nibble swap
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
	/// 将 Catalog 格式的 GUID 转换为 .meta 文件格式的 GUID
	/// (Addressable catalog 使用原始格式，Unity .meta 文件使用转换后格式)
	/// </summary>
	public static string ConvertCatalogGuidToMetaGuid(string catalogGuid)
	{
		if (string.IsNullOrEmpty(catalogGuid) || catalogGuid.Length != 32)
			return catalogGuid;
		
		try
		{
			// 解析为 UnityGuid 对象
			if (TryParseGuid(catalogGuid, out UnityGuid guid))
			{
				// UnityGuid.ToString() 会自动执行 byte reverse + nibble swap
				return guid.ToString();
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Processing, $"Failed to convert catalog GUID {catalogGuid}: {ex.Message}");
		}
		
		return catalogGuid;
	}
	
	/// <summary>
	/// 尝试通过 Catalog GUID 查找对应的 .meta 格式 GUID
	/// </summary>
	public static bool TryConvertCatalogGuidToMetaGuid(string catalogGuid, out string metaGuid)
	{
		if (string.IsNullOrEmpty(catalogGuid) || catalogGuid.Length != 32)
		{
			metaGuid = catalogGuid;
			return false;
		}
		
		lock (_lock)
		{
			// 首先检查这个 GUID 是否在 catalog 中存在
			if (_guidToInternalId.ContainsKey(catalogGuid))
			{
				// 转换为 .meta 格式
				metaGuid = ConvertCatalogGuidToMetaGuid(catalogGuid);
				return true;
			}
			
			// 如果不在 catalog 中，可能已经是 .meta 格式，直接返回
			metaGuid = catalogGuid;
			return false;
		}
	}
	
	/// <summary>
	/// 获取所有的 Catalog GUID (用于批量处理)
	/// </summary>
	public static IReadOnlyDictionary<string, string> GetAllCatalogGuids()
	{
		lock (_lock)
		{
			return new Dictionary<string, string>(_guidToInternalId);
		}
	}
}

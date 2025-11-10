using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.SerializationLogic;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetRipper.Processing;

/// <summary>
/// 从 AssetBundle 提取 (Bundle GUID, PathID) → 资源信息的完整映射
/// </summary>
/// <remarks>
/// 策略：
/// 1. 扫描所有资源，记录 (CollectionGuid, PathID, 资源名称, 资源路径)
/// 2. 扫描所有 leveldata，提取 (GroupId, m_AssetGUID) 对应关系
/// 3. 通过 GroupId 匹配资源名称，建立 m_AssetGUID → (CollectionGuid, PathID) 映射
/// 4. 在导出时，查找到对应的 (CollectionGuid, PathID)，提取其原始 Bundle GUID
/// </remarks>
public class BundleGuidExtractor : IAssetProcessor
{
	// (CollectionGuid_PathID) → AssetInfo
	private static readonly Dictionary<string, AssetInfo> _assetKeyToInfo = new();
	
	// AssetReference GUID → (CollectionGuid_PathID)
	private static readonly Dictionary<string, string> _assetRefGuidToKey = new();
	
	// GroupId → List of (CollectionGuid_PathID)
	private static readonly Dictionary<string, List<string>> _groupIdToAssetKeys = new();
	
	private static readonly object _lock = new();

	public class AssetInfo
	{
		public UnityGuid CollectionGuid { get; set; }
		public long PathID { get; set; }
		public string? Name { get; set; }
		public string? Path { get; set; }
		public string? ClassName { get; set; }
	}

	public void Process(GameData gameData)
	{
		Console.WriteLine("=== Bundle GUID Extraction Started ===");
		Logger.Info(LogCategory.Processing, "=== Bundle GUID Extraction Started ===");
		
		// 阶段1：建立完整的资源索引
		int totalAssets = 0;
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			totalAssets++;
			IndexAsset(asset);
		}

		Console.WriteLine($"Indexed {totalAssets} assets");
		Console.WriteLine($"Created {_assetKeyToInfo.Count} asset entries");
		Console.WriteLine($"Found {_groupIdToAssetKeys.Count} unique GroupIds");
		
		Logger.Info(LogCategory.Processing, $"Indexed {totalAssets} assets");
		Logger.Info(LogCategory.Processing, $"Created {_assetKeyToInfo.Count} asset entries");
		Logger.Info(LogCategory.Processing, $"Found {_groupIdToAssetKeys.Count} unique GroupIds");

		// 打印一些示例GroupId用于调试
		int sampleCount = 0;
		foreach (var kvp in _groupIdToAssetKeys)
		{
			if (sampleCount++ < 10)
			{
				Console.WriteLine($"  Sample GroupId: {kvp.Key} ({kvp.Value.Count} assets)");
			}
		}

		// 阶段2：扫描 leveldata 建立 AssetReference GUID → GroupId 映射
		int leveldataCount = 0;
		int assetRefCount = 0;
		int leveldataScanned = 0;
		
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			// 查找 ScriptableObject 类型的 leveldata
			if (asset.ClassName == "MonoBehaviour" || asset.ClassName.Contains("ScriptableObject"))
			{
				string? name = GetAssetName(asset);
				string? path = asset.OriginalPath;
				
				// 更宽松的匹配条件
				bool isLevelData = false;
				if (!string.IsNullOrEmpty(name))
				{
					isLevelData = name.Contains("level", StringComparison.OrdinalIgnoreCase) || 
					              name.Contains("Level") || 
					              name.Contains("Tutorial") ||
					              name.Contains("tutorial");
				}
				if (!isLevelData && !string.IsNullOrEmpty(path))
				{
					isLevelData = path.Contains("leveldata", StringComparison.OrdinalIgnoreCase) ||
					              path.Contains("LevelData", StringComparison.OrdinalIgnoreCase);
				}
				
				if (isLevelData)
				{
					leveldataScanned++;
					if (leveldataScanned <= 5)
					{
						Console.WriteLine($"  Scanning leveldata: {name ?? path ?? "unknown"}");
					}
					
					int refs = ExtractAssetReferencesFromLevelData(asset);
					if (refs > 0)
					{
						leveldataCount++;
						assetRefCount += refs;
						
						if (leveldataCount <= 5)
						{
							Console.WriteLine($"    Found {refs} AssetReferences");
						}
					}
				}
			}
		}

		Console.WriteLine($"Scanned {leveldataScanned} potential leveldata files");
		Console.WriteLine($"Extracted from {leveldataCount} files with AssetReferences");
		Console.WriteLine($"Extracted {assetRefCount} AssetReference GUIDs");
		Console.WriteLine($"Built {_assetRefGuidToKey.Count} GUID mappings");
		
		Logger.Info(LogCategory.Processing, $"Scanned {leveldataScanned} potential leveldata files");
		Logger.Info(LogCategory.Processing, $"Extracted from {leveldataCount} files");
		Logger.Info(LogCategory.Processing, $"Extracted {assetRefCount} AssetReference GUIDs");
		Logger.Info(LogCategory.Processing, $"Built {_assetRefGuidToKey.Count} GUID mappings");

		// 打印前几个映射用于调试
		int mappingSample = 0;
		foreach (var kvp in _assetRefGuidToKey)
		{
			if (mappingSample++ < 5)
			{
				if (_assetKeyToInfo.TryGetValue(kvp.Value, out var info))
				{
					Console.WriteLine($"  Sample mapping: {kvp.Key} → {info.Name}");
				}
			}
		}

		Console.WriteLine("=== Bundle GUID Extraction Completed ===");
		Logger.Info(LogCategory.Processing, "=== Bundle GUID Extraction Completed ===");
	}

	private static int _sampleCounter = 0;
	
	private void IndexAsset(IUnityObjectBase asset)
	{
		lock (_lock)
		{
			string key = GetAssetKey(asset);
			
			if (!_assetKeyToInfo.ContainsKey(key))
			{
				string? name = GetAssetName(asset);
				
				// 调试：打印前几个资源的信息
				if (_sampleCounter < 20)
				{
					_sampleCounter++;
					Console.WriteLine($"  Sample asset #{_sampleCounter}:");
					Console.WriteLine($"    ClassName: {asset.ClassName}");
					Console.WriteLine($"    OriginalPath: {asset.OriginalPath ?? "null"}");
					Console.WriteLine($"    OriginalName: {asset.OriginalName ?? "null"}");
					Console.WriteLine($"    Collection: {asset.Collection.Name}");
					Console.WriteLine($"    ExtractedName: {name ?? "null"}");
				}
				
				var info = new AssetInfo
				{
					CollectionGuid = asset.Collection.Guid,
					PathID = asset.PathID,
					Name = name,
					Path = asset.OriginalPath,
					ClassName = asset.ClassName
				};
				
				_assetKeyToInfo[key] = info;

			// 如果有资源名称，建立 GroupId 索引
			if (!string.IsNullOrEmpty(info.Name))
			{
				string groupId = Path.GetFileNameWithoutExtension(info.Name).ToLower();
				if (!string.IsNullOrEmpty(groupId))
				{
					if (!_groupIdToAssetKeys.ContainsKey(groupId))
					{
						_groupIdToAssetKeys[groupId] = new List<string>();
					}
					_groupIdToAssetKeys[groupId].Add(key);
					
				// 调试：记录关键资源
				if (groupId == "fisherman_0" || groupId == "tent_0" || groupId == "boat_0")
				{
					Logger.Info(LogCategory.Processing, $"[Index] Found {groupId}: AssetKey={key}, ClassName={asset.ClassName}, Name={info.Name}");
				}
				}
			}
			}
		}
	}

	private int ExtractAssetReferencesFromLevelData(IUnityObjectBase asset)
	{
		int count = 0;
		string assetName = GetAssetName(asset) ?? "unknown";
		
		try
		{
			Console.WriteLine($"    Extracting from leveldata: {assetName}");
			
			// 尝试将资源转换为 IMonoBehaviour 并加载其结构
			if (asset is IMonoBehaviour monoBehaviour)
			{
				SerializableStructure? structure = monoBehaviour.LoadStructure();
				if (structure != null)
				{
					Console.WriteLine($"      ✓ Loaded SerializableStructure: {structure.Type.Name}");
					Console.WriteLine($"      Found {structure.Fields.Length} fields");
					
					// 打印前几个字段名
					for (int i = 0; i < Math.Min(10, structure.Type.Fields.Count); i++)
					{
						var field = structure.Type.Fields[i];
						Console.WriteLine($"        Field[{i}]: {field.Name} ({field.Type.Type})");
					}
					
					// 尝试特殊处理 LevelDataSO：提取 Stages 和 CollectableDatas 的对应关系
					if (structure.Type.Name == "LevelDataSO")
					{
						count = ExtractLevelDataMappings(structure, asset);
					}
					else
					{
						// 其他类型：扫描所有字段
						count = ScanSerializableStructure(structure, asset);
					}
					
					Console.WriteLine($"      Total refs extracted: {count}");
				}
				else
				{
					Console.WriteLine($"      ✗ Failed to load SerializableStructure");
				}
			}
			else
			{
				Console.WriteLine($"      ✗ Asset is not a MonoBehaviour");
			}
		}
		catch (Exception ex)
		{
			Logger.Verbose(LogCategory.Processing, $"Error scanning asset: {ex.Message}");
			Console.WriteLine($"      Error: {ex.Message}");
		}

		return count;
	}
	
	/// <summary>
	/// 专门处理 LevelDataSO：提取 Stages 的 GroupId 和 CollectableDatas 的 m_AssetGUID 的对应关系
	/// </summary>
	private int ExtractLevelDataMappings(SerializableStructure levelDataStructure, IUnityObjectBase parentAsset)
	{
		int count = 0;
		
		try
		{
			// 提取 Stages 列表中的 GroupId，并打印第一个 Stage 的完整结构
			List<string> groupIds = new List<string>();
			if (levelDataStructure.TryGetField("Stages", out SerializableValue stagesValue))
			{
				if (stagesValue.CValue is IUnityAssetBase[] stagesArray)
				{
					for (int idx = 0; idx < stagesArray.Length; idx++)
					{
						var stageItem = stagesArray[idx];
						if (stageItem is SerializableStructure stageStruct)
						{
							// 打印第一个 Stage 的所有字段，用于调试
							if (idx == 0)
							{
								Console.WriteLine($"        First Stage structure ({stageStruct.Type.Name}):");
								for (int f = 0; f < stageStruct.Type.Fields.Count; f++)
								{
									var field = stageStruct.Type.Fields[f];
									Console.WriteLine($"          [{f}] {field.Name} ({field.Type.Type})");
								}
							}
							
							if (stageStruct.TryGetField("GroupId", out SerializableValue groupIdValue))
							{
								string groupId = groupIdValue.AsString;
								if (!string.IsNullOrEmpty(groupId))
								{
									groupIds.Add(groupId.ToLower());
								}
							}
						}
					}
				}
			}
			
			Console.WriteLine($"        Extracted {groupIds.Count} GroupIds from Stages");
			
			// 提取 CollectableDatas 列表中的 m_AssetGUID，并打印第一个 CollectableData 的完整结构
			List<string> assetGuids = new List<string>();
			if (levelDataStructure.TryGetField("CollectableDatas", out SerializableValue collectableDatasValue))
			{
				if (collectableDatasValue.CValue is IUnityAssetBase[] collectableDatasArray)
				{
					for (int idx = 0; idx < collectableDatasArray.Length; idx++)
					{
						var collectableDataItem = collectableDatasArray[idx];
						if (collectableDataItem is SerializableStructure collectableDataStruct)
						{
							// 打印第一个 CollectableData 的所有字段，用于调试
							if (idx == 0)
							{
								Console.WriteLine($"        First CollectableData structure ({collectableDataStruct.Type.Name}):");
								for (int f = 0; f < collectableDataStruct.Type.Fields.Count; f++)
								{
									var field = collectableDataStruct.Type.Fields[f];
									Console.WriteLine($"          [{f}] {field.Name} ({field.Type.Type})");
								}
							}
							
					// 注意：字段名是 collectableItem (小写 c)
					if (collectableDataStruct.TryGetField("collectableItem", out SerializableValue collectableItemValue))
					{
						if (collectableItemValue.CValue is SerializableStructure collectableItemStruct)
						{
							if (collectableItemStruct.TryGetField("m_AssetGUID", out SerializableValue guidValue))
							{
								string guidString = guidValue.AsString;
								if (!string.IsNullOrEmpty(guidString) && guidString.Length == 32)
								{
									assetGuids.Add(guidString);
									Console.WriteLine($"          Found m_AssetGUID: {guidString}");
								}
							}
						}
					}
						}
					}
				}
			}
			
			Console.WriteLine($"        Extracted {assetGuids.Count} AssetGUIDs from CollectableDatas");
			
			// 建立映射：3个 CollectableData 对应 1个 GroupId
			// 即：CollectableDatas[0..2] → Stages[0].GroupId
			//     CollectableDatas[3..5] → Stages[1].GroupId
			//     ...
			for (int i = 0; i < assetGuids.Count; i++)
			{
				int stageIndex = i / 3; // 每3个CollectableData对应一个Stage
				
				if (stageIndex >= groupIds.Count)
				{
					Console.WriteLine($"          ✗ No matching Stage for CollectableData index {i} (stage index would be {stageIndex}, but only {groupIds.Count} stages exist)");
					continue;
				}
				
				string groupId = groupIds[stageIndex];
				string assetGuid = assetGuids[i];
				
				// 在索引中查找匹配的资源
				if (_groupIdToAssetKeys.TryGetValue(groupId, out var assetKeys))
				{
					// 优先选择 prefab
					string? targetKey = assetKeys.FirstOrDefault(k => 
						_assetKeyToInfo.TryGetValue(k, out var info) && 
						(info.ClassName == "GameObject" || info.Path?.EndsWith(".prefab") == true));
					
					if (targetKey == null)
					{
						targetKey = assetKeys.FirstOrDefault();
					}
					
					if (targetKey != null)
					{
						lock (_lock)
						{
							if (!_assetRefGuidToKey.ContainsKey(assetGuid))
							{
								_assetRefGuidToKey[assetGuid] = targetKey;
								Console.WriteLine($"          ✓ Mapped[{i}]: {assetGuid} → {groupId} (stage {stageIndex}) → {targetKey}");
								count++;
							}
						}
					}
					else
					{
						Console.WriteLine($"          ✗ No AssetKey found for GroupId '{groupId}'");
					}
				}
				else
				{
					Console.WriteLine($"          ✗ GroupId '{groupId}' not in index");
				}
			}
			
			// 也扫描其他字段（BackGroundData 等可能也包含 AssetReference）
			int otherCount = ScanSerializableStructure(levelDataStructure, parentAsset);
			count += otherCount;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"        Error extracting LevelData mappings: {ex.Message}");
		}
		
		return count;
	}
	
	private int ScanSerializableStructure(SerializableStructure structure, IUnityObjectBase parentAsset)
	{
		int count = 0;
		
		for (int i = 0; i < structure.Fields.Length; i++)
		{
			var fieldDef = structure.Type.Fields[i];
			ref SerializableValue fieldValue = ref structure.Fields[i];
			
			// 递归扫描字段值
			count += ScanSerializableValue(ref fieldValue, fieldDef, parentAsset);
		}
		
		return count;
	}
	
	private int ScanSerializableValue(ref SerializableValue value, SerializableType.Field fieldDef, IUnityObjectBase parentAsset)
	{
		int count = 0;
		
		// 检查是否是结构体
		if (value.CValue is SerializableStructure structure)
		{
			// 检查是否是 AssetReference 结构（包含 m_AssetGUID 字段）
			if (structure.ContainsField("m_AssetGUID") && structure.ContainsField("m_SubObjectName"))
			{
				// 这是一个 AssetReference!
				if (structure.TryGetField("m_AssetGUID", out SerializableValue guidField))
				{
					string guidString = guidField.AsString;
					if (!string.IsNullOrEmpty(guidString) && guidString.Length == 32)
					{
						Console.WriteLine($"          Found m_AssetGUID: {guidString}");
						
						// 同时检查父结构是否有 GroupId 字段
						// TODO: 实现 GroupId 查找逻辑
						count++;
					}
				}
			}
			else
			{
				// 普通结构，递归扫描
				count += ScanSerializableStructure(structure, parentAsset);
			}
		}
		// 检查是否是数组
		else if (value.CValue is IUnityAssetBase[] assetArray)
		{
			foreach (var assetItem in assetArray)
			{
				if (assetItem is SerializableStructure itemStructure)
				{
					count += ScanSerializableStructure(itemStructure, parentAsset);
				}
			}
		}
		
		return count;
	}

	private int ScanObjectForAssetReferences(object obj, IUnityObjectBase parentAsset)
	{
		if (obj == null)
			return 0;

		int count = 0;
		Type objType = obj.GetType();

		// 检查是否包含 m_AssetGUID 属性（AssetReference 结构）
		var guidProp = objType.GetProperty("M_AssetGUID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
		if (guidProp != null)
		{
			try
			{
				object? guidValue = guidProp.GetValue(obj);
				if (guidValue is Utf8String utf8Guid)
				{
					string guidString = utf8Guid.String;
					if (!string.IsNullOrEmpty(guidString) && guidString.Length == 32)
					{
						// 找到一个 AssetReference GUID！
						Console.WriteLine($"          Found m_AssetGUID: {guidString}");
						
						// 现在尝试找到对应的 GroupId
						// 检查同一对象是否有 GroupId 属性（在 Stages 列表中）
						var groupIdProp = objType.GetProperty("GroupId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
						if (groupIdProp != null)
						{
							object? groupIdValue = groupIdProp.GetValue(obj);
							if (groupIdValue is Utf8String utf8GroupId)
							{
								string groupId = utf8GroupId.String.ToLower();
								Console.WriteLine($"          Found GroupId: {groupId}");
								
								// 建立 GUID → GroupId → AssetKey 映射
								if (_groupIdToAssetKeys.TryGetValue(groupId, out var assetKeys))
								{
									// 优先选择 prefab
									var prefabKey = assetKeys.FirstOrDefault(k => 
										_assetKeyToInfo.TryGetValue(k, out var info) && 
										(info.ClassName == "GameObject" || info.Path?.EndsWith(".prefab") == true));
									
									string targetKey = prefabKey ?? assetKeys.FirstOrDefault();
									
									if (targetKey != null)
									{
										lock (_lock)
										{
											if (!_assetRefGuidToKey.ContainsKey(guidString))
											{
												_assetRefGuidToKey[guidString] = targetKey;
												Console.WriteLine($"          ✓ Mapped: {guidString} → {groupId} → {targetKey}");
												Logger.Verbose(LogCategory.Processing, 
													$"Mapped AssetRef: {guidString} → {groupId} → {targetKey}");
												count++;
											}
										}
									}
									else
									{
										Console.WriteLine($"          ✗ No AssetKey found for GroupId '{groupId}'");
									}
								}
								else
								{
									Console.WriteLine($"          ✗ GroupId '{groupId}' not in index");
								}
							}
						}
						else
						{
							Console.WriteLine($"          ✗ No GroupId property found on object with m_AssetGUID");
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"          Error processing m_AssetGUID: {ex.Message}");
			}
		}

		// 递归处理集合和复杂对象
		if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
		{
			foreach (object? item in enumerable)
			{
				if (item != null)
				{
					count += ScanObjectForAssetReferences(item, parentAsset);
				}
			}
		}

		return count;
	}

	private static string GetAssetKey(IUnityObjectBase asset)
	{
		return $"{asset.Collection.Guid}_{asset.PathID}";
	}

	private static string? GetAssetName(IUnityObjectBase asset)
	{
		// 优先级1: OriginalPath (最准确)
		if (!string.IsNullOrEmpty(asset.OriginalPath))
		{
			string fileName = Path.GetFileName(asset.OriginalPath);
			if (!string.IsNullOrEmpty(fileName))
				return fileName;
		}

		// 优先级2: OriginalName
		if (!string.IsNullOrEmpty(asset.OriginalName))
			return asset.OriginalName;

		// 优先级3: 尝试多种可能的Name属性名
		try
		{
			var props = asset.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			
			// 查找名为 Name, m_Name, M_Name 的属性
			foreach (var prop in props)
			{
				if (prop.Name == "Name" || prop.Name == "m_Name" || prop.Name == "M_Name" || prop.Name == "Name_C114")
				{
					object? nameValue = prop.GetValue(asset);
					if (nameValue != null)
					{
						// 处理 Utf8String 类型
						if (nameValue is Utf8String utf8Name)
						{
							string name = utf8Name.String;
							if (!string.IsNullOrEmpty(name))
								return name;
						}
						// 处理 string 类型
						else if (nameValue is string strName)
						{
							if (!string.IsNullOrEmpty(strName))
								return strName;
						}
					}
				}
			}
		}
		catch
		{
			// Ignore reflection errors
		}

		// 优先级4: 尝试从 Collection 名称推断
		if (!string.IsNullOrEmpty(asset.Collection.Name))
		{
			// 如果 Collection 名称是有意义的资源名，使用它
			string collectionName = asset.Collection.Name;
			if (!collectionName.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase) &&
			    !collectionName.StartsWith("level", StringComparison.OrdinalIgnoreCase) &&
			    !collectionName.Equals("sharedassets", StringComparison.OrdinalIgnoreCase))
			{
				return collectionName;
			}
		}

		return null;
	}

	/// <summary>
	/// 根据资源查找它在 leveldata 中被引用时使用的 m_AssetGUID
	/// 这是核心方法：让导出的资源使用 leveldata 中的原始 GUID
	/// </summary>
	public static bool TryGetAssetGuidFromLevelData(IUnityObjectBase asset, out UnityGuid guid)
	{
		lock (_lock)
		{
			// 获取资源名称
			string? assetName = GetAssetName(asset);
			if (string.IsNullOrEmpty(assetName))
			{
				guid = default;
				return false;
			}

			// 规范化为 GroupId 格式
			string groupId = Path.GetFileNameWithoutExtension(assetName).ToLower();
			
		// 调试：打印查询信息
		if (groupId == "fisherman_0" || groupId == "tent_0" || groupId == "boat_0")
		{
			Logger.Info(LogCategory.Export, $"[DEBUG] TryGetAssetGuidFromLevelData: AssetName={assetName}, GroupId={groupId}, Mappings={_assetRefGuidToKey.Count}");
		}

		// 简化逻辑：直接基于 GroupId 匹配
		// 遍历所有映射，找到目标 GroupId 匹配的 GUID
		foreach (var kvp in _assetRefGuidToKey)
		{
			string assetRefGuid = kvp.Key;
			string targetAssetKey = kvp.Value;

			// 从 targetAssetKey 对应的资源中获取 GroupId
			if (_assetKeyToInfo.TryGetValue(targetAssetKey, out var targetInfo))
			{
				string? targetName = targetInfo.Name;
				if (!string.IsNullOrEmpty(targetName))
				{
					string targetGroupId = Path.GetFileNameWithoutExtension(targetName).ToLower();
					
					// 如果 GroupId 匹配，说明这个 GUID 应该用于当前资源
					if (targetGroupId == groupId)
					{
						if (TryParseGuid(assetRefGuid, out guid))
						{
							Logger.Info(LogCategory.Export, 
								$"✓ Using leveldata GUID for {assetName} (GroupId: {groupId}): {assetRefGuid}");
							return true;
						}
					}
				}
			}
		}
		
		if (groupId == "fisherman_0" || groupId == "tent_0" || groupId == "boat_0")
		{
			Logger.Info(LogCategory.Export, $"✗ No match found for GroupId: {groupId}");
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
	/// 根据 AssetReference GUID 查找对应的资源 Collection GUID
	/// </summary>
	public static bool TryGetAssetCollectionGuid(string assetRefGuid, out UnityGuid collectionGuid)
	{
		lock (_lock)
		{
			if (_assetRefGuidToKey.TryGetValue(assetRefGuid, out string? assetKey))
			{
				if (_assetKeyToInfo.TryGetValue(assetKey, out var info))
				{
					collectionGuid = info.CollectionGuid;
					return true;
				}
			}
		}

		collectionGuid = default;
		return false;
	}

	/// <summary>
	/// 根据资源名称（如 fisherman_0）查找对应的 Bundle GUID + PathID
	/// </summary>
	public static bool TryFindAssetByName(string name, out AssetInfo? info)
	{
		lock (_lock)
		{
			string groupId = Path.GetFileNameWithoutExtension(name).ToLower();
			if (_groupIdToAssetKeys.TryGetValue(groupId, out var keys))
			{
				// 优先返回 prefab
				var key = keys.FirstOrDefault(k => 
					_assetKeyToInfo.TryGetValue(k, out var i) && 
					(i.ClassName == "GameObject" || i.Path?.EndsWith(".prefab") == true))
					?? keys.FirstOrDefault();

				if (key != null && _assetKeyToInfo.TryGetValue(key, out info))
				{
					return true;
				}
			}
		}

		info = null;
		return false;
	}

	/// <summary>
	/// 导出完整的映射表用于调试
	/// </summary>
	public static void ExportMappings(string outputPath)
	{
		lock (_lock)
		{
			var lines = new List<string>
			{
				"# Bundle GUID Extraction Results",
				"",
				$"## Asset Index ({_assetKeyToInfo.Count} entries)",
				"# Format: (CollectionGuid_PathID) → Name | Path | ClassName",
				""
			};

			foreach (var kvp in _assetKeyToInfo.Take(100)) // 只显示前100个
			{
				var info = kvp.Value;
				lines.Add($"{kvp.Key}\t→\t{info.Name}\t{info.Path}\t{info.ClassName}");
			}

			lines.Add("");
			lines.Add($"## AssetReference Mappings ({_assetRefGuidToKey.Count} entries)");
			lines.Add("# Format: AssetRefGUID → (CollectionGuid_PathID)");
			lines.Add("");

			foreach (var kvp in _assetRefGuidToKey)
			{
				if (_assetKeyToInfo.TryGetValue(kvp.Value, out var info))
				{
					lines.Add($"{kvp.Key}\t→\t{kvp.Value}\t({info.Name})");
				}
				else
				{
					lines.Add($"{kvp.Key}\t→\t{kvp.Value}");
				}
			}

			File.WriteAllLines(outputPath, lines);
		}
	}
}



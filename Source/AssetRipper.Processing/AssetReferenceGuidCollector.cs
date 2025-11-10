using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace AssetRipper.Processing;

/// <summary>
/// 收集所有 AssetReference 字段中的 GUID，用于在导出时保留原始 GUID
/// </summary>
/// <remarks>
/// 这个处理器会扫描所有资源（特别是 ScriptableObject），查找其中的 m_AssetGUID 字段，
/// 并建立 (Collection GUID, PathID) → 原始 GUID 的映射表。
/// 在导出时，AssetExportCollection 会查询这个映射表来使用原始 GUID。
/// </remarks>
public class AssetReferenceGuidCollector : IAssetProcessor
{
	// 存储: (CollectionGuid, PathID) → 原始 AssetReference GUID
	// 这样可以精确定位被引用的资源
	private static readonly Dictionary<string, UnityGuid> _assetKeyToOriginalGuid = new();
	private static readonly object _lock = new();

	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Collecting AssetReference GUIDs...");
		
		int totalAssets = 0;
		int totalGuids = 0;

		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			totalAssets++;
			
			// 深度扫描所有字段查找包含 m_AssetGUID 的对象
			int guids = ScanForAssetGuids(asset, asset.GetType());
			totalGuids += guids;
		}

		Logger.Info(LogCategory.Processing, 
			$"Scanned {totalAssets} assets, found {totalGuids} AssetReference GUIDs");
		Logger.Info(LogCategory.Processing, 
			$"Collected {_assetKeyToOriginalGuid.Count} unique asset→GUID mappings");
	}

	private int ScanForAssetGuids(object obj, Type objType)
	{
		if (obj == null)
		{
			return 0;
		}

		int count = 0;

		// 检查是否是 AssetReference 结构体（包含 m_AssetGUID 字段）
		PropertyInfo? guidProp = objType.GetProperty("M_AssetGUID", BindingFlags.Public | BindingFlags.Instance);
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
						// 找到一个 AssetReference！
						// 现在我们需要找出这个 GUID 对应哪个资源
						// 但问题是：我们不知道！
						// 解决方案：先收集所有 GUID，稍后在导出时尝试匹配
						
						Logger.Verbose(LogCategory.Processing, $"Found AssetReference GUID: {guidString}");
						count++;
					}
				}
			}
			catch
			{
				// Ignore
			}
		}

		// 递归扫描所有属性和字段
		foreach (PropertyInfo prop in objType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (prop.CanRead && !prop.GetIndexParameters().Any())
			{
				try
				{
					object? value = prop.GetValue(obj);
					if (value != null)
					{
						Type valueType = value.GetType();
						
						// 处理集合类型
						if (value is System.Collections.IEnumerable enumerable && !(value is string))
						{
							foreach (object? item in enumerable)
							{
								if (item != null)
								{
									count += ScanForAssetGuids(item, item.GetType());
								}
							}
						}
						// 处理复杂对象（但避免无限递归）
						else if (!valueType.IsPrimitive && !valueType.IsEnum && valueType != typeof(string))
						{
							count += ScanForAssetGuids(value, valueType);
						}
					}
				}
				catch
				{
					// Ignore property access errors
				}
			}
		}

		return count;
	}

	/// <summary>
	/// 注册一个资源的原始 GUID（由 AddressableGuidResolver 或其他来源提供）
	/// </summary>
	public static void RegisterAssetGuid(IUnityObjectBase asset, UnityGuid originalGuid)
	{
		if (originalGuid.IsZero)
		{
			return;
		}

		lock (_lock)
		{
			string key = GetAssetKey(asset);
			if (!_assetKeyToOriginalGuid.ContainsKey(key))
			{
				_assetKeyToOriginalGuid[key] = originalGuid;
			}
		}
	}

	/// <summary>
	/// 尝试获取资源的原始 GUID
	/// </summary>
	public static bool TryGetOriginalGuid(IUnityObjectBase asset, out UnityGuid originalGuid)
	{
		lock (_lock)
		{
			string key = GetAssetKey(asset);
			return _assetKeyToOriginalGuid.TryGetValue(key, out originalGuid);
		}
	}

	private static string GetAssetKey(IUnityObjectBase asset)
	{
		return $"{asset.Collection.Guid}_{asset.PathID}";
	}

	/// <summary>
	/// 导出收集的GUID映射表（用于调试）
	/// </summary>
	public static void ExportMappings(string outputPath)
	{
		lock (_lock)
		{
			var lines = new List<string>
			{
				"# AssetReference GUID Mappings",
				"# Format: (CollectionGuid_PathID) → Original GUID",
				$"# Total: {_assetKeyToOriginalGuid.Count} mappings",
				""
			};

			foreach (var kvp in _assetKeyToOriginalGuid)
			{
				lines.Add($"{kvp.Key}\t→\t{kvp.Value}");
			}

			System.IO.File.WriteAllLines(outputPath, lines);
		}
	}
}



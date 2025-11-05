using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.IO;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.Primitives;

namespace AssetRipper.Assets.Collections;

/// <summary>
/// A collection of assets read from a <see cref="SerializedFile"/>.
/// </summary>
public sealed class SerializedAssetCollection : AssetCollection
{
	private FileIdentifier[]? DependencyIdentifiers { get; set; }

	private SerializedAssetCollection(Bundle bundle) : base(bundle)
	{
	}

	internal void InitializeDependencyList(IDependencyProvider? dependencyProvider)
	{
		if (Dependencies.Count > 1)
		{
			throw new Exception("Dependency list has already been initialized.");
		}
		if (DependencyIdentifiers is not null)
		{
			for (int i = 0; i < DependencyIdentifiers.Length; i++)
			{
				FileIdentifier identifier = DependencyIdentifiers[i];
				AssetCollection? dependency = Bundle.ResolveCollection(identifier);
				if (dependency is null)
				{
					dependencyProvider?.ReportMissingDependency(identifier);
				}
				SetDependency(i + 1, dependency);
			}
			DependencyIdentifiers = null;
		}
	}

	/// <summary>
	/// Creates a <see cref="SerializedAssetCollection"/> from a <see cref="SerializedFile"/>.
	/// </summary>
	/// <remarks>
	/// The new <see cref="SerializedAssetCollection"/> is automatically added to the <paramref name="bundle"/>.
	/// </remarks>
	/// <param name="bundle">The <see cref="Bundle"/> to add this collection to.</param>
	/// <param name="file">The <see cref="SerializedFile"/> from which to make this collection.</param>
	/// <param name="factory">A factory for creating assets.</param>
	/// <param name="defaultVersion">The default version to use if the file does not have a version, ie the version has been stripped.</param>
	/// <returns>The new collection.</returns>
	internal static SerializedAssetCollection FromSerializedFile(Bundle bundle, SerializedFile file, AssetFactoryBase factory, UnityVersion defaultVersion = default)
	{
		UnityVersion version = file.Version.Equals(0, 0, 0) ? defaultVersion : file.Version;
		SerializedAssetCollection collection = new SerializedAssetCollection(bundle)
		{
			Name = file.NameFixed,
			Version = version,
			OriginalVersion = version,
			Platform = file.Platform,
			Flags = file.Flags,
			EndianType = file.EndianType,
			Guid = TryParseGuidFromFileName(file.NameFixed),
		};
		ReadOnlySpan<FileIdentifier> fileDependencies = file.Dependencies;
		if (fileDependencies.Length > 0)
		{
			collection.DependencyIdentifiers = fileDependencies.ToArray();
		}
		ReadData(collection, file, factory);
		return collection;
	}

	/// <summary>
	/// Attempts to parse a GUID from a file name.
	/// </summary>
	/// <remarks>
	/// APK and AssetBundle files often use GUIDs as file names or include them in the name (e.g., "CAB-30b6e6ebf780b304f83e144c61a2e054").
	/// This method extracts the GUID for proper dependency resolution.
	/// </remarks>
	private static UnityGuid TryParseGuidFromFileName(string fileName)
	{
		if (string.IsNullOrEmpty(fileName))
		{
			return default;
		}

		// Try to parse the entire filename as a GUID (32 hex characters)
		if (fileName.Length == 32 && IsHexString(fileName))
		{
			return ParseGuid(fileName);
		}

		// Try to find GUID pattern in filename (e.g., "CAB-<guid>")
		int guidStart = fileName.LastIndexOf('-');
		if (guidStart >= 0 && guidStart + 33 <= fileName.Length)
		{
			string guidPart = fileName.Substring(guidStart + 1, 32);
			if (IsHexString(guidPart))
			{
				return ParseGuid(guidPart);
			}
		}

		// Try to find a 32-character hex string anywhere in the filename
		for (int i = 0; i <= fileName.Length - 32; i++)
		{
			string candidate = fileName.Substring(i, 32);
			if (IsHexString(candidate))
			{
				return ParseGuid(candidate);
			}
		}

		return default;
	}

	private static bool IsHexString(string s)
	{
		foreach (char c in s)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}
		return true;
	}

	private static UnityGuid ParseGuid(string hex)
	{
		// GUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
		// Unity GUID is stored as 4 uint values
		try
		{
			uint a = Convert.ToUInt32(hex.Substring(0, 8), 16);
			uint b = Convert.ToUInt32(hex.Substring(8, 8), 16);
			uint c = Convert.ToUInt32(hex.Substring(16, 8), 16);
			uint d = Convert.ToUInt32(hex.Substring(24, 8), 16);
			return new UnityGuid(a, b, c, d);
		}
		catch
		{
			return default;
		}
	}

	private static void ReadData(SerializedAssetCollection collection, SerializedFile file, AssetFactoryBase factory)
	{
		foreach (ObjectInfo objectInfo in file.Objects)
		{
			SerializedType? type = objectInfo.GetSerializedType(file.Types);
			int classID = objectInfo.TypeID < 0 ? 114 : objectInfo.TypeID;
			AssetInfo assetInfo = new AssetInfo(collection, objectInfo.FileID, classID);
			IUnityObjectBase? asset = factory.ReadAsset(assetInfo, objectInfo.ObjectData, type);
			if (asset is not null)
			{
				collection.AddAsset(asset);
			}
		}
	}
}

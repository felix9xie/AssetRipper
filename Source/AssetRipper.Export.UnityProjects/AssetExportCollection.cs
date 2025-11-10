using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.SourceGenerated.Classes.ClassID_1034;

namespace AssetRipper.Export.UnityProjects;

public class AssetExportCollection<T> : ExportCollection where T : IUnityObjectBase
{
	public AssetExportCollection(IAssetExporter assetExporter, T asset)
	{
		AssetExporter = assetExporter ?? throw new ArgumentNullException(nameof(assetExporter));
		Asset = asset ?? throw new ArgumentNullException(nameof(asset));
		
		// Priority 1: Try to find GUID from BundleGuidExtractor (extracted from leveldata)
		// This gives us the actual m_AssetGUID from AssetReference fields
		GUID = Processing.BundleGuidExtractor.TryGetAssetGuidFromLevelData(asset, out UnityGuid leveldataGuid)
			? leveldataGuid
			// Priority 2: Try Addressable catalog
			: AddressableGuidResolver.TryFindOriginalGuid(asset, out UnityGuid catalogGuid)
				? catalogGuid
				// Priority 3: Generate deterministic GUID
				: GenerateDeterministicGuid(asset);
			
		// Record the mapping for troubleshooting
		AddressableGuidResolver.RecordMapping(asset, GUID);
	}
	
	/// <summary>
	/// Generates a deterministic GUID for an asset based on its path or identifiers.
	/// </summary>
	/// <remarks>
	/// This is essential for preserving Addressable AssetReference GUIDs.
	/// The GUID generation priority:
	/// 1. Use OriginalPath (if available) - ensures same resource name always gets same GUID
	/// 2. Use Collection GUID + PathID - ensures uniqueness based on bundle location
	/// This ensures consistent GUIDs across multiple exports.
	/// </remarks>
	private static UnityGuid GenerateDeterministicGuid(IUnityObjectBase asset)
	{
		// Priority 1: Use OriginalPath if available
		// This is the best option as it's based on the actual resource name/path
		// which is stable across different AssetBundle configurations
		if (!string.IsNullOrEmpty(asset.OriginalPath))
		{
			return GenerateGuidFromPath(asset.OriginalPath);
		}
		
		// Priority 2: Use OriginalName + ClassName as fallback
		// Some assets might have name but not full path
		if (!string.IsNullOrEmpty(asset.OriginalName))
		{
			string identifier = $"{asset.ClassName}/{asset.OriginalName}";
			return GenerateGuidFromPath(identifier);
		}
		
		// Priority 3: Use Collection GUID + PathID
		// This ensures uniqueness but GUID will change if asset moves to different bundle
		UnityGuid collectionGuid = asset.Collection.Guid;
		long pathId = asset.PathID;
		
		if (!collectionGuid.IsZero)
		{
			return GenerateGuidFromCollectionAndPathId(collectionGuid, pathId);
		}
		else
		{
			// Fallback: use collection name and PathID
			string identifier = $"{asset.Collection.Name}/{pathId}";
			return GenerateGuidFromPath(identifier);
		}
	}
	
	private static UnityGuid GenerateGuidFromPath(string path)
	{
		// Normalize path to ensure consistency
		// Convert to lowercase and use forward slashes
		string normalizedPath = path.ToLowerInvariant().Replace('\\', '/');
		
		using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
		{
			byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(normalizedPath);
			byte[] hash = md5.ComputeHash(pathBytes);
			
			uint data0 = BitConverter.ToUInt32(hash, 0);
			uint data1 = BitConverter.ToUInt32(hash, 4);
			uint data2 = BitConverter.ToUInt32(hash, 8);
			uint data3 = BitConverter.ToUInt32(hash, 12);
			
			return new UnityGuid(data0, data1, data2, data3);
		}
	}
	
	private static UnityGuid GenerateGuidFromCollectionAndPathId(UnityGuid collectionGuid, long pathId)
	{
		using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
		{
			byte[] buffer = new byte[16 + 8]; // 16 bytes for GUID + 8 bytes for PathID
			
			// Write collection GUID bytes
			BitConverter.GetBytes(collectionGuid.Data0).CopyTo(buffer, 0);
			BitConverter.GetBytes(collectionGuid.Data1).CopyTo(buffer, 4);
			BitConverter.GetBytes(collectionGuid.Data2).CopyTo(buffer, 8);
			BitConverter.GetBytes(collectionGuid.Data3).CopyTo(buffer, 12);
			
			// Write PathID bytes
			BitConverter.GetBytes(pathId).CopyTo(buffer, 16);
			
			// Compute MD5 hash
			byte[] hash = md5.ComputeHash(buffer);
			
			// Convert hash to UnityGuid
			uint data0 = BitConverter.ToUInt32(hash, 0);
			uint data1 = BitConverter.ToUInt32(hash, 4);
			uint data2 = BitConverter.ToUInt32(hash, 8);
			uint data3 = BitConverter.ToUInt32(hash, 12);
			
			return new UnityGuid(data0, data1, data2, data3);
		}
	}

	public override bool Export(IExportContainer container, string projectDirectory, FileSystem fileSystem)
	{
		string subPath = fileSystem.Path.Join(projectDirectory, FileSystem.FixInvalidPathCharacters(Asset.GetBestDirectory()));
		string fileName = GetUniqueFileName(Asset, subPath, fileSystem);

		fileSystem.Directory.Create(subPath);

		string filePath = fileSystem.Path.Join(subPath, fileName);
		bool result = ExportInner(container, filePath, projectDirectory, fileSystem);
		if (result)
		{
			Meta meta = new Meta(GUID, CreateImporter(container));
			ExportMeta(container, meta, filePath, fileSystem);
			return true;
		}
		return false;
	}

	public override bool Contains(IUnityObjectBase asset)
	{
		return Asset.AssetInfo == asset.AssetInfo;
	}

	public override long GetExportID(IExportContainer container, IUnityObjectBase asset)
	{
		if (asset.AssetInfo == Asset.AssetInfo)
		{
			return ExportIdHandler.GetMainExportID(Asset);
		}
		throw new ArgumentException(null, nameof(asset));
	}

	public override MetaPtr CreateExportPointer(IExportContainer container, IUnityObjectBase asset, bool isLocal)
	{
		long exportID = GetExportID(container, asset);
		return isLocal ?
			new MetaPtr(exportID) :
			new MetaPtr(exportID, GUID, AssetExporter.ToExportType(Asset));
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="container"></param>
	/// <param name="filePath">The full path to the exported asset destination</param>
	/// <param name="dirPath">The full path to the project export directory</param>
	/// <returns>True if export was successful, false otherwise</returns>
	protected virtual bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		return AssetExporter.Export(container, Asset, filePath, fileSystem);
	}

	protected virtual IUnityObjectBase CreateImporter(IExportContainer container)
	{
		INativeFormatImporter importer = NativeFormatImporter.Create(container.File, container.ExportVersion);
		importer.MainObjectFileID = GetExportID(container, Asset);
		if (importer.Has_AssetBundleName_R() && Asset.AssetBundleName is not null)
		{
			importer.AssetBundleName_R = Asset.AssetBundleName;
		}
		return importer;
	}

	public override UnityGuid GUID { get; }
	public override IAssetExporter AssetExporter { get; }
	public override AssetCollection File => Asset.Collection;
	public override IEnumerable<IUnityObjectBase> Assets
	{
		get { yield return Asset; }
	}
	public override string Name => Asset.GetBestName();
	public T Asset { get; }
}

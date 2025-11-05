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
		
		// Generate a deterministic GUID based on the asset's unique identifiers
		// This ensures that the same asset always gets the same GUID across exports
		// which is critical for Addressable AssetReference resolution
		GUID = GenerateDeterministicGuid(asset);
	}
	
	/// <summary>
	/// Generates a deterministic GUID for an asset based on its Collection GUID and PathID.
	/// </summary>
	/// <remarks>
	/// This is essential for preserving Addressable AssetReference GUIDs.
	/// The GUID is generated using a hash of the asset's unique identifiers:
	/// - Collection GUID (identifies the file/bundle)
	/// - PathID (identifies the object within the file)
	/// This ensures the same asset always gets the same GUID.
	/// </remarks>
	private static UnityGuid GenerateDeterministicGuid(IUnityObjectBase asset)
	{
		// Use the asset's Collection GUID and PathID to create a unique identifier
		UnityGuid collectionGuid = asset.Collection.Guid;
		long pathId = asset.PathID;
		
		// If the collection has a GUID, use it to generate a deterministic GUID
		if (!collectionGuid.IsZero)
		{
			// Combine collection GUID and PathID to create a unique hash
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
				
				// Convert hash to UnityGuid (first 16 bytes of hash -> 4 uint32 values)
				uint data0 = BitConverter.ToUInt32(hash, 0);
				uint data1 = BitConverter.ToUInt32(hash, 4);
				uint data2 = BitConverter.ToUInt32(hash, 8);
				uint data3 = BitConverter.ToUInt32(hash, 12);
				
				return new UnityGuid(data0, data1, data2, data3);
			}
		}
		else
		{
			// Fallback: use collection name and PathID if no GUID is available
			string collectionName = asset.Collection.Name;
			using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
			{
				byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(collectionName);
				byte[] pathIdBytes = BitConverter.GetBytes(pathId);
				byte[] buffer = new byte[nameBytes.Length + pathIdBytes.Length];
				
				nameBytes.CopyTo(buffer, 0);
				pathIdBytes.CopyTo(buffer, nameBytes.Length);
				
				byte[] hash = md5.ComputeHash(buffer);
				
				uint data0 = BitConverter.ToUInt32(hash, 0);
				uint data1 = BitConverter.ToUInt32(hash, 4);
				uint data2 = BitConverter.ToUInt32(hash, 8);
				uint data3 = BitConverter.ToUInt32(hash, 12);
				
				return new UnityGuid(data0, data1, data2, data3);
			}
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

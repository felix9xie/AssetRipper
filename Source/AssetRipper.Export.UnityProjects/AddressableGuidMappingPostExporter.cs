using AssetRipper.Export.Configuration;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// PostExporter 用于导出 Addressable GUID 映射记录
/// </summary>
public class AddressableGuidMappingPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string outputPath = Path.Combine(settings.ExportRootPath, "addressable_guid_mapping.json");
		AddressableGuidResolver.ExportMappingRecords(outputPath);
	}
}


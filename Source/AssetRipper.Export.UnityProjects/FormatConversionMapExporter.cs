using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// Post-exporter that saves the format conversion map for Addressable GUID mapping.
/// </summary>
public class FormatConversionMapExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		if (FormatConversionTracker.ConversionCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Exporting format conversion map ({FormatConversionTracker.ConversionCount} conversions)...");
			
			string projectPath = fileSystem.Path.Join(settings.ExportRootPath, "ExportedProject");
			FormatConversionTracker.ExportConversionMap(projectPath, fileSystem);
			
			Logger.Info(LogCategory.Export, "Format conversion map exported successfully.");
		}
		else
		{
			Logger.Info(LogCategory.Export, "No format conversions to export.");
		}
	}
}


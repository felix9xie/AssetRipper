using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure;
using AssetRipper.Processing;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// Post-export processor that fixes GUID references in leveldata files
/// by replacing game's original GUIDs with AssetRipper's exported GUIDs
/// </summary>
public class LeveldataGuidPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "Starting leveldata GUID fix...");
		
		string exportPath = settings.ExportRootPath ?? "";
		if (string.IsNullOrEmpty(exportPath))
		{
			Logger.Warning(LogCategory.Export, "Export path is empty, skipping leveldata GUID fix");
			return;
		}

		// Step 1: Build a mapping of asset names to their exported GUIDs
		var assetNameToGuid = BuildAssetNameToGuidMapping(exportPath, fileSystem);
		Logger.Info(LogCategory.Export, $"Built mapping for {assetNameToGuid.Count} assets");

		// Step 2: Find and process leveldata directory
		string? leveldataPath = FindLeveldataPath(exportPath, fileSystem);
		if (string.IsNullOrEmpty(leveldataPath))
		{
			Logger.Warning(LogCategory.Export, "No leveldata directory found");
			return;
		}

		// Step 3: Process each leveldata file
		int processedFiles = 0;
		int totalReplacements = 0;
		
		var assetFiles = fileSystem.Directory.GetFiles(leveldataPath, "*.asset", SearchOption.AllDirectories);
		foreach (var assetFile in assetFiles)
		{
			try
			{
				int replacements = ProcessLeveldataFile(assetFile, assetNameToGuid, fileSystem);
				if (replacements > 0)
				{
					processedFiles++;
					totalReplacements += replacements;
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to process {assetFile}: {ex.Message}");
			}
		}

		Logger.Info(LogCategory.Export, $"Fixed {totalReplacements} GUID references in {processedFiles} leveldata files");
	}

	/// <summary>
	/// Builds a mapping from asset names (without extension) to their exported GUIDs
	/// </summary>
	private Dictionary<string, string> BuildAssetNameToGuidMapping(string exportPath, FileSystem fileSystem)
	{
		var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		
		// Search for all .meta files
		var metaFiles = fileSystem.Directory.GetFiles(exportPath, "*.meta", SearchOption.AllDirectories);
		
		foreach (var metaFile in metaFiles)
		{
			try
			{
				// Extract asset name (remove .prefab.meta, .asset.meta, etc.)
				string fileName = fileSystem.Path.GetFileName(metaFile);
				string assetName = fileName.Replace(".prefab.meta", "").Replace(".asset.meta", "").Replace(".meta", "");
				
				// Read GUID from meta file
				string content = fileSystem.File.ReadAllText(metaFile);
				var guidMatch = Regex.Match(content, @"guid:\s*([a-f0-9]{32})");
				
				if (guidMatch.Success)
				{
					string guid = guidMatch.Groups[1].Value;
					
					// Use the asset name as key (case-insensitive)
					if (!mapping.ContainsKey(assetName))
					{
						mapping[assetName] = guid;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"Failed to parse {metaFile}: {ex.Message}");
			}
		}
		
		return mapping;
	}

	/// <summary>
	/// Finds the leveldata directory path
	/// </summary>
	private string? FindLeveldataPath(string exportPath, FileSystem fileSystem)
	{
		string[] possiblePaths = new[]
		{
			fileSystem.Path.Join(exportPath, "Assets", "Resources", "leveldata"),
			fileSystem.Path.Join(exportPath, "ExportedProject", "Assets", "Resources", "leveldata")
		};

		foreach (var path in possiblePaths)
		{
			if (fileSystem.Directory.Exists(path))
			{
				Logger.Info(LogCategory.Export, $"Found leveldata directory: {path}");
				return path;
			}
		}

		return null;
	}

	/// <summary>
	/// Processes a single leveldata file, replacing GUIDs based on Stages list
	/// </summary>
	private int ProcessLeveldataFile(string filePath, Dictionary<string, string> assetNameToGuid, FileSystem fileSystem)
	{
		string content = fileSystem.File.ReadAllText(filePath);
		int replacements = 0;
		
		// Strategy: 
		// 1. Extract all GroupIds from Stages section
		// 2. When encountering m_AssetGUID in CollectableDatas, replace with GUIDs from Stages in a round-robin fashion
		// This assumes CollectableDatas items correspond to the Stages resources
		
		var lines = content.Split('\n');
		var newLines = new List<string>();
		
		// Phase 1: Extract Stages GroupIds
		var stageGroupIds = new List<string>();
		bool inStages = false;
		
		foreach (var line in lines)
		{
			if (line.Contains("Stages:"))
			{
				inStages = true;
				continue;
			}
			
			if (inStages)
			{
				// Check if we've exited the Stages section
				if (line.Length > 0 && !line.StartsWith(" ") && !line.StartsWith("\t") && !line.StartsWith("-"))
				{
					inStages = false;
				}
				else
				{
					var groupIdMatch = Regex.Match(line, @"GroupId:\s*(\w+)");
					if (groupIdMatch.Success)
					{
						string groupId = groupIdMatch.Groups[1].Value;
						if (assetNameToGuid.ContainsKey(groupId))
						{
							stageGroupIds.Add(groupId);
						}
					}
				}
			}
		}
		
		Logger.Verbose(LogCategory.Export, $"  Found {stageGroupIds.Count} stage resources: {string.Join(", ", stageGroupIds)}");
		
		// Phase 2: Replace m_AssetGUIDs using round-robin from Stages
		int currentStageIndex = 0;
		bool inCollectableDatas = false;
		
		foreach (var line in lines)
		{
			string newLine = line;
			
			// Detect CollectableDatas section
			if (line.Contains("CollectableDatas:"))
			{
				inCollectableDatas = true;
			}
			else if (inCollectableDatas && line.Length > 0 && !line.StartsWith(" ") && !line.StartsWith("\t") && !line.StartsWith("-"))
			{
				inCollectableDatas = false;
			}
			
			// Replace m_AssetGUID in CollectableDatas
			if (inCollectableDatas && stageGroupIds.Count > 0)
			{
				var guidMatch = Regex.Match(line, @"m_AssetGUID:\s*([a-f0-9]{32})");
				if (guidMatch.Success)
				{
					string oldGuid = guidMatch.Groups[1].Value;
					
					// Use current stage resource in round-robin fashion
					string stageGroupId = stageGroupIds[currentStageIndex % stageGroupIds.Count];
					currentStageIndex++;
					
					if (assetNameToGuid.TryGetValue(stageGroupId, out string? newGuid))
					{
						newLine = line.Replace(oldGuid, newGuid);
						replacements++;
						Logger.Verbose(LogCategory.Export, $"  Replaced {oldGuid} → {newGuid} (using {stageGroupId})");
					}
				}
			}
			
			newLines.Add(newLine);
		}
		
		// Write back if any replacements were made
		if (replacements > 0)
		{
			fileSystem.File.WriteAllText(filePath, string.Join("\n", newLines));
			Logger.Info(LogCategory.Export, $"  Fixed {replacements} GUID references in {Path.GetFileName(filePath)}");
		}
		
		return replacements;
	}
}

using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// Tracks file format conversions during export (e.g., .psb to .png)
/// to help with GUID mapping for Addressable assets.
/// </summary>
public static class FormatConversionTracker
{
	private static readonly Dictionary<string, string> _conversions = new();
	private static readonly object _lock = new();

	/// <summary>
	/// Records a format conversion from original path to exported path.
	/// </summary>
	/// <param name="originalPath">Original asset path (e.g., "Assets/Textures/image.psb")</param>
	/// <param name="exportedPath">Exported asset path (e.g., "Assets/Textures/image.png")</param>
	public static void RecordConversion(string originalPath, string exportedPath)
	{
		lock (_lock)
		{
			_conversions[originalPath] = exportedPath;
		}
	}

	/// <summary>
	/// Exports the conversion map to a JSON file.
	/// </summary>
	public static void ExportConversionMap(string projectDirectory, FileSystem fileSystem)
	{
		lock (_lock)
		{
			if (_conversions.Count == 0)
				return;

			string filePath = fileSystem.Path.Join(projectDirectory, "format_conversions.json");
			
		var jsonOptions = new JsonSerializerOptions
		{
			WriteIndented = true,
			TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine()
		};

	// Use ToString-based fallback for AOT compatibility
	string json;
	try
	{
		json = JsonSerializer.Serialize(_conversions, jsonOptions);
	}
	catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
	{
		// Fallback: manual JSON generation for AOT compatibility
		Console.WriteLine($"[FormatConversionTracker] Using manual JSON fallback due to: {ex.GetType().Name}");
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("{");
		bool first = true;
		foreach (var kvp in _conversions)
		{
			if (!first) sb.AppendLine(",");
			first = false;
			sb.Append($"  \"{kvp.Key}\": \"{kvp.Value}\"");
		}
		sb.AppendLine();
		sb.AppendLine("}");
		json = sb.ToString();
	}
			fileSystem.File.WriteAllText(filePath, json);
			
			// Also write a summary
			string summaryPath = fileSystem.Path.Join(projectDirectory, "format_conversions.txt");
			using StreamWriter writer = new StreamWriter(fileSystem.File.Create(summaryPath));
			writer.WriteLine("# AssetRipper Format Conversion Map");
			writer.WriteLine($"# Total conversions: {_conversions.Count}");
			writer.WriteLine("# This file helps GUID mapping tools locate assets that changed format during export.");
			writer.WriteLine();
			writer.WriteLine("Original Path -> Exported Path");
			writer.WriteLine("=".PadRight(100, '='));
			
			foreach (var kvp in _conversions)
			{
				writer.WriteLine($"{kvp.Key} -> {kvp.Value}");
			}
		}
	}

	/// <summary>
	/// Clears all recorded conversions. Call this at the start of a new export.
	/// </summary>
	public static void Clear()
	{
		lock (_lock)
		{
			_conversions.Clear();
		}
	}

	/// <summary>
	/// Gets the exported path for an original path, or null if no conversion was recorded.
	/// </summary>
	public static string? GetExportedPath(string originalPath)
	{
		lock (_lock)
		{
			return _conversions.TryGetValue(originalPath, out string? exportedPath) ? exportedPath : null;
		}
	}

	public static int ConversionCount
	{
		get
		{
			lock (_lock)
			{
				return _conversions.Count;
			}
		}
	}
}







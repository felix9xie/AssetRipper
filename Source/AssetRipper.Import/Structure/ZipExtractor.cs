using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace AssetRipper.Import.Structure;

internal static class ZipExtractor
{
	private const string ZipExtension = ".zip";
	private const string ApkExtension = ".apk";
	private const string ApksExtension = ".apks";
	private const string ApkPlusExtension = ".apk+";
	private const string ObbExtension = ".obb";
	private const string XapkExtension = ".xapk";
	private const string VpkExtension = ".vpk"; //PS Vita
	private const string IpaExtension = ".ipa"; //iOS App Store Package
	private const uint ZipNormalMagic = 0x04034B50;
	private const uint ZipEmptyMagic = 0x06054B50;
	private const uint ZipSpannedMagic = 0x08074B50;

	public static List<string> Process(IEnumerable<string> paths, FileSystem fileSystem)
	{
		List<string> result = [];
		foreach (string path in paths)
		{
			switch (GetFileExtension(path, fileSystem))
			{
				case ZipExtension:
				case ApkExtension:
				case ObbExtension:
				case VpkExtension:
				case IpaExtension:
					result.Add(ExtractZip(path, fileSystem));
					break;
				case ApksExtension:
				case ApkPlusExtension:
				case XapkExtension:
					result.Add(ExtractXapk(path, fileSystem));
					break;
				default:
					result.Add(path);
					break;
			}
		}
		return result;
	}

	private static string ExtractZip(string zipFilePath, FileSystem fileSystem)
	{
		if (!HasCompatibleMagic(zipFilePath, fileSystem))
		{
			return zipFilePath;
		}

		string outputDirectory = fileSystem.Directory.CreateTemporary();
		DecompressZipArchive(zipFilePath, outputDirectory, fileSystem);
		return outputDirectory;
	}

	private static string ExtractXapk(string xapkFilePath, FileSystem fileSystem)
	{
		if (!HasCompatibleMagic(xapkFilePath, fileSystem))
		{
			Logger.Info(LogCategory.Import, $"XAPK file '{xapkFilePath}' does not have compatible magic number.");
			return xapkFilePath;
		}

		string intermediateDirectory = fileSystem.Directory.CreateTemporary();
		string outputDirectory = fileSystem.Directory.CreateTemporary();
		DecompressZipArchive(xapkFilePath, intermediateDirectory, fileSystem);
		
		// List all files in intermediate directory for debugging
		string[] intermediateFiles = fileSystem.Directory.GetFiles(intermediateDirectory);
		Logger.Info(LogCategory.Import, $"Found {intermediateFiles.Length} files in XAPK:");
		foreach (string file in intermediateFiles)
		{
			string fileName = fileSystem.Path.GetFileName(file);
			string ext = GetFileExtension(file, fileSystem);
			Logger.Info(LogCategory.Import, $"  - {fileName} (extension: {ext})");
		}
		
		int apkCount = 0;
		foreach (string filePath in intermediateFiles)
		{
			if (GetFileExtension(filePath, fileSystem) == ApkExtension)
			{
				apkCount++;
				DecompressZipArchive(filePath, outputDirectory, fileSystem);
			}
		}
		
		Logger.Info(LogCategory.Import, $"Extracted {apkCount} APK file(s) from XAPK to '{outputDirectory}'");
		return outputDirectory;
	}

	private static void DecompressZipArchive(string zipFilePath, string outputDirectory, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Import, $"Decompressing files...{Environment.NewLine}\tFrom: {zipFilePath}{Environment.NewLine}\tTo: {outputDirectory}");
		
		// Use SharpCompress directly with file path instead of stream to avoid potential issues
		using (ZipArchive archive = ZipArchive.Open(zipFilePath))
		{
			int entryCount = archive.Entries.Count();
			int successCount = 0;
			
			Logger.Info(LogCategory.Import, $"Archive contains {entryCount} entries");
			
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				try
				{
					string entryKey = entry.Key ?? "";
					if (entry.IsDirectory)
					{
						// Create directory
						string directoryPath = fileSystem.Path.Join(outputDirectory, entryKey);
						if (!fileSystem.Directory.Exists(directoryPath))
						{
							fileSystem.Directory.Create(directoryPath);
						}
					}
					else
					{
						// Extract file
						string? directory = fileSystem.Path.GetDirectoryName(entryKey);
						if (string.IsNullOrEmpty(directory))
						{
							directory = string.Empty;
						}
						
						string fullDirectory = fileSystem.Path.Join(outputDirectory, directory);
						if (!fileSystem.Directory.Exists(fullDirectory))
						{
							fileSystem.Directory.Create(fullDirectory);
						}
						
						string fileName = fileSystem.Path.GetFileName(entryKey) ?? "";
						fileName = FileSystem.FixInvalidFileNameCharacters(fileName);
						string filePath = fileSystem.Path.Join(fullDirectory, fileName);
						
						using (Stream entryStream = entry.OpenEntryStream())
						using (Stream outputStream = fileSystem.File.Create(filePath))
						{
							entryStream.CopyTo(outputStream);
						}
					}
					successCount++;
				}
				catch (Exception ex)
				{
					Logger.Log(LogType.Error, LogCategory.Import, $"Failed to extract entry '{entry.Key}': {ex.Message}");
				}
			}
			
			Logger.Info(LogCategory.Import, $"Extracted {successCount}/{entryCount} entries to '{outputDirectory}'");
		}
	}

	private static void WriteEntryToDirectory(IReader reader, string outputDirectory, FileSystem fileSystem)
	{
		IEntry entry = reader.Entry;
		string filePath;
		string fullOutputDirectory = fileSystem.Path.GetFullPath(outputDirectory);

		if (!fileSystem.Directory.Exists(fullOutputDirectory))
		{
			throw new ExtractionException($"Directory does not exist to extract to: {fullOutputDirectory}");
		}

		string fileName = fileSystem.Path.GetFileName(entry.Key ?? throw new NullReferenceException("Entry Key is null")) ?? throw new NullReferenceException("File is null");
		fileName = FileSystem.FixInvalidFileNameCharacters(fileName);

		string? directory = fileSystem.Path.GetDirectoryName(entry.Key ?? throw new NullReferenceException("Entry Key is null"));
		// Handle root directory files (directory can be null or empty string)
		if (string.IsNullOrEmpty(directory))
		{
			directory = string.Empty;
		}
		string fullDirectory = fileSystem.Path.GetFullPath(fileSystem.Path.Join(fullOutputDirectory, directory));

		if (!fileSystem.Directory.Exists(fullDirectory))
		{
			if (!fullDirectory.StartsWith(fullOutputDirectory, StringComparison.Ordinal))
			{
				throw new ExtractionException("Entry is trying to create a directory outside of the destination directory.");
			}

			fileSystem.Directory.Create(fullDirectory);
		}
		filePath = fileSystem.Path.Join(fullDirectory, fileName);

		if (!entry.IsDirectory)
		{
			filePath = fileSystem.Path.GetFullPath(filePath);

			if (!filePath.StartsWith(fullOutputDirectory,StringComparison.Ordinal))
			{
				throw new ExtractionException("Entry is trying to write a file outside of the destination directory.");
			}

			using Stream stream = fileSystem.File.Create(filePath);
			reader.WriteEntryTo(stream);
		}
		else if (!fileSystem.Directory.Exists(filePath))
		{
			fileSystem.Directory.Create(filePath);
		}
	}

	private static string? GetFileExtension(string path, FileSystem fileSystem)
	{
		if (fileSystem.File.Exists(path))
		{
			return fileSystem.Path.GetExtension(path);
		}
		else
		{
			return null;
		}
	}

	private static bool HasCompatibleMagic(string path, FileSystem fileSystem)
	{
		uint magic = GetMagicNumber(path, fileSystem);
		return magic == ZipNormalMagic || magic == ZipEmptyMagic || magic == ZipSpannedMagic;
	}

	private static uint GetMagicNumber(string path, FileSystem fileSystem)
	{
		using Stream stream = fileSystem.File.OpenRead(path);
		return new BinaryReader(stream).ReadUInt32();
	}
}

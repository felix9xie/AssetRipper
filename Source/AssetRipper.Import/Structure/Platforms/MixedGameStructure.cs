using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams;

namespace AssetRipper.Import.Structure.Platforms;

public sealed class MixedGameStructure : PlatformGameStructure
{
	public MixedGameStructure(IEnumerable<string> paths, FileSystem fileSystem) : base(fileSystem)
	{
		HashSet<string> dataPaths = [];
		foreach (string path in SelectUniquePaths(paths))
		{
			if (MultiFileStream.Exists(path, FileSystem))
			{
				string name = MultiFileStream.GetFileName(path);
				AddFile(Files, name, path);
				string directory = FileSystem.Path.GetDirectoryName(path) ?? throw new Exception("Could not get directory name");
				dataPaths.Add(directory);
			}
			else if (FileSystem.Directory.Exists(path))
			{
				CollectFromDirectory(path, Files, Assemblies, dataPaths);
			}
			else
			{
				throw new Exception($"Neither file nor directory at '{path}' exists");
			}
		}

		DataPaths = dataPaths.ToArray();
		Name = Files.Count == 0 ? string.Empty : Files.First().Key;
		GameDataPath = null;
		UnityPlayerPath = null;
		Version = null;

		// Attempt to detect IL2Cpp files in the collected paths
		DetectIl2CppFiles(paths);

		// Determine scripting backend
		if (HasIl2CppFiles())
		{
			Backend = ScriptingBackend.IL2Cpp;
		}
		else if (Assemblies.Count > 0)
		{
			Backend = ScriptingBackend.Mono;
		}
		else
		{
			Backend = ScriptingBackend.Unknown;
		}
	}

	private static IEnumerable<string> SelectUniquePaths(IEnumerable<string> paths)
	{
		return paths.Select(t => MultiFileStream.GetFilePath(t)).Distinct();
	}

	private void CollectFromDirectory(string root, List<KeyValuePair<string, string>> files, Dictionary<string, string> assemblies, ISet<string> dataPaths)
	{
		int count = files.Count;
		CollectSerializedGameFiles(root, files);
		CollectWebFiles(root, files);
		CollectAssetBundles(root, files);
		CollectAssembliesSafe(root, assemblies);
		if (files.Count != count)
		{
			dataPaths.Add(root);
		}

		foreach (string subDirectory in FileSystem.Directory.EnumerateDirectories(root))
		{
			CollectFromDirectory(subDirectory, files, assemblies, dataPaths);
		}
	}

	private void CollectWebFiles(string root, List<KeyValuePair<string, string>> files)
	{
		foreach (string levelFile in FileSystem.Directory.EnumerateFiles(root))
		{
			string extension = FileSystem.Path.GetExtension(levelFile);
			switch (extension)
			{
				case WebGLGameStructure.DataExtension:
				case WebGLGameStructure.DataGzExtension:
					{
						string name = FileSystem.Path.GetFileNameWithoutExtension(levelFile);
						AddFile(files, name, levelFile);
					}
					break;

				case WebGLGameStructure.UnityWebExtension:
					{
						string fileName = FileSystem.Path.GetFileName(levelFile);
						if (fileName.EndsWith(WebGLGameStructure.DataWebExtension, StringComparison.Ordinal))
						{
							string name = fileName.Substring(0, fileName.Length - WebGLGameStructure.DataWebExtension.Length);
							AddFile(files, name, levelFile);
						}
					}
					break;
			}
		}
	}

	private void CollectAssembliesSafe(string root, Dictionary<string, string> assemblies)
	{
		foreach (string file in FileSystem.Directory.EnumerateFiles(root))
		{
			string name = FileSystem.Path.GetFileName(file);
			if (MonoManager.IsMonoAssembly(name))
			{
				if (assemblies.TryGetValue(name, out string? value))
				{
					Logger.Log(LogType.Warning, LogCategory.Import, $"Duplicate assemblies found: '{value}' & '{file}'");
				}
				else
				{
					assemblies.Add(name, file);
				}
			}
		}
	}

	private void DetectIl2CppFiles(IEnumerable<string> paths)
	{
		// Search for IL2Cpp files in all provided paths
		foreach (string path in paths)
		{
			if (!FileSystem.Directory.Exists(path))
			{
				continue;
			}

			// Search for libil2cpp.so recursively
			if (Il2CppGameAssemblyPath == null)
			{
				try
				{
					// Search in lib directories first
					string libPath = FileSystem.Path.Join(path, "lib");
					if (FileSystem.Directory.Exists(libPath))
					{
						Il2CppGameAssemblyPath = FileSystem.Directory.EnumerateFiles(libPath, "libil2cpp.so", SearchOption.AllDirectories).FirstOrDefault();
					}

					// If not found in lib, search the entire path
					if (Il2CppGameAssemblyPath == null)
					{
						Il2CppGameAssemblyPath = FileSystem.Directory.EnumerateFiles(path, "libil2cpp.so", SearchOption.AllDirectories).FirstOrDefault();
					}
				}
				catch (Exception ex)
				{
					Logger.Log(LogType.Warning, LogCategory.Import, $"Error searching for libil2cpp.so in '{path}': {ex.Message}");
				}
			}

			// Search for global-metadata.dat recursively
			if (Il2CppMetaDataPath == null)
			{
				try
				{
					Il2CppMetaDataPath = FileSystem.Directory.EnumerateFiles(path, "global-metadata.dat", SearchOption.AllDirectories).FirstOrDefault();
					
					// If found, also set ManagedPath to its parent directory
					if (Il2CppMetaDataPath != null)
					{
						string? metadataDir = FileSystem.Path.GetDirectoryName(Il2CppMetaDataPath);
						if (metadataDir != null)
						{
							ManagedPath = FileSystem.Path.GetDirectoryName(metadataDir); // Go up one more level from Metadata folder
						}
					}
				}
				catch (Exception ex)
				{
					Logger.Log(LogType.Warning, LogCategory.Import, $"Error searching for global-metadata.dat in '{path}': {ex.Message}");
				}
			}

			// If both files are found, we can stop searching
			if (Il2CppGameAssemblyPath != null && Il2CppMetaDataPath != null)
			{
				Logger.Info(LogCategory.Import, $"IL2Cpp files detected:");
				Logger.Info(LogCategory.Import, $"  libil2cpp.so: {Il2CppGameAssemblyPath}");
				Logger.Info(LogCategory.Import, $"  global-metadata.dat: {Il2CppMetaDataPath}");
				break;
			}
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AssetRipper.Tools
{
    /// <summary>
    /// 工具：将关卡配置中的原始 GUID 映射到导出后的确定性 GUID
    /// </summary>
    public class GuidMappingTool
    {
        private readonly Dictionary<string, string> _guidToPath = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _pathToNewGuid = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _oldGuidToNewGuid = new Dictionary<string, string>();

        public class CatalogData
        {
            [JsonProperty("m_InternalIds")]
            public string[] InternalIds { get; set; }

            [JsonProperty("m_KeyDataString")]
            public string KeyDataString { get; set; }

            [JsonProperty("m_BucketDataString")]
            public string BucketDataString { get; set; }
        }

        /// <summary>
        /// 步骤 1: 从 catalog.json 解析原始 GUID → InternalId 映射
        /// </summary>
        public void LoadCatalogMappings(string catalogPath)
        {
            Console.WriteLine("=== 步骤 1: 解析 Addressable Catalog ===");
            Console.WriteLine($"Catalog: {catalogPath}");
            Console.WriteLine();

            if (!File.Exists(catalogPath))
            {
                throw new FileNotFoundException($"Catalog file not found: {catalogPath}");
            }

            var json = File.ReadAllText(catalogPath);
            var catalog = JsonConvert.DeserializeObject<CatalogData>(json);

            // 解码 KeyDataString
            var keyData = Convert.FromBase64String(catalog.KeyDataString);
            
            // 解码 BucketDataString
            var bucketData = Convert.FromBase64String(catalog.BucketDataString);
            var bucketCount = BitConverter.ToInt32(bucketData, 0);

            Console.WriteLine($"Bucket 数量: {bucketCount}");
            Console.WriteLine("正在解析 GUID 映射...");

            int offset = 4;
            int foundCount = 0;

            for (int i = 0; i < bucketCount; i++)
            {
                var dataOffset = BitConverter.ToInt32(bucketData, offset);
                offset += 4;
                var entryCount = BitConverter.ToInt32(bucketData, offset);
                offset += 4;

                // 读取 key
                var keyType = keyData[dataOffset];
                if (keyType == 0) // ASCII String (GUID)
                {
                    var keyValueOffset = dataOffset + 1;
                    var strLen = BitConverter.ToInt32(keyData, keyValueOffset);
                    keyValueOffset += 4;
                    var strBytes = new byte[strLen];
                    Array.Copy(keyData, keyValueOffset, strBytes, 0, strLen);
                    var guid = Encoding.ASCII.GetString(strBytes);

                    // 只处理看起来像 GUID 的字符串（32 个十六进制字符）
                    if (IsGuid(guid) && entryCount > 0)
                    {
                        // 读取第一个 entry 作为主要资源路径
                        var entryIndex = BitConverter.ToInt32(bucketData, offset);
                        if (entryIndex < catalog.InternalIds.Length)
                        {
                            var internalId = catalog.InternalIds[entryIndex];
                            _guidToPath[guid] = internalId;
                            foundCount++;
                        }
                    }
                }

                offset += (entryCount * 4);
            }

            Console.WriteLine($"✅ 解析完成！找到 {foundCount} 个 GUID 映射");
            Console.WriteLine();
        }

        /// <summary>
        /// 步骤 2: 扫描导出项目，建立资源路径 → 新 GUID 的映射
        /// </summary>
        public void ScanExportedProject(string exportedProjectPath)
        {
            Console.WriteLine("=== 步骤 2: 扫描导出的 Unity 项目 ===");
            Console.WriteLine($"项目路径: {exportedProjectPath}");
            Console.WriteLine();

            if (!Directory.Exists(exportedProjectPath))
            {
                throw new DirectoryNotFoundException($"Exported project not found: {exportedProjectPath}");
            }

            // 扫描所有 .meta 文件
            var metaFiles = Directory.GetFiles(exportedProjectPath, "*.meta", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {metaFiles.Length} 个 .meta 文件");
            Console.WriteLine("正在提取 GUID...");

            int processedCount = 0;
            foreach (var metaFile in metaFiles)
            {
                try
                {
                    var assetFile = metaFile.Substring(0, metaFile.Length - 5); // 移除 .meta
                    if (!File.Exists(assetFile))
                        continue;

                    // 读取 .meta 文件中的 guid
                    var metaContent = File.ReadAllText(metaFile);
                    var guidMatch = Regex.Match(metaContent, @"^guid:\s*([a-f0-9]{32})", RegexOptions.Multiline);
                    
                    if (guidMatch.Success)
                    {
                        var newGuid = guidMatch.Groups[1].Value;
                        var relativePath = GetRelativePath(exportedProjectPath, assetFile);
                        
                        // 标准化路径（使用 / 分隔符）
                        relativePath = relativePath.Replace('\\', '/');
                        
                        _pathToNewGuid[relativePath] = newGuid;
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to process {metaFile}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ 扫描完成！处理了 {processedCount} 个资源");
            Console.WriteLine();
        }

        /// <summary>
        /// 步骤 3: 建立旧 GUID → 新 GUID 的映射
        /// </summary>
        public void BuildGuidMapping()
        {
            Console.WriteLine("=== 步骤 3: 建立 GUID 映射关系 ===");
            Console.WriteLine($"总共需要处理 {_guidToPath.Count} 个 GUID...");
            Console.WriteLine();

            int matchedCount = 0;
            int unmatchedCount = 0;
            int processedCount = 0;
            int totalCount = _guidToPath.Count;
            int lastProgress = 0;

            foreach (var kvp in _guidToPath)
            {
                try
                {
                    var oldGuid = kvp.Key;
                    var internalId = kvp.Value;

                    // 尝试匹配资源路径
                    // internalId 格式可能是：{hash}[{path}]
                    var pathMatch = Regex.Match(internalId, @"\[(.+?)\]");
                    string resourcePath = null;

                    if (pathMatch.Success)
                    {
                        resourcePath = pathMatch.Groups[1].Value;
                    }
                    else
                    {
                        // 可能是直接的路径
                        resourcePath = internalId;
                    }

                    // 尝试在导出的项目中找到对应的资源
                    string newGuid = FindMatchingGuid(resourcePath);
                    
                    if (newGuid != null)
                    {
                        _oldGuidToNewGuid[oldGuid] = newGuid;
                        matchedCount++;
                    }
                    else
                    {
                        unmatchedCount++;
                        if (unmatchedCount <= 10) // 只显示前 10 个未匹配的
                        {
                            Console.WriteLine($"⚠ 未找到匹配: {oldGuid} → {resourcePath}");
                            Console.WriteLine($"  原始 InternalId: {internalId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ 处理 GUID 时出错: {ex.Message}");
                }

                // 显示进度 - 更频繁的更新（每5%）
                processedCount++;
                int progress = (processedCount * 100) / totalCount;
                if (progress >= lastProgress + 5)
                {
                    lastProgress = progress;
                    Console.WriteLine($"  进度: {progress}% ({processedCount}/{totalCount}) - 已匹配: {matchedCount}, 未匹配: {unmatchedCount}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"✅ 映射完成！");
            Console.WriteLine($"  - 成功匹配: {matchedCount} 个");
            Console.WriteLine($"  - 未匹配: {unmatchedCount} 个");
            Console.WriteLine();
        }

        /// <summary>
        /// 步骤 4: 转换关卡配置文件中的 GUID
        /// </summary>
        public void ConvertLevelDataGuids(string levelDataPath, string outputPath = null)
        {
            Console.WriteLine("=== 步骤 4: 转换关卡配置 GUID ===");
            Console.WriteLine($"输入目录: {levelDataPath}");
            
            if (outputPath == null)
            {
                outputPath = levelDataPath + "_converted";
            }
            
            Console.WriteLine($"输出目录: {outputPath}");
            Console.WriteLine();

            if (!Directory.Exists(levelDataPath))
            {
                throw new DirectoryNotFoundException($"Level data directory not found: {levelDataPath}");
            }

            // 创建输出目录
            Directory.CreateDirectory(outputPath);

            // 扫描所有 .asset 文件
            var assetFiles = Directory.GetFiles(levelDataPath, "*.asset", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {assetFiles.Length} 个 .asset 文件");
            Console.WriteLine();

            int convertedFiles = 0;
            int totalReplacements = 0;

            foreach (var assetFile in assetFiles)
            {
                var content = File.ReadAllText(assetFile);
                var originalContent = content;
                int replacements = 0;

                // 查找所有 m_AssetGUID
                var guidPattern = @"m_AssetGUID:\s*([a-f0-9]{32})";
                content = Regex.Replace(content, guidPattern, match =>
                {
                    var oldGuid = match.Groups[1].Value;
                    
                    if (_oldGuidToNewGuid.TryGetValue(oldGuid, out var newGuid))
                    {
                        replacements++;
                        return $"m_AssetGUID: {newGuid}";
                    }
                    
                    return match.Value; // 保持原样
                });

                // 保存转换后的文件
                var relativePath = Path.GetRelativePath(levelDataPath, assetFile);
                var outputFile = Path.Combine(outputPath, relativePath);
                var outputDir = Path.GetDirectoryName(outputFile);
                Directory.CreateDirectory(outputDir);
                
                File.WriteAllText(outputFile, content);

                if (replacements > 0)
                {
                    Console.WriteLine($"✅ {Path.GetFileName(assetFile)}: 替换了 {replacements} 个 GUID");
                    convertedFiles++;
                    totalReplacements += replacements;
                }
                else
                {
                    Console.WriteLine($"  {Path.GetFileName(assetFile)}: 无需转换");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"=== 转换完成！ ===");
            Console.WriteLine($"  - 转换文件数: {convertedFiles}/{assetFiles.Length}");
            Console.WriteLine($"  - 替换 GUID 数: {totalReplacements}");
            Console.WriteLine($"  - 输出目录: {outputPath}");
            Console.WriteLine();
        }

        /// <summary>
        /// 导出映射表到文件
        /// </summary>
        public void ExportMappingTable(string outputPath)
        {
            Console.WriteLine($"导出映射表到: {outputPath}");
            
            var sb = new StringBuilder();
            sb.AppendLine("# GUID Mapping Table");
            sb.AppendLine($"# Generated: {DateTime.Now}");
            sb.AppendLine($"# Total Mappings: {_oldGuidToNewGuid.Count}");
            sb.AppendLine();
            sb.AppendLine("Old GUID\t→\tNew GUID\t→\tResource Path");
            sb.AppendLine("=".PadRight(120, '='));

            foreach (var kvp in _oldGuidToNewGuid.OrderBy(x => x.Key))
            {
                var oldGuid = kvp.Key;
                var newGuid = kvp.Value;
                var path = _guidToPath.TryGetValue(oldGuid, out var p) ? p : "Unknown";
                
                sb.AppendLine($"{oldGuid}\t→\t{newGuid}\t→\t{path}");
            }

            File.WriteAllText(outputPath, sb.ToString());
            Console.WriteLine("✅ 映射表已导出");
            Console.WriteLine();
        }

        // ===== Helper Methods =====

        private string FindMatchingGuid(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;

            // 标准化路径
            resourcePath = resourcePath.Replace('\\', '/').ToLowerInvariant();

            // 1. 尝试精确匹配
            foreach (var kvp in _pathToNewGuid)
            {
                if (kvp.Key.ToLowerInvariant().Equals(resourcePath, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            // 2. 针对 .psb 文件，尝试替换扩展名为 .png
            if (resourcePath.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
            {
                var pngPath = resourcePath.Substring(0, resourcePath.Length - 4) + ".png";
                foreach (var kvp in _pathToNewGuid)
                {
                    if (kvp.Key.ToLowerInvariant().Equals(pngPath, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
                }
            }

            // 3. 尝试文件名匹配
            var fileName = Path.GetFileNameWithoutExtension(resourcePath);
            var matches = _pathToNewGuid.Where(kvp => 
                Path.GetFileNameWithoutExtension(kvp.Key).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
                return matches[0].Value;

            // 4. 尝试部分路径匹配
            matches = _pathToNewGuid.Where(kvp => 
                kvp.Key.ToLowerInvariant().Contains(fileName.ToLowerInvariant()))
                .ToList();

            if (matches.Count == 1)
                return matches[0].Value;

            return null;
        }

        private static bool IsGuid(string str)
        {
            if (string.IsNullOrEmpty(str) || str.Length != 32)
                return false;

            foreach (char c in str)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            
            return Uri.UnescapeDataString(relativeUri.ToString());
        }
    }

}


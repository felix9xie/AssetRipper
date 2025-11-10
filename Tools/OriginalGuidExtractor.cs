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
    /// 从原始游戏文件中提取旧GUID到资源名的映射
    /// </summary>
    public class OriginalGuidExtractor
    {
        private readonly Dictionary<string, string> _oldGuidToAddress = new Dictionary<string, string>();
        
        /// <summary>
        /// 步骤1: 从原始catalog.json解析GUID到address的映射
        /// </summary>
        public void ExtractFromCatalog(string catalogPath)
        {
            Console.WriteLine("=== 从原始Catalog提取GUID映射 ===");
            Console.WriteLine($"Catalog路径: {catalogPath}");
            Console.WriteLine();

            if (!File.Exists(catalogPath))
            {
                throw new FileNotFoundException($"Catalog未找到: {catalogPath}");
            }

            try
            {
                var json = File.ReadAllText(catalogPath);
                var catalog = JsonConvert.DeserializeObject<CatalogData>(json);
                
                if (catalog == null)
                {
                    Console.WriteLine("❌ 无法解析catalog.json");
                    return;
                }

                Console.WriteLine($"✓ Catalog解析成功");
                Console.WriteLine($"  - InternalIds: {catalog.InternalIds?.Length ?? 0}");
                Console.WriteLine($"  - KeyDataString: {(string.IsNullOrEmpty(catalog.KeyDataString) ? "空" : $"{catalog.KeyDataString.Length} 字符")}");
                Console.WriteLine($"  - BucketDataString: {(string.IsNullOrEmpty(catalog.BucketDataString) ? "空" : $"{catalog.BucketDataString.Length} 字符")}");
                Console.WriteLine();

                // 解析KeyDataString获取所有keys (addresses)
                var keys = ParseKeys(catalog.KeyDataString);
                Console.WriteLine($"✓ 解析到 {keys.Length} 个keys");

                // 解析BucketDataString获取bucket到key的映射
                var buckets = ParseBuckets(catalog.BucketDataString, keys.Length);
                Console.WriteLine($"✓ 解析到 {buckets.Count} 个buckets");

                // 从keys中提取GUID
                int guidCount = 0;
                foreach (var key in keys)
                {
                    if (key == null) continue;
                    
                    string keyStr = key.ToString() ?? "";
                    if (string.IsNullOrEmpty(keyStr)) continue;
                    
                    // 尝试从key中提取GUID (32位十六进制字符串)
                    var guidMatches = Regex.Matches(keyStr, @"([0-9a-fA-F]{32})");
                    foreach (Match match in guidMatches)
                    {
                        string guid = match.Groups[1].Value.ToLower();
                        
                        // key本身可能就是address，或者包含address
                        // 尝试提取资源名称（移除GUID和扩展名）
                        string address = ExtractAddressFromKey(keyStr);
                        
                        if (!_oldGuidToAddress.ContainsKey(guid))
                        {
                            _oldGuidToAddress[guid] = address;
                            guidCount++;
                        }
                    }
                }

                Console.WriteLine($"✅ 提取完成！找到 {guidCount} 个GUID映射");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 处理失败: {ex.Message}");
                Console.WriteLine($"   {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 步骤2: 从AssetBundle文件中提取GUID信息
        /// </summary>
        public void ExtractFromBundles(string bundlesPath)
        {
            Console.WriteLine("=== 从AssetBundles提取GUID映射 ===");
            Console.WriteLine($"Bundles目录: {bundlesPath}");
            Console.WriteLine();

            if (!Directory.Exists(bundlesPath))
            {
                throw new DirectoryNotFoundException($"Bundles目录未找到: {bundlesPath}");
            }

            var bundleFiles = Directory.GetFiles(bundlesPath, "*.bundle", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {bundleFiles.Length} 个bundle文件");

            int processedCount = 0;
            foreach (var bundleFile in bundleFiles)
            {
                try
                {
                    // 从bundle文件名中提取可能的GUID和资源名
                    var fileName = Path.GetFileNameWithoutExtension(bundleFile);
                    
                    // bundle文件名格式可能是: <hash>_<resourcename>.bundle 或 <groupname>_assets_<guid>.bundle
                    var parts = fileName.Split('_');
                    
                    // 在文件名中查找GUID
                    foreach (var part in parts)
                    {
                        if (part.Length == 32 && Regex.IsMatch(part, @"^[0-9a-fA-F]{32}$"))
                        {
                            string guid = part.ToLower();
                            
                            // 尝试从其他部分推断资源名
                            string resourceName = string.Join("_", parts.Where(p => p != part && p.Length > 0));
                            
                            if (!string.IsNullOrEmpty(resourceName) && !_oldGuidToAddress.ContainsKey(guid))
                            {
                                _oldGuidToAddress[guid] = resourceName;
                                processedCount++;
                            }
                        }
                    }
                    
                    // 也尝试读取bundle文件内容查找GUID（简单的文本搜索）
                    // 注意：这不是完整的AssetBundle解析，只是尝试找到可能的GUID
                    ExtractGuidsFromBundleContent(bundleFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: 无法处理 {Path.GetFileName(bundleFile)}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ 从bundles提取了 {processedCount} 个额外的GUID映射");
            Console.WriteLine();
        }

        /// <summary>
        /// 步骤3: 导出GUID映射表
        /// </summary>
        public void ExportMapping(string outputPath)
        {
            Console.WriteLine("=== 导出GUID映射表 ===");
            Console.WriteLine($"输出路径: {outputPath}");
            Console.WriteLine();

            var sb = new StringBuilder();
            sb.AppendLine("# 原始游戏GUID到资源名映射");
            sb.AppendLine("# 格式: 旧GUID → 资源名/Address");
            sb.AppendLine($"# 总计: {_oldGuidToAddress.Count} 个映射");
            sb.AppendLine();

            foreach (var kvp in _oldGuidToAddress.OrderBy(x => x.Value))
            {
                sb.AppendLine($"{kvp.Key}\t→\t{kvp.Value}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            Console.WriteLine($"✅ 映射表已导出到: {outputPath}");
            Console.WriteLine($"   包含 {_oldGuidToAddress.Count} 个GUID映射");
            Console.WriteLine();
        }

        /// <summary>
        /// 获取提取的映射
        /// </summary>
        public Dictionary<string, string> GetMapping()
        {
            return new Dictionary<string, string>(_oldGuidToAddress);
        }

        #region 辅助方法

        private object[] ParseKeys(string keyDataString)
        {
            if (string.IsNullOrEmpty(keyDataString))
                return Array.Empty<object>();

            try
            {
                var keyData = Convert.FromBase64String(keyDataString);
                var keyCount = BitConverter.ToInt32(keyData, 0);
                var keys = new object[keyCount];

                int offset = 4;
                for (int i = 0; i < keyCount; i++)
                {
                    if (offset >= keyData.Length)
                        break;

                    // 读取类型 (1 byte)
                    byte objectType = keyData[offset];
                    offset++;

                    // 根据类型读取数据
                    if (objectType == 0) // String
                    {
                        if (offset + 4 > keyData.Length)
                            break;

                        int length = BitConverter.ToInt32(keyData, offset);
                        offset += 4;

                        if (offset + length > keyData.Length)
                            break;

                        var stringValue = Encoding.UTF8.GetString(keyData, offset, length);
                        offset += length;
                        keys[i] = stringValue;
                    }
                    else
                    {
                        // 其他类型暂不支持，跳过
                        keys[i] = $"UnknownType{objectType}";
                    }
                }

                return keys;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: 解析Keys失败: {ex.Message}");
                return Array.Empty<object>();
            }
        }

        private List<int[]> ParseBuckets(string bucketDataString, int keyCount)
        {
            var buckets = new List<int[]>();

            if (string.IsNullOrEmpty(bucketDataString))
                return buckets;

            try
            {
                var bucketData = Convert.FromBase64String(bucketDataString);
                int offset = 0;

                while (offset < bucketData.Length)
                {
                    if (offset + 8 > bucketData.Length)
                        break;

                    int entryCount = BitConverter.ToInt32(bucketData, offset);
                    offset += 4;

                    int[] entries = new int[entryCount];
                    for (int i = 0; i < entryCount; i++)
                    {
                        if (offset + 4 > bucketData.Length)
                            break;

                        entries[i] = BitConverter.ToInt32(bucketData, offset);
                        offset += 4;
                    }

                    buckets.Add(entries);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: 解析Buckets失败: {ex.Message}");
            }

            return buckets;
        }

        private string ExtractAddressFromKey(string key)
        {
            // 移除GUID
            var cleaned = Regex.Replace(key, @"[0-9a-fA-F]{32}", "");
            
            // 移除常见的分隔符和扩展名
            cleaned = cleaned.Replace(".bundle", "")
                           .Replace(".prefab", "")
                           .Replace(".asset", "")
                           .Trim('_', '-', '.');

            return string.IsNullOrWhiteSpace(cleaned) ? key : cleaned;
        }

        private void ExtractGuidsFromBundleContent(string bundleFile)
        {
            try
            {
                // 读取bundle文件的前几KB，查找可能的GUID模式
                using (var fs = File.OpenRead(bundleFile))
                {
                    int readSize = Math.Min(10240, (int)fs.Length); // 读取前10KB
                    byte[] buffer = new byte[readSize];
                    fs.Read(buffer, 0, readSize);

                    // 转换为字符串（可能包含二进制数据）
                    string content = Encoding.ASCII.GetString(buffer);

                    // 查找GUID模式
                    var guidMatches = Regex.Matches(content, @"([0-9a-fA-F]{32})");
                    
                    foreach (Match match in guidMatches.Cast<Match>().Take(5)) // 限制每个bundle最多5个GUID
                    {
                        string guid = match.Groups[1].Value.ToLower();
                        
                        if (!_oldGuidToAddress.ContainsKey(guid))
                        {
                            // 从bundle文件名推断资源名
                            string resourceName = Path.GetFileNameWithoutExtension(bundleFile);
                            resourceName = Regex.Replace(resourceName, @"[0-9a-fA-F]{32}", "").Trim('_', '-');
                            
                            if (!string.IsNullOrWhiteSpace(resourceName))
                            {
                                _oldGuidToAddress[guid] = resourceName;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略读取失败
            }
        }

        #endregion

        #region 数据类

        public class CatalogData
        {
            [JsonProperty("m_InternalIds")]
            public string[] InternalIds { get; set; }

            [JsonProperty("m_KeyDataString")]
            public string KeyDataString { get; set; }

            [JsonProperty("m_BucketDataString")]
            public string BucketDataString { get; set; }

            [JsonProperty("m_EntryDataString")]
            public string EntryDataString { get; set; }
        }

        #endregion
    }
}


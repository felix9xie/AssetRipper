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
        // 旧GUID → GroupId的映射（从leveldata中提取）
        private readonly Dictionary<string, string> _oldGuidToGroupId = new Dictionary<string, string>();
        
        // 文件名（包括GroupId） → 新GUID的映射（从导出项目扫描）
        private readonly Dictionary<string, string> _fileNameToNewGuid = new Dictionary<string, string>();
        
        // 完整路径 → 新GUID的映射（用于精确匹配）
        private readonly Dictionary<string, string> _pathToNewGuid = new Dictionary<string, string>();
        
        // 最终映射：旧GUID → 新GUID
        private readonly Dictionary<string, string> _oldGuidToNewGuid = new Dictionary<string, string>();

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

            [JsonProperty("m_ProviderIds")]
            public string[] ProviderIds { get; set; }

            [JsonProperty("m_ResourceTypes")]
            public ResourceType[] ResourceTypes { get; set; }
        }

        public class ResourceType
        {
            [JsonProperty("m_AssemblyName")]
            public string AssemblyName { get; set; }

            [JsonProperty("m_ClassName")]
            public string ClassName { get; set; }
        }

        /// <summary>
        /// 步骤 1: 从leveldata提取旧GUID和GroupId的关联
        /// </summary>
        public void ExtractGuidGroupIdPairs(string levelDataPath)
        {
            Console.WriteLine("=== 步骤 1: 从LevelData提取GUID-GroupId关联 ===");
            Console.WriteLine($"关卡数据目录: {levelDataPath}");
            Console.WriteLine();

            var files = Directory.GetFiles(levelDataPath, "*.asset", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {files.Length} 个关卡文件");

            int totalPairs = 0;
            foreach (var file in files)
            {
                var content = File.ReadAllText(file);
                
                // 提取所有 Stages 块（包含 GroupId 列表）
                var stagesMatches = Regex.Matches(content, @"Stages:\s*\n((?:\s*- GroupId:.*\n)+)", RegexOptions.Multiline);
                
                foreach (Match stagesMatch in stagesMatches)
                {
                    // 提取该Stages块的所有GroupId
                    var groupIdMatches = Regex.Matches(stagesMatch.Groups[1].Value, @"GroupId:\s*(\S+)");
                    var groupIds = groupIdMatches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                    
                    // 查找该Stages块之后的CollectableDatas（在下一个Stages块之前）
                    int stagesEnd = stagesMatch.Index + stagesMatch.Length;
                    string remainingContent = content.Substring(stagesEnd);
                    
                    // 找到下一个Stages或文件末尾
                    var nextStagesMatch = Regex.Match(remainingContent, @"Stages:");
                    int searchEnd = nextStagesMatch.Success ? nextStagesMatch.Index : remainingContent.Length;
                    string blockContent = remainingContent.Substring(0, searchEnd);
                    
                    // 在这个块中查找m_AssetGUID
                    var guidMatches = Regex.Matches(blockContent, @"m_AssetGUID:\s*([0-9a-fA-F]{32})");
                    
                    // 将所有GUID关联到所有GroupId
                    foreach (Match guidMatch in guidMatches)
                    {
                        string oldGuid = guidMatch.Groups[1].Value.ToLower();
                        
                        foreach (var groupId in groupIds)
                        {
                            if (!_oldGuidToGroupId.ContainsKey(oldGuid))
                            {
                                _oldGuidToGroupId[oldGuid] = groupId;
                                totalPairs++;
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"✅ 提取完成！找到 {_oldGuidToGroupId.Count} 个唯一的旧GUID");
            Console.WriteLine($"   总共 {totalPairs} 个GUID-GroupId关联");
            Console.WriteLine();
        }

        /* 旧的catalog解析方法已废弃
        /// <summary>
        /// 步骤 1: 从 catalog.json 解析原始 GUID → InternalId 映射
        /// </summary>
        public void LoadCatalogMappings_OLD(string catalogPath)
        {
            Console.WriteLine("=== 步骤 1: 解析 Addressable Catalog ===");
            Console.WriteLine($"Catalog: {catalogPath}");
            Console.WriteLine();

            if (!File.Exists(catalogPath))
            {
                throw new FileNotFoundException($"Catalog file not found: {catalogPath}");
            }

            try
            {
                var json = File.ReadAllText(catalogPath);
                Console.WriteLine($"✓ 文件读取成功，大小: {json.Length} 字节");

                var catalog = JsonConvert.DeserializeObject<CatalogData>(json);
                
                if (catalog == null)
                {
                    Console.WriteLine("❌ 反序列化失败: catalog 为 null");
                    return;
                }
                
                Console.WriteLine($"✓ JSON 反序列化成功");
                Console.WriteLine($"  - InternalIds: {catalog.InternalIds?.Length ?? 0}");
                Console.WriteLine($"  - KeyDataString: {(string.IsNullOrEmpty(catalog.KeyDataString) ? "空" : $"{catalog.KeyDataString.Length} 字符")}");
                Console.WriteLine($"  - BucketDataString: {(string.IsNullOrEmpty(catalog.BucketDataString) ? "空" : $"{catalog.BucketDataString.Length} 字符")}");
                Console.WriteLine($"  - EntryDataString: {(string.IsNullOrEmpty(catalog.EntryDataString) ? "空" : $"{catalog.EntryDataString.Length} 字符")}");
                Console.WriteLine();

            // 解码 KeyDataString (base64 → binary → keys)
            var keyData = Convert.FromBase64String(catalog.KeyDataString);
            var keyCount = BitConverter.ToInt32(keyData, 0);
            var keys = new object[keyCount];

            Console.WriteLine($"Key 数量: {keyCount}");

            int keyOffset = 4; // Skip the count
            for (int i = 0; i < keyCount; i++)
            {
                // 根据Unity源代码，格式是：1字节类型 + 数据
                if (keyOffset >= keyData.Length)
                {
                    keys[i] = $"OutOfBounds_at_{keyOffset}";
                    break;
                }
                
                // 读取1字节的类型（不是4字节！）
                byte keyTypeByte = keyData[keyOffset];
                keyOffset++;
                
                try
                {
                    switch (keyTypeByte)
                    {
                        case 0: // AsciiString
                            {
                                var strLength = BitConverter.ToInt32(keyData, keyOffset);
                                keyOffset += 4;
                                keys[i] = Encoding.ASCII.GetString(keyData, keyOffset, strLength);
                                keyOffset += strLength;
                                break;
                            }
                        case 1: // UnicodeString
                            {
                                var strLength = BitConverter.ToInt32(keyData, keyOffset);
                                keyOffset += 4;
                                keys[i] = Encoding.Unicode.GetString(keyData, keyOffset, strLength);
                                keyOffset += strLength;
                                break;
                            }
                        case 2: // UInt16
                            {
                                keys[i] = BitConverter.ToUInt16(keyData, keyOffset);
                                keyOffset += 2;
                                break;
                            }
                        case 3: // UInt32
                            {
                                keys[i] = BitConverter.ToUInt32(keyData, keyOffset);
                                keyOffset += 4;
                                break;
                            }
                        case 4: // Int32
                            {
                                keys[i] = BitConverter.ToInt32(keyData, keyOffset);
                                keyOffset += 4;
                                break;
                            }
                        case 5: // Hash128
                            {
                                var hashLength = keyData[keyOffset];
                                keyOffset++;
                                keys[i] = Encoding.ASCII.GetString(keyData, keyOffset, hashLength);
                                keyOffset += hashLength;
                                break;
                            }
                        case 6: // Type
                            {
                                var typeLength = keyData[keyOffset];
                                keyOffset++;
                                keys[i] = Encoding.ASCII.GetString(keyData, keyOffset, typeLength);
                                keyOffset += typeLength;
                                break;
                            }
                        case 7: // JsonObject - skip for now
                            {
                                var assemblyNameLength = keyData[keyOffset];
                                keyOffset += 1 + assemblyNameLength;
                                var classNameLength = keyData[keyOffset];
                                keyOffset += 1 + classNameLength;
                                var jsonLength = BitConverter.ToInt32(keyData, keyOffset);
                                keyOffset += 4 + jsonLength;
                                keys[i] = $"JsonObject_Skipped";
                                break;
                            }
                        default:
                            keys[i] = $"UnknownType{keyTypeByte}";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    keys[i] = $"ParseError_{ex.Message}";
                    break;
                }
            }

            // Debug: 输出前10个key的内容和统计信息
            int validStringCount = 0;
            int unknownTypeCount = 0;
            int int32Count = 0;
            int guidCount = 0;
            
            for (int i = 0; i < keys.Length; i++)
            {
                var keyStr = keys[i] as string;
                if (keyStr != null)
                {
                    if (keyStr.StartsWith("UnknownType"))
                        unknownTypeCount++;
                    else
                    {
                        validStringCount++;
                        if (IsGuid(keyStr))
                            guidCount++;
                    }
                }
                else if (keys[i] is int)
                {
                    int32Count++;
                }
            }
            
            Console.WriteLine("\n[Debug] Key解析统计:");
            Console.WriteLine($"  总Key数: {keys.Length}");
            Console.WriteLine($"  有效字符串: {validStringCount}");
            Console.WriteLine($"  其中GUID格式: {guidCount}");
            Console.WriteLine($"  Int32类型: {int32Count}");
            Console.WriteLine($"  UnknownType: {unknownTypeCount}");
            Console.WriteLine($"\n前10个Key示例:");
            for (int i = 0; i < Math.Min(10, keys.Length); i++)
            {
                var key = keys[i];
                var keyStr = key as string;
                if (keyStr != null)
                {
                    var preview = keyStr.Length > 60 ? keyStr.Substring(0, 60) + "..." : keyStr;
                    Console.WriteLine($"  [{i}] {preview} (长度:{keyStr.Length})");
                }
                else
                {
                    Console.WriteLine($"  [{i}] {key} (类型:{key?.GetType().Name ?? "null"})");
                }
            }
            Console.WriteLine();

            int foundCount = 0;

            // 方法1: 解码 EntryDataString (base64 → binary → entries) - 获取所有详细条目
            var entryData = Convert.FromBase64String(catalog.EntryDataString);
            int entryCount = BitConverter.ToInt32(entryData, 0);
            int entryOffset = 4;

            Console.WriteLine($"Entry 数量: {entryCount}");
            Console.WriteLine("正在解析 EntryData GUID 映射...");

            const int kBytesPerInt32 = 4;
            int keyRangeFail = 0, notStringFail = 0, internalIdFail = 0, notGuidFail = 0;

            for (int i = 0; i < entryCount; i++)
            {
                // Read entry data (7 integers per entry)
                var internalId = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var providerIndex = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var dependencyKeyIndex = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var depHash = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var dataIndex = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var primaryKey = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                var resourceType = BitConverter.ToInt32(entryData, entryOffset);
                entryOffset += kBytesPerInt32;

                // Debug前5个entry
                if (i < 5)
                {
                    var keyPreview = (primaryKey >= 0 && primaryKey < keys.Length) ? keys[primaryKey] : "OUT_OF_RANGE";
                    Console.WriteLine($"  [Entry{i}] primaryKey={primaryKey}, internalId={internalId}, key={keyPreview}");
                }

                // Extract data
                if (primaryKey >= 0 && primaryKey < keys.Length)
                {
                    var keyObj = keys[primaryKey];
                    if (keyObj is string keyStr)
                    {
                        if (internalId >= 0 && internalId < catalog.InternalIds.Length)
                        {
                            // 尝试从keyStr中提取GUID
                            // 1. 如果keyStr本身就是32字符GUID，直接使用
                            // 2. 否则尝试提取其中的32字符十六进制串
                            string guid = null;
                            
                            if (IsGuid(keyStr))
                            {
                                guid = keyStr.ToLower();
                            }
                            else
                            {
                                // 尝试提取：前32字符、后缀前32字符、或任意连续32字符十六进制
                                if (keyStr.Length >= 32 && IsGuid(keyStr.Substring(0, 32)))
                                {
                                    guid = keyStr.Substring(0, 32).ToLower();
                                }
                                else
                                {
                                    // 使用正则表达式提取32位十六进制GUID
                                    var match = System.Text.RegularExpressions.Regex.Match(keyStr, @"([0-9a-fA-F]{32})");
                                    if (match.Success)
                                    {
                                        guid = match.Groups[1].Value.ToLower();
                                    }
                                }
                            }
                            
                            if (guid != null)
                            {
                                if (!_guidToPath.ContainsKey(guid))
                                {
                                    _guidToPath[guid] = catalog.InternalIds[internalId];
                                    foundCount++;
                                }
                            }
                            else
                            {
                                notGuidFail++;
                            }
                        }
                        else
                        {
                            internalIdFail++;
                        }
                    }
                    else
                    {
                        notStringFail++;
                    }
                }
                else
                {
                    keyRangeFail++;
                }
            }
            
            Console.WriteLine($"  失败统计: keyRange={keyRangeFail}, notString={notStringFail}, internalIdFail={internalIdFail}, notGuid={notGuidFail}");

            Console.WriteLine($"  从 EntryData 找到: {foundCount} 个 GUID");

            // 方法2: 解码 BucketDataString - 获取哈希桶中的 GUID (补充遗漏的)
            var bucketData = Convert.FromBase64String(catalog.BucketDataString);
            var bucketCount = BitConverter.ToInt32(bucketData, 0);

            Console.WriteLine($"Bucket 数量: {bucketCount}");
            Console.WriteLine("正在解析 BucketData GUID 映射（补充）...");

            int bucketFoundCount = 0;
            int offset = 4;

            for (int i = 0; i < bucketCount; i++)
            {
                var dataOffset = BitConverter.ToInt32(bucketData, offset);
                offset += 4;
                var entryCountInBucket = BitConverter.ToInt32(bucketData, offset);
                offset += 4;

                // 读取 key
                if (dataOffset < keyData.Length)
                {
                    var keyType = BitConverter.ToInt32(keyData, dataOffset);
                    if (keyType == 0) // String key (GUID)
                    {
                        var keyValueOffset = dataOffset + 4;
                        if (keyValueOffset + 4 <= keyData.Length)
                        {
                            var strLen = BitConverter.ToInt32(keyData, keyValueOffset);
                            keyValueOffset += 4;
                            if (keyValueOffset + strLen <= keyData.Length)
                            {
                                var strBytes = new byte[strLen];
                                Array.Copy(keyData, keyValueOffset, strBytes, 0, strLen);
                                var guid = Encoding.UTF8.GetString(strBytes);

                                // 只处理看起来像 GUID 的字符串（32 个十六进制字符）
                                if (IsGuid(guid) && entryCountInBucket > 0)
                                {
                                    // 读取第一个 entry 作为主要资源路径
                                    if (offset + 4 <= bucketData.Length)
                                    {
                                        var entryIndex = BitConverter.ToInt32(bucketData, offset);
                                        if (entryIndex >= 0 && entryIndex < catalog.InternalIds.Length)
                                        {
                                            // 只添加 EntryData 中没有的
                                            if (!_guidToPath.ContainsKey(guid))
                                            {
                                                _guidToPath[guid] = catalog.InternalIds[entryIndex];
                                                bucketFoundCount++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                offset += (entryCountInBucket * 4);
            }

                Console.WriteLine($"  从 BucketData 补充: {bucketFoundCount} 个 GUID");
                Console.WriteLine($"✅ 解析完成！总共找到 {foundCount + bucketFoundCount} 个 GUID 映射");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 解析 Catalog 时发生错误:");
                Console.WriteLine($"   类型: {ex.GetType().Name}");
                Console.WriteLine($"   消息: {ex.Message}");
                Console.WriteLine($"   堆栈: {ex.StackTrace}");
                throw;
            }
        }
        */

        /// <summary>
        /// 步骤 3.5: 验证关卡文件 GUID 关联的正确性（在建立映射之后）
        /// </summary>
        public void ValidateLevelDataAssociations(string levelDataPath)
        {
            Console.WriteLine("=== 步骤 3.5: 验证关卡文件 GUID 关联 ===");
            Console.WriteLine($"关卡数据路径: {levelDataPath}");
            Console.WriteLine();

            if (!Directory.Exists(levelDataPath))
            {
                Console.WriteLine("⚠ 关卡数据目录不存在，跳过此步骤");
                Console.WriteLine();
                return;
            }

            var levelFiles = Directory.GetFiles(levelDataPath, "*.asset", SearchOption.AllDirectories);
            Console.WriteLine($"找到 {levelFiles.Length} 个关卡文件");

            int totalValidations = 0;
            int successfulValidations = 0;
            int failedValidations = 0;
            int totalGroupIds = 0;
            int foundGroupIds = 0;
            
            foreach (var levelFile in levelFiles)
            {
                try
                {
                    var content = File.ReadAllText(levelFile);
                    
                    // 提取所有 Stages 中的 GroupId
                    var stageMatches = Regex.Matches(content, @"Stages:\s*((?:\s*-\s*GroupId:\s*\S+\s*)+)", RegexOptions.Multiline);
                    var guidMatches = Regex.Matches(content, @"m_AssetGUID:\s*([a-f0-9]{32})", RegexOptions.Multiline);
                    
                    // 提取 GroupId 列表
                    List<string> groupIds = new List<string>();
                    foreach (Match stageMatch in stageMatches)
                    {
                        var groupIdMatches = Regex.Matches(stageMatch.Groups[1].Value, @"GroupId:\s*(\S+)");
                        foreach (Match gidMatch in groupIdMatches)
                        {
                            groupIds.Add(gidMatch.Groups[1].Value);
                        }
                    }
                    
                    // 提取所有 GUID
                    List<string> guids = new List<string>();
                    foreach (Match guidMatch in guidMatches)
                    {
                        guids.Add(guidMatch.Groups[1].Value);
                    }
                    
                    // 如果没有 GroupId 或 GUID，跳过验证
                    if (groupIds.Count == 0 || guids.Count == 0)
                    {
                        continue;
                    }
                    
                    // 验证：检查 GroupId 中的所有内容是否存在于 m_AssetGUID 关联的资源中
                    totalValidations++;
                    bool allGroupIdsFound = true;
                    List<string> missingGroupIds = new List<string>();
                    
                    foreach (var groupId in groupIds)
                    {
                        totalGroupIds++;
                        bool found = false;
                        
                        // 检查是否在文件名映射中找到匹配GroupId的资源
                        if (_fileNameToNewGuid.ContainsKey(groupId.ToLower()))
                        {
                            found = true;
                            foundGroupIds++;
                        }
                        
                        if (!found)
                        {
                            allGroupIdsFound = false;
                            missingGroupIds.Add(groupId);
                        }
                    }
                    
                    // 输出验证结果
                    if (allGroupIdsFound)
                    {
                        successfulValidations++;
                        // 只在详细模式下显示成功的验证
                        // Console.WriteLine($"✅ {Path.GetFileName(levelFile)}: 验证通过 ({groupIds.Count} 个 GroupId)");
                    }
                    else
                    {
                        failedValidations++;
                        // 只显示前5个失败的详细信息
                        if (failedValidations <= 5)
                        {
                            Console.WriteLine($"❌ {Path.GetFileName(levelFile)}: 验证失败");
                            Console.WriteLine($"   缺失的 GroupId: {string.Join(", ", missingGroupIds.Take(10))}");
                            if (missingGroupIds.Count > 10)
                            {
                                Console.WriteLine($"   ... 还有 {missingGroupIds.Count - 10} 个");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ 处理文件失败 {Path.GetFileName(levelFile)}: {ex.Message}");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine($"=== 验证统计 ===");
            Console.WriteLine($"  总验证数: {totalValidations}");
            Console.WriteLine($"  验证通过: {successfulValidations}");
            Console.WriteLine($"  验证失败: {failedValidations}");
            Console.WriteLine($"  GroupId 总数: {totalGroupIds}");
            Console.WriteLine($"  找到的 GroupId: {foundGroupIds}");
            Console.WriteLine($"  匹配率: {(totalGroupIds > 0 ? (foundGroupIds * 100.0 / totalGroupIds).ToString("F1") : "0")}%");
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
                        
                        // 建立完整路径到GUID的映射
                        _pathToNewGuid[relativePath] = newGuid;
                        
                        // 建立文件名（不含扩展名）到GUID的映射
                        // 优先选择.prefab文件，因为leveldata中的引用通常指向prefab
                        var fileName = Path.GetFileNameWithoutExtension(assetFile).ToLower();
                        var extension = Path.GetExtension(assetFile).ToLower();
                        
                        if (!_fileNameToNewGuid.ContainsKey(fileName))
                        {
                            // 第一次遇到这个文件名，直接添加
                            _fileNameToNewGuid[fileName] = newGuid;
                        }
                        else if (extension == ".prefab")
                        {
                            // 如果是prefab，覆盖之前的映射（prefab优先级最高）
                            _fileNameToNewGuid[fileName] = newGuid;
                        }
                        
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to process {metaFile}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ 扫描完成！处理了 {processedCount} 个资源");
            Console.WriteLine($"   文件名映射: {_fileNameToNewGuid.Count} 个");
            Console.WriteLine();
        }

        /// <summary>
        /// 步骤 3: 基于GroupId建立旧GUID到新GUID的映射
        /// </summary>
        public void BuildGuidMappingFromGroupIds()
        {
            Console.WriteLine("=== 步骤 3: 建立GUID映射关系 ===");
            Console.WriteLine($"需要映射的旧GUID数量: {_oldGuidToGroupId.Count}");
            Console.WriteLine();

            int matchedCount = 0;
            int notFoundCount = 0;

            foreach (var pair in _oldGuidToGroupId)
            {
                string oldGuid = pair.Key;
                string groupId = pair.Value.ToLower();

                // 在文件名映射中查找匹配GroupId的资源
                if (_fileNameToNewGuid.TryGetValue(groupId, out string newGuid))
                {
                    _oldGuidToNewGuid[oldGuid] = newGuid;
                    matchedCount++;
                }
                else
                {
                    notFoundCount++;
                    if (notFoundCount <= 10) // 只显示前10个未匹配的
                    {
                        Console.WriteLine($"  ⚠ 未找到匹配: 旧GUID={oldGuid}, GroupId={groupId}");
                    }
                }
            }

            if (notFoundCount > 10)
            {
                Console.WriteLine($"  ... 还有 {notFoundCount - 10} 个未匹配的GroupId");
            }

            Console.WriteLine();
            Console.WriteLine($"✅ 映射完成！");
            Console.WriteLine($"   成功匹配: {matchedCount} 个");
            Console.WriteLine($"   未找到匹配: {notFoundCount} 个");
            Console.WriteLine($"   匹配率: {(matchedCount * 100.0 / _oldGuidToGroupId.Count):F1}%");
            Console.WriteLine();
        }

        /* 旧的BuildGuidMapping方法已废弃
        /// <summary>
        /// 步骤 3: 建立旧 GUID → 新 GUID 的映射
        /// </summary>
        public void BuildGuidMapping_OLD()
        {
            Console.WriteLine("=== 步骤 3: 建立 GUID 映射关系 ===");
            Console.WriteLine($"总共需要处理 {_guidToPath.Count} 个 GUID...");
            Console.WriteLine();
            
            // 初始化查找缓存以加速查找
            InitializeLookupCache();

            int matchedCount = 0;
            int unmatchedCount = 0;
            int processedCount = 0;
            int totalCount = _guidToPath.Count;
            int lastProgress = 0;

            Console.WriteLine($"总共需要处理 {totalCount} 个 GUID...");
            Console.WriteLine();

            foreach (var oldGuid in _guidToPath.Keys)
            {
                try
                {
                    string resourcePath = null;
                    string internalId = null;
                    
                    // 使用 catalog 中的路径
                    if (_guidToPath.TryGetValue(oldGuid, out internalId))
                    {
                        // 尝试匹配资源路径
                        // internalId 格式可能是：{hash}[{path}]
                        var pathMatch = Regex.Match(internalId, @"\[(.+?)\]");

                        if (pathMatch.Success)
                        {
                            resourcePath = pathMatch.Groups[1].Value;
                        }
                        else
                        {
                            // 可能是直接的路径
                            resourcePath = internalId;
                        }
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
                            if (internalId != null)
                            {
                                Console.WriteLine($"  原始 InternalId: {internalId}");
                            }
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
        */

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
                
                // 尝试找到对应的GroupId和路径
                var groupId = _oldGuidToGroupId.TryGetValue(oldGuid, out var gid) ? gid : "Unknown";
                
                // 尝试从路径映射中找到对应的路径
                var path = _pathToNewGuid.FirstOrDefault(p => p.Value == newGuid).Key ?? $"GroupId:{groupId}";
                
                sb.AppendLine($"{oldGuid}\t→\t{newGuid}\t→\t{path}");
            }

            File.WriteAllText(outputPath, sb.ToString());
            Console.WriteLine("✅ 映射表已导出");
            Console.WriteLine();
        }

        // ===== Helper Methods =====

        // 缓存：小写路径 → 原始路径
        private Dictionary<string, string> _pathLookupCache = new Dictionary<string, string>();
        
        // 缓存：文件名 → 路径列表
        private Dictionary<string, List<string>> _fileNameLookupCache = new Dictionary<string, List<string>>();

        // 初始化查找缓存（在 ScanExportedProject 之后调用）
        private void InitializeLookupCache()
        {
            Console.WriteLine("正在初始化查找缓存...");
            
            foreach (var kvp in _pathToNewGuid)
            {
                var path = kvp.Key;
                var lowerPath = path.ToLowerInvariant();
                
                // 路径缓存
                if (!_pathLookupCache.ContainsKey(lowerPath))
                {
                    _pathLookupCache[lowerPath] = path;
                }
                
                // 文件名缓存
                var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (!_fileNameLookupCache.ContainsKey(fileName))
                {
                    _fileNameLookupCache[fileName] = new List<string>();
                }
                _fileNameLookupCache[fileName].Add(path);
            }
            
            Console.WriteLine($"✅ 缓存初始化完成！路径: {_pathLookupCache.Count}, 文件名: {_fileNameLookupCache.Count}");
        }

        private string FindMatchingGuid(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;

            // 标准化路径
            resourcePath = resourcePath.Replace('\\', '/').ToLowerInvariant();

            // 1. 尝试精确匹配（使用缓存）
            if (_pathLookupCache.TryGetValue(resourcePath, out string exactPath))
            {
                return _pathToNewGuid[exactPath];
            }

            // 2. 格式转换映射已废弃（不再使用catalog）
            // if (_formatConversions.TryGetValue(resourcePath, out string convertedPath))
            // {
            //     var convertedLower = convertedPath.ToLowerInvariant();
            //     if (_pathLookupCache.TryGetValue(convertedLower, out string convertedExactPath))
            //     {
            //         return _pathToNewGuid[convertedExactPath];
            //     }
            // }

            // 3. 如果没有格式转换映射，尝试手动替换 .psb 为 .png（兼容旧版本）
            if (resourcePath.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
            {
                var pngPath = resourcePath.Substring(0, resourcePath.Length - 4) + ".png";
                if (_pathLookupCache.TryGetValue(pngPath, out string pngExactPath))
                {
                    return _pathToNewGuid[pngExactPath];
                }
            }

            // 4. 尝试文件名匹配（使用缓存）
            var fileName = Path.GetFileNameWithoutExtension(resourcePath).ToLowerInvariant();
            if (_fileNameLookupCache.TryGetValue(fileName, out List<string> matches))
            {
                if (matches.Count == 1)
                {
                    return _pathToNewGuid[matches[0]];
                }
            }

            // 5. 尝试部分路径匹配（只在文件名匹配有多个结果时才使用）
            if (matches != null && matches.Count > 1)
            {
                // 尝试找到路径中包含更多匹配部分的
                var pathParts = resourcePath.Split('/');
                var bestMatch = matches
                    .Select(m => new { Path = m, Score = CountMatchingParts(pathParts, m.ToLowerInvariant().Split('/')) })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                
                if (bestMatch != null && bestMatch.Score > 0)
                {
                    return _pathToNewGuid[bestMatch.Path];
                }
            }

            return null;
        }
        
        private int CountMatchingParts(string[] parts1, string[] parts2)
        {
            int score = 0;
            int minLen = Math.Min(parts1.Length, parts2.Length);
            
            // 从后往前比较（文件名最重要）
            for (int i = 1; i <= minLen; i++)
            {
                if (parts1[parts1.Length - i].Equals(parts2[parts2.Length - i], StringComparison.OrdinalIgnoreCase))
                {
                    score += i; // 越靠近文件名的匹配权重越高
                }
            }
            
            return score;
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


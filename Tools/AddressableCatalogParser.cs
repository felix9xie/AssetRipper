using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AssetRipper.Tools
{
    /// <summary>
    /// Tool to parse Addressable catalog.json and extract GUID → InternalId mappings
    /// </summary>
    public class AddressableCatalogParser
    {
        public class CatalogData
        {
            [JsonProperty("m_LocatorId")]
            public string LocatorId { get; set; }

            [JsonProperty("m_BuildResultHash")]
            public string BuildResultHash { get; set; }

            [JsonProperty("m_InternalIds")]
            public string[] InternalIds { get; set; }

            [JsonProperty("m_InternalIdPrefixes")]
            public string[] InternalIdPrefixes { get; set; }

            [JsonProperty("m_KeyDataString")]
            public string KeyDataString { get; set; }

            [JsonProperty("m_BucketDataString")]
            public string BucketDataString { get; set; }

            [JsonProperty("m_EntryDataString")]
            public string EntryDataString { get; set; }

            [JsonProperty("m_ExtraDataString")]
            public string ExtraDataString { get; set; }

            [JsonProperty("m_resourceTypes")]
            public ResourceType[] ResourceTypes { get; set; }

            [JsonProperty("m_ProviderIds")]
            public string[] ProviderIds { get; set; }
        }

        public class ResourceType
        {
            [JsonProperty("m_AssemblyName")]
            public string AssemblyName { get; set; }

            [JsonProperty("m_ClassName")]
            public string ClassName { get; set; }
        }

        public class ResourceEntry
        {
            public string Guid { get; set; }
            public string InternalId { get; set; }
            public string Provider { get; set; }
            public string ResourceType { get; set; }
        }

        /// <summary>
        /// Parse catalog.json and extract GUID mappings
        /// </summary>
        public static Dictionary<string, ResourceEntry> ParseCatalog(string catalogPath)
        {
            var json = File.ReadAllText(catalogPath);
            var catalog = JsonConvert.DeserializeObject<CatalogData>(json);

            var result = new Dictionary<string, ResourceEntry>();

            try
            {
                // Decode KeyDataString (base64 → binary → keys)
                var keyData = Convert.FromBase64String(catalog.KeyDataString);
                var keyCount = BitConverter.ToInt32(keyData, 0);
                var keys = new object[keyCount];

                int keyOffset = 4; // Skip the count
                for (int i = 0; i < keyCount; i++)
                {
                    // Read key type (4 bytes)
                    var keyType = BitConverter.ToInt32(keyData, keyOffset);
                    keyOffset += 4;

                    if (keyType == 0) // String key (GUID)
                    {
                        var strLength = BitConverter.ToInt32(keyData, keyOffset);
                        keyOffset += 4;
                        var strBytes = new byte[strLength];
                        Array.Copy(keyData, keyOffset, strBytes, 0, strLength);
                        keys[i] = Encoding.UTF8.GetString(strBytes);
                        keyOffset += strLength;
                    }
                    else if (keyType == 4) // Int32 key
                    {
                        keys[i] = BitConverter.ToInt32(keyData, keyOffset);
                        keyOffset += 4;
                    }
                    else
                    {
                        // Other key types, skip for now
                        keys[i] = $"UnknownType{keyType}";
                    }
                }

                // Decode EntryDataString (base64 → binary → entries)
                var entryData = Convert.FromBase64String(catalog.EntryDataString);
                int count = BitConverter.ToInt32(entryData, 0);
                int entryOffset = 4;

                const int kBytesPerInt32 = 4;
                const int k_EntryDataItemPerEntry = 7;

                for (int i = 0; i < count; i++)
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

                    // Extract data
                    if (primaryKey >= 0 && primaryKey < keys.Length)
                    {
                        var keyObj = keys[primaryKey];
                        if (keyObj is string keyStr && internalId < catalog.InternalIds.Length)
                        {
                            var entry = new ResourceEntry
                            {
                                Guid = keyStr,
                                InternalId = catalog.InternalIds[internalId],
                                Provider = providerIndex < catalog.ProviderIds.Length ? catalog.ProviderIds[providerIndex] : "Unknown",
                                ResourceType = resourceType < catalog.ResourceTypes.Length ? 
                                    catalog.ResourceTypes[resourceType].ClassName : "Unknown"
                            };

                            // Only add if it looks like a GUID (32 hex characters)
                            if (IsGuid(keyStr))
                            {
                                result[keyStr] = entry;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing catalog: {ex.Message}");
            }

            return result;
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

        /// <summary>
        /// Find GUID in catalog
        /// </summary>
        public static ResourceEntry FindGuid(string catalogPath, string guid)
        {
            var mappings = ParseCatalog(catalogPath);
            return mappings.TryGetValue(guid, out var entry) ? entry : null;
        }

        /// <summary>
        /// Print all GUID mappings
        /// </summary>
        public static void PrintAllMappings(string catalogPath, string outputPath = null)
        {
            var mappings = ParseCatalog(catalogPath);
            var output = new StringBuilder();

            output.AppendLine($"Total GUID mappings: {mappings.Count}");
            output.AppendLine();

            foreach (var kvp in mappings)
            {
                output.AppendLine($"GUID: {kvp.Key}");
                output.AppendLine($"  InternalId: {kvp.Value.InternalId}");
                output.AppendLine($"  Provider: {kvp.Value.Provider}");
                output.AppendLine($"  Type: {kvp.Value.ResourceType}");
                output.AppendLine();
            }

            if (outputPath != null)
            {
                File.WriteAllText(outputPath, output.ToString());
                Console.WriteLine($"Mappings written to: {outputPath}");
            }
            else
            {
                Console.WriteLine(output.ToString());
            }
        }
    }
}


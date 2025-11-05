using System;
using System.IO;

namespace AssetRipper.Tools
{
    /// <summary>
    /// Test program to parse Addressable catalog and find GUID mappings
    /// </summary>
    class TestCatalogParser
    {
        static void Main(string[] args)
        {
            var catalogPath = @"D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\temp\d212\f050f870\assets\aa\catalog.json";
            var targetGuid = "30b6e6ebf780b304f83e144c61a2e054";

            Console.WriteLine("=== Addressable Catalog Parser ===");
            Console.WriteLine($"Catalog: {catalogPath}");
            Console.WriteLine($"Target GUID: {targetGuid}");
            Console.WriteLine();

            if (!File.Exists(catalogPath))
            {
                Console.WriteLine("ERROR: Catalog file not found!");
                return;
            }

            Console.WriteLine("Parsing catalog...");
            var mappings = AddressableCatalogParser.ParseCatalog(catalogPath);
            Console.WriteLine($"Total GUID mappings found: {mappings.Count}");
            Console.WriteLine();

            // Search for target GUID
            Console.WriteLine($"Searching for GUID: {targetGuid}");
            if (mappings.TryGetValue(targetGuid, out var entry))
            {
                Console.WriteLine("✅ FOUND!");
                Console.WriteLine($"  InternalId: {entry.InternalId}");
                Console.WriteLine($"  Provider: {entry.Provider}");
                Console.WriteLine($"  Type: {entry.ResourceType}");
            }
            else
            {
                Console.WriteLine("❌ NOT FOUND");
                Console.WriteLine();
                Console.WriteLine("First 10 GUIDs in catalog:");
                int count = 0;
                foreach (var kvp in mappings)
                {
                    if (count++ >= 10) break;
                    Console.WriteLine($"  {kvp.Key} → {kvp.Value.InternalId}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to export all mappings...");
            Console.ReadKey();

            var outputPath = @"D:\Work\Tools\AssetRipper-master\AssetRipper\catalog_guid_mappings.txt";
            AddressableCatalogParser.PrintAllMappings(catalogPath, outputPath);

            Console.WriteLine();
            Console.WriteLine("Done!");
        }
    }
}


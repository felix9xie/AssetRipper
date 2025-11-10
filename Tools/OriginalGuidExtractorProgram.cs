using System;

namespace AssetRipper.Tools
{
    /// <summary>
    /// 原始GUID提取器的主程序
    /// 用于从原始游戏文件中提取GUID到资源名的映射
    /// </summary>
    class OriginalGuidExtractorProgram
    {
        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   原始游戏GUID提取工具");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                // 配置路径
                string originalGamePath = @"D:\Work\UnPacker\Triple Match City\Triple Match City_2.9.0_APKPure";
                string catalogPath = System.IO.Path.Combine(originalGamePath, @"UnityDataAssetPack\assets\aa\catalog.json");
                string bundlesPath = System.IO.Path.Combine(originalGamePath, @"UnityDataAssetPack\assets\aa\Android");
                string outputPath = @"D:\Work\UnPacker\Triple Match City\original_guid_mapping.txt";

                // 创建提取器
                var extractor = new OriginalGuidExtractor();

                // 步骤1: 从catalog.json提取
                Console.WriteLine("【步骤 1/3】从Catalog提取GUID映射");
                Console.WriteLine();
                extractor.ExtractFromCatalog(catalogPath);

                // 步骤2: 从AssetBundles提取（补充信息）
                Console.WriteLine("【步骤 2/3】从AssetBundles提取额外的GUID映射");
                Console.WriteLine();
                extractor.ExtractFromBundles(bundlesPath);

                // 步骤3: 导出映射表
                Console.WriteLine("【步骤 3/3】导出GUID映射表");
                Console.WriteLine();
                extractor.ExportMapping(outputPath);

                // 显示统计信息
                var mapping = extractor.GetMapping();
                Console.WriteLine("========================================");
                Console.WriteLine("   提取完成!");
                Console.WriteLine("========================================");
                Console.WriteLine($"总GUID数: {mapping.Count}");
                Console.WriteLine($"输出文件: {outputPath}");
                Console.WriteLine();
                Console.WriteLine("接下来:");
                Console.WriteLine("1. 查看输出文件确认映射正确性");
                Console.WriteLine("2. 使用GuidMappingTool结合此映射表进行转换");
                Console.WriteLine();

                // 显示前10个映射示例
                Console.WriteLine("映射示例（前10个）:");
                int count = 0;
                foreach (var kvp in mapping)
                {
                    if (count++ >= 10) break;
                    Console.WriteLine($"  {kvp.Key} → {kvp.Value}");
                }
                if (mapping.Count > 10)
                {
                    Console.WriteLine($"  ... 还有 {mapping.Count - 10} 个映射");
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("   错误");
                Console.WriteLine("========================================");
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("详细信息:");
                Console.WriteLine(ex.ToString());
                Console.WriteLine();
                Environment.Exit(1);
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}


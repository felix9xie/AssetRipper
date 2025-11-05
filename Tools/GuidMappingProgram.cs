using System;
using System.IO;

namespace AssetRipper.Tools
{
    /// <summary>
    /// GUID 映射转换工具的主程序入口
    /// </summary>
    class GuidMappingProgram
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║     Addressable GUID 映射转换工具 v1.0                    ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
                Console.WriteLine();

                // 配置路径
                var catalogPath = @"D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\temp\d212\f050f870\assets\aa\catalog.json";
                // var exportedProjectPath = @"D:\Work\Tools\AssetRipper-master\AssetRipper\Source\0Bins\AssetRipper.GUI.Free\Release\bundle\extractasset\AssetRipper_export_20251105_035302\ExportedProject";
                // var levelDataPath = @"D:\Work\Demo\LevelDataUnpack\Assets\leveldata";
                // var outputPath = @"D:\Work\Demo\LevelDataUnpack\Assets\leveldata_converted";
                // var mappingTablePath = @"D:\Work\Demo\LevelDataUnpack\guid_mapping_table.txt";

                var exportedProjectPath = @"D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251105_035302\ExportedProject";
                var levelDataPath = @"D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251105_035302\ExportedProject\Assets\Resources\leveldata";
                var outputPath = @"D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251105_035302\ExportedProject\Assets\Resources\leveldata_converted";
                var mappingTablePath = @"D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251105_035302\ExportedProject\guid_mapping_table.txt";

                // 检查路径
                if (!File.Exists(catalogPath))
                {
                    Console.WriteLine($"❌ 错误：找不到 catalog 文件");
                    Console.WriteLine($"   {catalogPath}");
                    return;
                }

                if (!Directory.Exists(exportedProjectPath))
                {
                    Console.WriteLine($"❌ 错误：找不到导出的项目目录");
                    Console.WriteLine($"   {exportedProjectPath}");
                    return;
                }

                if (!Directory.Exists(levelDataPath))
                {
                    Console.WriteLine($"❌ 错误：找不到关卡数据目录");
                    Console.WriteLine($"   {levelDataPath}");
                    return;
                }

                // 执行转换
                var tool = new GuidMappingTool();

                // 步骤 1: 加载 catalog 映射
                tool.LoadCatalogMappings(catalogPath);

                // 步骤 2: 扫描导出项目
                tool.ScanExportedProject(exportedProjectPath);

                // 步骤 3: 建立映射关系
                tool.BuildGuidMapping();

                // 步骤 4: 转换关卡配置
                tool.ConvertLevelDataGuids(levelDataPath, outputPath);

                // 导出映射表
                tool.ExportMappingTable(mappingTablePath);

                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                   ✅ 全部完成！                            ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("❌ 发生错误：");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("堆栈跟踪：");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}


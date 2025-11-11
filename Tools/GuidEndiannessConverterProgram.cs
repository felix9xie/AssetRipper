using System;

namespace AssetRipper.Tools
{
	public class GuidEndiannessConverterProgram
	{
		public static void Main(string[] args)
		{
			Console.WriteLine();
			
			// 首先运行测试
			GuidEndiannessConverter.Test();

			Console.WriteLine("开始转换 leveldata 文件...");
			Console.WriteLine();

			// 配置路径（使用最新的导出目录）
			string levelDataPath = @"D:\Work\UnPacker\Triple Match City\AssetRipper_export_20251110_030331\ExportedProject\Assets\Resources\leveldata";

			if (!System.IO.Directory.Exists(levelDataPath))
			{
				Console.WriteLine($"❌ 错误：找不到 leveldata 目录");
				Console.WriteLine($"   {levelDataPath}");
				Console.WriteLine();
				Console.WriteLine("请检查路径是否正确！");
				return;
			}

			// 执行转换（就地转换，直接修改原文件）
			var converter = new GuidEndiannessConverter();
			converter.ConvertLevelDataDirectory(levelDataPath);

			Console.WriteLine("转换完成！");
		}
	}
}


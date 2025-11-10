using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetRipper.Tools
{
	/// <summary>
	/// 将 leveldata 文件中的 GUID 从大端序转换为小端序（Unity .meta 文件格式）
	/// </summary>
	public class GuidEndiannessConverter
	{
		/// <summary>
		/// 将32位十六进制 GUID 从大端序转换为小端序
		/// 例如：662b44a898afe7840a044dcf6bfc8120 → a8442b6684e7af98cf4d040a2081fc6b
		/// </summary>
		private static string ConvertGuidToLittleEndian(string bigEndianGuid)
		{
			if (string.IsNullOrEmpty(bigEndianGuid) || bigEndianGuid.Length != 32)
			{
				return bigEndianGuid;
			}

			// 每8个字符（4字节）作为一组，反转字节序
			string part0 = bigEndianGuid.Substring(0, 8);   // 662b44a8
			string part1 = bigEndianGuid.Substring(8, 8);   // 98afe784
			string part2 = bigEndianGuid.Substring(16, 8);  // 0a044dcf
			string part3 = bigEndianGuid.Substring(24, 8);  // 6bfc8120

			// 反转每个4字节组的字节序
			string result = ReverseBytes(part0) +
			                ReverseBytes(part1) +
			                ReverseBytes(part2) +
			                ReverseBytes(part3);

			return result;
		}

		/// <summary>
		/// 反转字节序：每2个字符（1字节）为单位反转
		/// 例如：662b44a8 → a8442b66
		/// </summary>
		private static string ReverseBytes(string hex)
		{
			if (hex.Length != 8) return hex;

			return hex.Substring(6, 2) + // a8
			       hex.Substring(4, 2) + // 44
			       hex.Substring(2, 2) + // 2b
			       hex.Substring(0, 2);  // 66
		}

		/// <summary>
		/// 转换 leveldata 目录中的所有 .asset 文件的 GUID 格式
		/// </summary>
		public void ConvertLevelDataDirectory(string levelDataPath, string outputPath = null)
		{
			Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
			Console.WriteLine("║     GUID 字节序转换工具 (大端序 → 小端序)                 ║");
			Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
			Console.WriteLine();
			Console.WriteLine($"输入目录: {levelDataPath}");

			if (!Directory.Exists(levelDataPath))
			{
				Console.WriteLine($"❌ 错误：目录不存在！");
				return;
			}

			// 默认输出到同一目录（就地转换）
			if (string.IsNullOrEmpty(outputPath))
			{
				outputPath = levelDataPath;
			}
			Console.WriteLine($"输出目录: {outputPath}");
			Console.WriteLine();

			// 查找所有 .asset 文件
			var assetFiles = Directory.GetFiles(levelDataPath, "*.asset", SearchOption.AllDirectories);
			Console.WriteLine($"找到 {assetFiles.Length} 个 .asset 文件");
			Console.WriteLine();

			int convertedFiles = 0;
			int totalReplacements = 0;

			foreach (var assetFile in assetFiles)
			{
				var content = File.ReadAllText(assetFile);
				int replacements = 0;

				// 查找所有 m_AssetGUID: [32位十六进制]
				var guidPattern = @"m_AssetGUID:\s*([a-f0-9]{32})";
				content = Regex.Replace(content, guidPattern, match =>
				{
					var bigEndianGuid = match.Groups[1].Value;
					var littleEndianGuid = ConvertGuidToLittleEndian(bigEndianGuid);

					if (bigEndianGuid != littleEndianGuid)
					{
						replacements++;
						Console.WriteLine($"  {bigEndianGuid} → {littleEndianGuid}");
					}

					return $"m_AssetGUID: {littleEndianGuid}";
				});

				// 保存转换后的文件
				if (replacements > 0)
				{
					var relativePath = Path.GetRelativePath(levelDataPath, assetFile);
					var outputFile = Path.Combine(outputPath, relativePath);
					var outputDir = Path.GetDirectoryName(outputFile);
					
					if (!string.IsNullOrEmpty(outputDir))
					{
						Directory.CreateDirectory(outputDir);
					}

					File.WriteAllText(outputFile, content);
					Console.WriteLine($"✅ {Path.GetFileName(assetFile)}: 转换了 {replacements} 个 GUID");
					convertedFiles++;
					totalReplacements += replacements;
				}
				else
				{
					Console.WriteLine($"  {Path.GetFileName(assetFile)}: 无需转换");
				}
			}

			Console.WriteLine();
			Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
			Console.WriteLine("║                   ✅ 转换完成！                            ║");
			Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
			Console.WriteLine($"  - 转换文件数: {convertedFiles}/{assetFiles.Length}");
			Console.WriteLine($"  - 转换 GUID 数: {totalReplacements}");
			Console.WriteLine($"  - 输出目录: {outputPath}");
			Console.WriteLine();
		}

		/// <summary>
		/// 测试转换逻辑
		/// </summary>
		public static void Test()
		{
			Console.WriteLine("=== 测试 GUID 字节序转换 ===");
			Console.WriteLine();

			var testCases = new[]
			{
				("662b44a898afe7840a044dcf6bfc8120", "a8442b6684e7af98cf4d040a2081fc6b", "fisherman_0"),
				("7e92e440b4b49fe4b92999e25fd199bc", "40e4927ee49fb4b4e29929b9bc99d15f", "tent_0"),
				("7ad548b4ffbdcd9429eb5d362686ab66", "b448d57a94cdbdff365deb2966ab8626", "boat_0")
			};

			foreach (var (bigEndian, expectedLittleEndian, name) in testCases)
			{
				var actualLittleEndian = ConvertGuidToLittleEndian(bigEndian);
				var match = actualLittleEndian == expectedLittleEndian ? "✅" : "❌";
				
				Console.WriteLine($"{match} {name}:");
				Console.WriteLine($"   输入 (大端): {bigEndian}");
				Console.WriteLine($"   输出 (小端): {actualLittleEndian}");
				Console.WriteLine($"   期望 (小端): {expectedLittleEndian}");
				Console.WriteLine();
			}
		}
	}
}


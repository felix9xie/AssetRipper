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
	/// 将32位十六进制 GUID 转换为 Unity .meta 文件格式
	/// 步骤：字节序反转 + nibble swap
	/// 例如：662b44a898afe7840a044dcf6bfc8120 → 8a44b266487efa89fcd440a00218cfb6
	/// </summary>
	private static string ConvertGuidToLittleEndian(string bigEndianGuid)
	{
		if (string.IsNullOrEmpty(bigEndianGuid) || bigEndianGuid.Length != 32)
		{
			return bigEndianGuid;
		}

		// 步骤1：字节序反转（每4字节反转）
		string ReverseBytes(string hex)
		{
			if (hex.Length != 8) return hex;
			return hex.Substring(6, 2) + hex.Substring(4, 2) + hex.Substring(2, 2) + hex.Substring(0, 2);
		}

		string reversed = ReverseBytes(bigEndianGuid.Substring(0, 8)) +
		                 ReverseBytes(bigEndianGuid.Substring(8, 8)) +
		                 ReverseBytes(bigEndianGuid.Substring(16, 8)) +
		                 ReverseBytes(bigEndianGuid.Substring(24, 8));

		// 步骤2：nibble swap（每两个字符交换）
		char[] chars = reversed.ToCharArray();
		for (int i = 0; i < 32; i += 2)
		{
			char temp = chars[i];
			chars[i] = chars[i + 1];
			chars[i + 1] = temp;
		}

		return new string(chars);
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
			("662b44a898afe7840a044dcf6bfc8120", "8a44b266487efa89fcd440a00218cfb6", "fisherman_0"),
			("7e92e440b4b49fe4b92999e25fd199bc", "044e29e74ef94b4b2e99929bcb991df5", "tent_0"),
			("7ad548b4ffbdcd9429eb5d362686ab66", "4b845da749dcdbff63d5be9266ba6862", "boat_0")
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


using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UniversalConvert.App;
using UniversalConvert.Core.Plugins;

namespace UniversalConvert.Tests
{
    /// <summary>扩展包解压与校验回归测试。</summary>
    internal static class PackageTests
    {
        private const string Manifest = "{\"id\":\"com.test.plugin\",\"name\":\"Test\",\"version\":\"1.0.0\"}";

        public static void RunAll()
        {
            Program.Run("扩展包：正常包可解压并校验", NormalPackage);
            Program.Run("扩展包：缺少 manifest.json 应拒绝", MissingManifest);
            Program.Run("扩展包：越界条目应拒绝", TraversalEntry);
            Program.Run("扩展包：损坏清单读取返回 null", CorruptManifest);
            Program.Run("扩展目录：长标识使用稳定短名称", LongIdDirectory);
        }

        private static void NormalPackage()
        {
            var root = NewRoot();
            var zip = Path.Combine(root, "package.zip");
            var destination = Path.Combine(root, "out");
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    AddText(archive, "manifest.json", Manifest);
                    AddBytes(archive, "plugin.dll", new byte[] { 1, 2, 3 });
                    AddText(archive, "tools\\tool.exe", "x");
                }

                PluginPackage.Extract(zip, destination);
                PluginPackage.ValidateExtractedDirectory(destination);

                Program.True(File.Exists(Path.Combine(destination, "manifest.json")), "应解压 manifest.json");
                Program.True(File.Exists(Path.Combine(destination, "plugin.dll")), "应解压插件 DLL");
                var manifest = PluginPackage.ReadManifest(destination);
                Program.Equal("com.test.plugin", manifest.Id, "应读取清单标识");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void MissingManifest()
        {
            var root = NewRoot();
            var zip = Path.Combine(root, "package.zip");
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    AddBytes(archive, "plugin.dll", new byte[] { 1 });
                }

                var rejected = false;
                try
                {
                    PluginPackage.Extract(zip, Path.Combine(root, "out"));
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Program.True(rejected, "缺少 manifest.json 的包必须被拒绝");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TraversalEntry()
        {
            var root = NewRoot();
            var zip = Path.Combine(root, "package.zip");
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    AddText(archive, "manifest.json", Manifest);
                    AddBytes(archive, "plugin.dll", new byte[] { 1 });
                    AddText(archive, "../evil.txt", "pwn");
                }

                var rejected = false;
                try
                {
                    PluginPackage.Extract(zip, Path.Combine(root, "out"));
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Program.True(rejected, "越界条目必须被拒绝");
                Program.True(!File.Exists(Path.Combine(root, "evil.txt")), "不得在目标目录之外写入文件");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void CorruptManifest()
        {
            var root = NewRoot();
            var directory = Path.Combine(root, "plugin");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "manifest.json"), "{ not json");
                Program.True(PluginPackage.ReadManifest(directory) == null, "损坏清单应返回 null");

                var rejected = false;
                try
                {
                    PluginPackage.ValidateExtractedDirectory(directory);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }
                Program.True(rejected, "损坏清单的目录必须校验失败");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void LongIdDirectory()
        {
            var info = new ExtensionInfo { Id = "com.universalconvert.libreoffice" };
            var directory = System.IO.Path.GetFileName(ExtensionCenter.GetInstallDirectory(info));
            Program.True(directory.Length <= 20, "长标识的安装目录名应缩短到 20 字符以内");
            Program.True(directory.StartsWith("uc-", StringComparison.Ordinal), "长标识应使用 uc- 前缀的稳定短名称");

            var shortInfo = new ExtensionInfo { Id = "com.test.plugin" };
            var shortDirectory = System.IO.Path.GetFileName(ExtensionCenter.GetInstallDirectory(shortInfo));
            Program.Equal("com.test.plugin", shortDirectory, "短标识应直接作为目录名");
        }

        private static void AddText(ZipArchive archive, string name, string text)
        {
            AddBytes(archive, name, Encoding.UTF8.GetBytes(text));
        }

        private static void AddBytes(ZipArchive archive, string name, byte[] data)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using (var stream = entry.Open())
            {
                stream.Write(data, 0, data.Length);
            }
        }

        private static string NewRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "uc_package_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteRoot(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // 测试清理失败不影响断言
            }
        }
    }
}

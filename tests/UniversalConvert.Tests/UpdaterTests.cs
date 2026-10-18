using UniversalConvert.Updater;

namespace UniversalConvert.Tests
{
    /// <summary>独立更新器参数解析。</summary>
    internal static class UpdaterTests
    {
        public static void RunAll()
        {
            Program.Run("更新器：基本参数", Basic);
            Program.Run("更新器：安装包与扩展更新互斥", Conflict);
            Program.Run("更新器：非法参数", Invalid);
        }

        private static void Basic()
        {
            var options = UpdaterOptions.Parse(new[]
            {
                "--parent", "1234", "--app", @"C:\Apps\UniversalConvert.App.exe", "--apply-extensions"
            });
            Program.Equal(1234, options.ParentPid, "父进程 PID 应被解析");
            Program.Equal(@"C:\Apps\UniversalConvert.App.exe", options.AppPath, "应用路径应被解析");
            Program.True(options.ApplyExtensions, "应开启扩展更新");
            Program.True(options.Restart, "默认应重启应用");
            Program.Equal(0, options.Errors.Count, "不应产生错误");
        }

        private static void Conflict()
        {
            var options = UpdaterOptions.Parse(new[]
            {
                "--parent=1", "--app=C:\\App.exe", "--installer=C:\\Setup.exe", "--apply-extensions"
            });
            Program.Equal(1, options.ParentPid, "内联 PID 应被解析");
            Program.Equal(@"C:\Setup.exe", options.InstallerPath, "安装包路径应被解析");
            Program.True(options.Errors.Count > 0, "安装包与扩展更新不能同时执行");
        }

        private static void Invalid()
        {
            var missing = UpdaterOptions.Parse(new[] { "--apply-extensions" });
            Program.True(missing.Errors.Count > 0, "缺少 --app 应报错");

            var badParent = UpdaterOptions.Parse(new[] { "--parent", "0", "--app", "x.exe" });
            Program.True(badParent.Errors.Count > 0, "非法父进程应报错");

            var unknown = UpdaterOptions.Parse(new[] { "--app", "x.exe", "--unknown" });
            Program.True(unknown.Errors.Count > 0, "未知参数应报错");
        }
    }
}

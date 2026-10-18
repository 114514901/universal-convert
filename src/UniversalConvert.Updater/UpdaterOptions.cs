using System;
using System.Collections.Generic;

namespace UniversalConvert.Updater
{
    /// <summary>独立更新器参数。只接受本地路径与本产品约定的开关，不执行任意命令。</summary>
    internal sealed class UpdaterOptions
    {
        public int ParentPid { get; private set; }
        public string AppPath { get; private set; }
        public string InstallerPath { get; private set; }
        public bool ApplyExtensions { get; private set; }
        public bool Restart { get; private set; }
        public List<string> Errors { get; private set; }

        private UpdaterOptions()
        {
            Errors = new List<string>();
            Restart = true;
        }

        public static UpdaterOptions Parse(string[] args)
        {
            var options = new UpdaterOptions();
            if (args == null) return options;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrEmpty(arg)) continue;

                if (arg == "--apply-extensions")
                {
                    options.ApplyExtensions = true;
                    continue;
                }
                if (arg == "--no-restart")
                {
                    options.Restart = false;
                    continue;
                }
                if (arg == "--restart")
                {
                    options.Restart = true;
                    continue;
                }

                string name;
                string inline;
                SplitOption(arg, out name, out inline);
                switch (name.ToLowerInvariant())
                {
                    case "--parent":
                        var parent = TakeValue(args, ref i, name, inline);
                        int pid;
                        if (!int.TryParse(parent, out pid) || pid <= 0)
                        {
                            options.Errors.Add("更新器参数 --parent 必须为正整数进程 ID。");
                        }
                        else
                        {
                            options.ParentPid = pid;
                        }
                        break;
                    case "--app":
                        options.AppPath = TakeValue(args, ref i, name, inline);
                        break;
                    case "--installer":
                        options.InstallerPath = TakeValue(args, ref i, name, inline);
                        break;
                    default:
                        options.Errors.Add("更新器收到未知参数：" + arg);
                        break;
                }
            }

            if (string.IsNullOrEmpty(options.AppPath))
            {
                options.Errors.Add("更新器缺少 --app 参数。");
            }
            if (options.InstallerPath != null && options.InstallerPath.Length == 0)
            {
                options.Errors.Add("更新器参数 --installer 不能为空。");
            }
            if (options.InstallerPath != null && options.ApplyExtensions)
            {
                options.Errors.Add("更新器不能同时执行扩展更新与安装包更新。");
            }
            return options;
        }

        private static string TakeValue(string[] args, ref int index, string name, string inline)
        {
            if (inline != null) return inline;
            if (index + 1 >= args.Length || args[index + 1] == null
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            index++;
            return args[index];
        }

        private static void SplitOption(string arg, out string name, out string inline)
        {
            name = arg;
            inline = null;
            var index = arg.IndexOf('=');
            if (index > 2)
            {
                name = arg.Substring(0, index);
                inline = arg.Substring(index + 1);
            }
        }
    }
}

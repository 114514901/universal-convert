using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalConvert.Cli
{
    /// <summary>命令行子命令。</summary>
    public enum CliCommand
    {
        None,
        Convert,
        Formats,
        Plugins,
        Help,
        Version
    }

    /// <summary>进度显示模式。</summary>
    public enum CliProgressMode
    {
        Auto,
        Always,
        Never
    }

    /// <summary>单个参数覆盖项。</summary>
    public sealed class CliOptionOverride
    {
        public string Key { get; private set; }
        public string Value { get; private set; }

        public CliOptionOverride(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// 命令行语法解析结果。该类型只负责语法与参数组合校验，不访问文件系统。
    /// 错误信息统一使用正式书面语，供程序直接输出。
    /// </summary>
    public sealed class CliOptions
    {
        public CliCommand Command { get; private set; }
        public List<string> Files { get; private set; }
        public List<string> FileLists { get; private set; }
        public List<CliOptionOverride> Overrides { get; private set; }
        public List<string> Errors { get; private set; }

        public string To { get; private set; }
        public string Output { get; private set; }
        public string OutputDir { get; private set; }
        public string Preset { get; private set; }
        public string PluginId { get; private set; }
        public string InputExtension { get; private set; }

        public bool Force { get; private set; }
        public bool Skip { get; private set; }
        public bool DryRun { get; private set; }
        public bool Json { get; private set; }
        public bool Quiet { get; private set; }
        public bool Verbose { get; private set; }
        public bool StopOnError { get; private set; }

        public int Jobs { get; private set; }
        public CliProgressMode Progress { get; private set; }

        private CliOptions()
        {
            Files = new List<string>();
            FileLists = new List<string>();
            Overrides = new List<CliOptionOverride>();
            Errors = new List<string>();
            Jobs = 1;
            Progress = CliProgressMode.Auto;
        }

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            if (args == null || args.Length == 0)
            {
                options.Command = CliCommand.Help;
                return options;
            }

            var command = args[0];
            if (Is(command, "help") || Is(command, "--help") || Is(command, "-h"))
            {
                options.Command = CliCommand.Help;
                return options;
            }
            if (Is(command, "version") || Is(command, "--version") || Is(command, "-V"))
            {
                options.Command = CliCommand.Version;
                return options;
            }

            if (Is(command, "convert") || Is(command, "--convert"))
            {
                options.Command = CliCommand.Convert;
                options.ParseConvert(args);
                return options;
            }
            if (Is(command, "formats") || Is(command, "--formats") || Is(command, "list-formats"))
            {
                options.Command = CliCommand.Formats;
                options.ParseFormats(args);
                return options;
            }
            if (Is(command, "plugins") || Is(command, "--plugins") || Is(command, "list-plugins"))
            {
                options.Command = CliCommand.Plugins;
                options.ParsePlugins(args);
                return options;
            }

            options.Command = CliCommand.None;
            if (command.StartsWith("-", StringComparison.Ordinal))
            {
                options.Errors.Add(string.Format(CliText.UnknownOption, command));
            }
            else
            {
                options.Errors.Add(string.Format(CliText.UnknownCommand, command));
                options.Errors.Add(CliText.UnknownCommandHint);
            }
            return options;
        }

        private void ParseConvert(string[] args)
        {
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == null) continue;

                if (arg == "--")
                {
                    for (i++; i < args.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(args[i])) Files.Add(args[i]);
                    }
                    break;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    Files.Add(arg);
                    continue;
                }

                string name;
                string inline;
                SplitOption(arg, out name, out inline);

                switch (name.ToLowerInvariant())
                {
                    case "--to":
                        To = TakeValue(args, ref i, name, inline);
                        break;
                    case "--output":
                        Output = TakeValue(args, ref i, name, inline);
                        break;
                    case "--output-dir":
                        OutputDir = TakeValue(args, ref i, name, inline);
                        break;
                    case "--preset":
                        Preset = TakeValue(args, ref i, name, inline);
                        break;
                    case "--plugin":
                        PluginId = TakeValue(args, ref i, name, inline);
                        break;
                    case "-o":
                    case "--option":
                        AddOverride(TakeValue(args, ref i, name, inline));
                        break;
                    case "--file-list":
                        var list = TakeValue(args, ref i, name, inline);
                        if (!string.IsNullOrEmpty(list)) FileLists.Add(list);
                        break;
                    case "--jobs":
                        ParseJobs(TakeValue(args, ref i, name, inline));
                        break;
                    case "--progress":
                        ParseProgress(TakeValue(args, ref i, name, inline));
                        break;
                    case "--force":
                        RejectInlineValue(arg, name, inline);
                        Force = true;
                        break;
                    case "--skip":
                        RejectInlineValue(arg, name, inline);
                        Skip = true;
                        break;
                    case "--dry-run":
                        RejectInlineValue(arg, name, inline);
                        DryRun = true;
                        break;
                    case "--json":
                        RejectInlineValue(arg, name, inline);
                        Json = true;
                        break;
                    case "--quiet":
                        RejectInlineValue(arg, name, inline);
                        Quiet = true;
                        break;
                    case "--verbose":
                        RejectInlineValue(arg, name, inline);
                        Verbose = true;
                        break;
                    case "--stop-on-error":
                        RejectInlineValue(arg, name, inline);
                        StopOnError = true;
                        break;
                    case "--help":
                    case "-h":
                        Command = CliCommand.Help;
                        return;
                    case "--version":
                        Command = CliCommand.Version;
                        return;
                    default:
                        Errors.Add(string.Format(CliText.UnknownOption, arg));
                        break;
                }
            }

            if (Json && Quiet)
            {
                Errors.Add(CliText.JsonQuietConflict);
            }
            if (Force && Skip)
            {
                Errors.Add(CliText.ForceSkipConflict);
            }
            if (!string.IsNullOrEmpty(Output) && !string.IsNullOrEmpty(OutputDir))
            {
                Errors.Add(CliText.OutputConflict);
            }
            if (string.IsNullOrEmpty(To))
            {
                Errors.Add(CliText.MissingTargetFormat);
            }
        }

        private void ParseFormats(string[] args)
        {
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == null) continue;

                string name;
                string inline;
                SplitOption(arg, out name, out inline);

                switch (name.ToLowerInvariant())
                {
                    case "--input":
                        InputExtension = TakeValue(args, ref i, name, inline);
                        break;
                    case "--plugin":
                        PluginId = TakeValue(args, ref i, name, inline);
                        break;
                    case "--json":
                        RejectInlineValue(arg, name, inline);
                        Json = true;
                        break;
                    case "--help":
                    case "-h":
                        Command = CliCommand.Help;
                        return;
                    case "--version":
                        Command = CliCommand.Version;
                        return;
                    default:
                        Errors.Add(string.Format(CliText.UnknownOption, arg));
                        break;
                }
            }

            if (Json && Quiet)
            {
                Errors.Add(CliText.JsonQuietConflict);
            }
        }

        private void ParsePlugins(string[] args)
        {
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == null) continue;

                string name;
                string inline;
                SplitOption(arg, out name, out inline);

                switch (name.ToLowerInvariant())
                {
                    case "--json":
                        RejectInlineValue(arg, name, inline);
                        Json = true;
                        break;
                    case "--help":
                    case "-h":
                        Command = CliCommand.Help;
                        return;
                    case "--version":
                        Command = CliCommand.Version;
                        return;
                    default:
                        Errors.Add(string.Format(CliText.UnknownOption, arg));
                        break;
                }
            }
        }

        private void ParseJobs(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            int jobs;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out jobs)
                || jobs < 1 || jobs > 64)
            {
                Errors.Add(CliText.InvalidJobs);
                return;
            }
            Jobs = jobs;
        }

        private void ParseProgress(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (Is(value, "auto"))
            {
                Progress = CliProgressMode.Auto;
            }
            else if (Is(value, "always"))
            {
                Progress = CliProgressMode.Always;
            }
            else if (Is(value, "never"))
            {
                Progress = CliProgressMode.Never;
            }
            else
            {
                Errors.Add(CliText.InvalidProgress);
            }
        }

        private void AddOverride(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var index = value.IndexOf('=');
            if (index <= 0)
            {
                Errors.Add(string.Format(CliText.InvalidOverride, value));
                return;
            }

            Overrides.Add(new CliOptionOverride(
                value.Substring(0, index).Trim(),
                value.Substring(index + 1)));
        }

        private string TakeValue(string[] args, ref int index, string name, string inline)
        {
            if (inline != null)
            {
                if (inline.Length == 0)
                {
                    Errors.Add(string.Format(CliText.MissingOptionValue, name));
                    return null;
                }
                return inline;
            }

            if (index + 1 >= args.Length
                || args[index + 1] == null
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Errors.Add(string.Format(CliText.MissingOptionValue, name));
                return null;
            }

            index++;
            return args[index];
        }

        private void RejectInlineValue(string raw, string name, string inline)
        {
            if (inline != null)
            {
                Errors.Add(string.Format(CliText.UnknownOption, raw));
            }
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

        private static bool Is(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}

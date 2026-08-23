using System;
using System.IO;
using Newtonsoft.Json;

namespace UniversalConvert.Core.Config
{
    /// <summary>
    /// 配置读写。所有需要共享状态的地方（主程序、右键菜单）都通过它读写同一份配置。
    /// </summary>
    public sealed class ConfigStore
    {
        public const string AppFolderName = "UniversalConvert";
        public const string ConfigFileName = "config.json";

        /// <summary>配置所在目录：%AppData%\UniversalConvert。</summary>
        public static string ConfigDirectory
        {
            get
            {
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(roaming, AppFolderName);
            }
        }

        public static string ConfigPath => Path.Combine(ConfigDirectory, ConfigFileName);

        public AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                var json = File.ReadAllText(ConfigPath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(AppConfig config)
        {
            if (config == null) return;
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }
}

using System.IO;
using System.Text.Json;
using IOT_Manager.Models;

namespace IOT_Manager.Services
{
    public class SettingsService
    {
        private const string ConfigFile = "appsettings.json";
        public AppConfig Config { get; private set; }

        public SettingsService()
        {
            LoadConfig();
        }

        public void LoadConfig()
        {
            if (File.Exists(ConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFile);
                    Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                catch
                {
                    Config = new AppConfig();
                }
            }
            else
            {
                Config = new AppConfig();
            }
        }

        public void SaveConfig()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(ConfigFile, json);
        }
    }
}
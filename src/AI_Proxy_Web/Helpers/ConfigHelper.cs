using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Helpers;

public class ConfigHelper
{
    private static readonly Lazy<ConfigHelper> _instance = new Lazy<ConfigHelper>(() => new ConfigHelper());
    public static ConfigHelper Instance => _instance.Value;

    private JObject Config { get; set; }
    private JObject? DevConfig { get; set; }

    private ConfigHelper()
    {
        LoadConfig();
    }

    private void LoadConfig()
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found at: {configPath}");
        }

        string jsonContent = File.ReadAllText(configPath);
        Config = JObject.Parse(jsonContent);
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment == "Development")
        {
            string devConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.Development.json");
            if (File.Exists(devConfigPath))
            {
                jsonContent = File.ReadAllText(devConfigPath);
                DevConfig = JObject.Parse(jsonContent);
            }
        }
    }

    // 可选：添加一个重新加载配置的方法
    public void ReloadConfig()
    {
        LoadConfig();
    }

    public T GetConfig<T>(string key)
    {
        var ks = key.Split(":", StringSplitOptions.RemoveEmptyEntries);
        if (ks.Length == 1)
        {
            if (DevConfig != null && DevConfig[key] is not null)
            {
                return DevConfig[key].Value<T>();
            }
            if (Config[key] is not null)
                return Config[key].Value<T>();
        }
        else
        {
            if (DevConfig != null)
            {
                JToken tk = DevConfig;
                for (var i = 0; i < ks.Length-1; i++)
                {
                    if (tk != null && tk[ks[i]] is not null)
                        tk = tk[ks[i]];
                    else tk = null;
                }

                if (tk != null && tk[ks[ks.Length - 1]] is not null)
                    return tk[ks[ks.Length - 1]].Value<T>();
            }
            JToken tk2 = Config;
            for (var i = 0; i < ks.Length-1; i++)
            {
                if(tk2 != null && tk2[ks[i]] is not null)
                    tk2 = tk2[ks[i]];
                else tk2 = null;
            }
            if(tk2 != null && tk2[ks[ks.Length - 1]] is not null)
                return tk2[ks[ks.Length - 1]].Value<T>();
        }

        if(!key.StartsWith("ModelUsed:"))
            Console.WriteLine($"Config {key} not found");
        return default(T);
    }
}
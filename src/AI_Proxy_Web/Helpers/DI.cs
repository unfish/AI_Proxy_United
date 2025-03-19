using System.Reflection;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.External;
using AI_Proxy_Web.Feishu;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Functions.InternalFunctions;
using MessagePack;
using MySql.EntityFrameworkCore.Extensions;

namespace AI_Proxy_Web.Helpers;

public class DI
{
    private static Dictionary<int, Type> _apiClassTypes;
    private static Dictionary<int, ApiClassAttribute> _apiClassAttributes;
    private static Dictionary<string, Type> _funcProcessorTypes;
    private static Dictionary<string, ProcessorAttribute> _funcProcessorAttributes;
    public static void RegisterService(WebApplicationBuilder builder)
    {
        // 确保配置在应用启动前加载
        var configHelper = ConfigHelper.Instance;
        builder.Services.AddSingleton(configHelper);
        
        MessagePackSerializer.DefaultOptions = MessagePack.Resolvers.ContractlessStandardResolver.Options.WithCompression(MessagePackCompression.Lz4BlockArray);
        
        var redisConnection = configHelper.GetConfig<string>("Connection:Redis");
        var redisClient = new CSRedis.CSRedisClient(redisConnection+",idleTimeout=15000,tryit=2,poolsize=1000,connectTimeout=30000");
        RedisHelper.Initialization(redisClient);
        
        var logDbConnectionString = configHelper.GetConfig<string>("Connection:DB");
        builder.Services.AddMySQLServer<LogDbContext>(logDbConnectionString, optionsBuilder =>
        {
            optionsBuilder.EnableRetryOnFailure(3);
        });
        
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<CustomRateLimiter>();
        builder.Services.AddSingleton<IFeishuRestClient, FeishuRestClient>();
        builder.Services.AddSingleton<IOpenAIRestClient, OpenAIRestClient>();
        
        builder.Services.AddScoped<IApiFactory, ApiFactory>();
        builder.Services.AddScoped<ILogRepository, LogRepository>();
        builder.Services.AddScoped<IFunctionRepository, FunctionRepository>();
        builder.Services.AddScoped<IRestRepository, RestRepository>();
        builder.Services.AddScoped<IFeishuService, FeishuService>();
        builder.Services.AddScoped<IAudioService, AudioService>();
        
        builder.Services.AddScoped<IBoyerMooreMatchService, BoyerMooreMatchService>();
        builder.Services.AddScoped<IBookFeishuService, BookFeishuService>();
        builder.Services.AddScoped<IWeatherApi, WeatherApi>();
        builder.Services.AddScoped<IOssFileService, OssFileService>();

        //初始化所有的Api子类并缓存，可以通过模型的ID快速获取模型实例，并且不需要反射的性能损失
        var assembly = Assembly.GetExecutingAssembly();
        _apiClassTypes = new Dictionary<int, Type>();
        _apiClassAttributes = new Dictionary<int, ApiClassAttribute>();
        _funcProcessorTypes = new Dictionary<string, Type>();
        _funcProcessorAttributes = new Dictionary<string, ProcessorAttribute>();
        var clientType = typeof(IApiClient);
        foreach (var t in  assembly.GetTypes())
        {
            if (t.IsAssignableTo(clientType)) //自动注册所有的ApiClient类
            {
                builder.Services.AddScoped(t, sp => ActivatorUtilities.CreateInstance(sp, t));
            }
            else
            {
                var attr = t.GetCustomAttribute<ApiClassAttribute>(); //自动注册所有的Api类并缓存
                if (attr != null)
                {
                    var mu = configHelper.GetConfig<int>("ModelUsed:"+((M)attr.Id).ToString());
                    attr.Hidden = mu == 0;
                    attr.NeedLevel = mu == 1 ? 0 : mu;
                    _apiClassTypes.Add(attr.Id, t);
                    _apiClassAttributes.Add(attr.Id, attr);
                    builder.Services.AddScoped(t, sp => ActivatorUtilities.CreateInstance(sp, t));
                }
                else
                {
                    var attr2 = t.GetCustomAttribute<ProcessorAttribute>();
                    if (attr2 != null)
                    {
                        _funcProcessorTypes.Add(attr2.Name, t);
                        _funcProcessorAttributes.Add(attr2.Name, attr2);
                        builder.Services.AddScoped(t, sp => ActivatorUtilities.CreateInstance(sp, t));
                    }
                }
            }
        }
    }

    public static bool IsApiClass(int id)
    {
        return _apiClassTypes.ContainsKey(id);
    }
    
    public static ApiBase GetApiClass(int id, IServiceProvider serviceProvider)
    {
        if (!_apiClassTypes.TryGetValue(id, out var subclassType))
        {
            throw new Exception("No subclass found for the provided id");
        }

        return (ApiBase)serviceProvider.GetRequiredService(subclassType);
    }

    public static List<ApiClassAttribute> GetApiClassAttributes()
    {
        return _apiClassAttributes.Values.Where(t=>!t.Hidden).OrderBy(t => t.Order).ToList();
    }
    
    public static ApiClassAttribute? GetApiClassAttribute(int id)
    { 
        if (!_apiClassAttributes.TryGetValue(id, out var attribute))
        {
            return null;
        }
        return attribute;
    }

    public static bool IsApiClass(int id, Type apiClass)
    {
        if (!_apiClassTypes.TryGetValue(id, out var subclassType))
        {
            return false;
        }

        return subclassType == apiClass;
    }
    
    public static int GetApiClassAttributeId(Type apiClass)
    {
        foreach (var kv in _apiClassTypes)
        {
            if (kv.Value == apiClass)
                return kv.Key;
        }

        return -1;
    }
    
    public static BaseProcessor GetFuncProcessor(string name, IServiceProvider serviceProvider)
    {
        if (!_funcProcessorTypes.TryGetValue(name, out var subclassType))
        {
            throw new Exception("No processor found for the function: " + name);
        }

        return (BaseProcessor)serviceProvider.GetRequiredService(subclassType);
    }

    public static bool IsMultiTurnFunc(string name)
    {
        if(_funcProcessorAttributes.TryGetValue(name, out var attr))
        {
            return attr.MultiTurn;
        }
        return false;
    }

}
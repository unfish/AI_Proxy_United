using System.Reflection;
using System.Threading.Channels;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Apis.V2;
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
    private static Dictionary<string, Type> _funcProcessorTypes;
    private static Dictionary<string, ProcessorAttribute> _funcProcessorAttributes;
    
    private static Dictionary<int, ApiClassAttribute> _modelsAttributes;
    private static Dictionary<string, Type> _apiProviders;
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
        
        builder.Services.AddScoped<IBoyerMooreMatchService, BoyerMooreMatchService>();
        builder.Services.AddScoped<IBookFeishuService, BookFeishuService>();
        builder.Services.AddScoped<IWeatherApi, WeatherApi>();
        builder.Services.AddScoped<IOssFileService, OssFileService>();
        
        //初始化所有的Api子类并缓存，可以通过模型的ID快速获取模型实例，并且不需要反射的性能损失
        var assembly = Assembly.GetExecutingAssembly();
        _funcProcessorTypes = new Dictionary<string, Type>();
        _funcProcessorAttributes = new Dictionary<string, ProcessorAttribute>();
        _apiProviders = new Dictionary<string, Type>();
        foreach (var t in  assembly.GetTypes())
        {
            var attr2 = t.GetCustomAttribute<ProcessorAttribute>();
            if (attr2 != null)
            {
                _funcProcessorTypes.Add(attr2.Name, t);
                _funcProcessorAttributes.Add(attr2.Name, attr2);
                builder.Services.AddScoped(t, sp => ActivatorUtilities.CreateInstance(sp, t));
                continue;
            }    
            var attr3 = t.GetCustomAttribute<ApiProviderAttribute>();
            if (attr3 != null)
            {
                _apiProviders.Add(attr3.Name, t);
                builder.Services.AddScoped(t, sp => ActivatorUtilities.CreateInstance(sp, t));
            }
        }
        
        _modelsAttributes = new Dictionary<int, ApiClassAttribute>();
        var models = configHelper.GetAllKeys("Models");
        foreach (var m in models)
        {
            var attr = new ApiClassAttribute()
            {
                Id = configHelper.GetConfig<int>("Models:" + m + ":Id"),
                Name = m,
                DisplayName = configHelper.GetConfig<string>("Models:" + m + ":DisplayName"),
                Provider = configHelper.GetConfig<string>("Models:" + m + ":Provider"),
                ModelName = configHelper.GetConfig<string>("Models:" + m + ":ModelName"),
                VisionModelName = configHelper.GetConfig<string>("Models:" + m + ":VisionModelName"),
                Description = configHelper.GetConfig<string>("Models:" + m + ":Description"),
                Order = configHelper.GetConfig<int>("Models:" + m + ":Order"),
                CanUseFunction = configHelper.GetConfig<bool>("Models:" + m + ":CanUseFunction"),
                CanProcessImage = configHelper.GetConfig<bool>("Models:" + m + ":CanProcessImage"),
                CanProcessAudio = configHelper.GetConfig<bool>("Models:" + m + ":CanProcessAudio"),
                CanProcessFile = configHelper.GetConfig<bool>("Models:" + m + ":CanProcessFile"),
                MaxTokens = configHelper.GetConfig<int>("Models:" + m + ":MaxTokens"),
                UseThinkingMode = configHelper.GetConfig<bool>("Models:" + m + ":UseThinkingMode"),
                ExtraTools = configHelper.GetConfig<string>("Models:" + m + ":ExtraTools") ?? "",
                NeedLevel = configHelper.GetConfig<int>("Models:" + m + ":NeedLevel"),
                NeedLongProcessTime = configHelper.GetConfig<bool>("Models:" + m + ":NeedLongProcessTime"),
                EmbeddingModelName = configHelper.GetConfig<string>("Models:" + m + ":EmbeddingModelName"),
                EmbeddingDimensions = configHelper.GetConfig<int>("Models:" + m + ":EmbeddingDimensions"),
                Type = Enum.Parse<ApiClassTypeEnum>(configHelper.GetConfig<string>("Models:" + m + ":Type")),
                Hidden = configHelper.GetConfig<bool>("Models:" + m + ":Hidden"),
                ExtraHeaders = configHelper.GetConfig<string>("Models:" + m + ":ExtraHeaders") ?? "",
            };
            if (string.IsNullOrEmpty(attr.VisionModelName))
                attr.VisionModelName = attr.ModelName;
            _modelsAttributes.Add(attr.Id, attr);
        }
    }
    
        
    public static ApiProviderBase? GetApiProvider(string name, IServiceProvider serviceProvider)
    {
        if (_apiProviders.TryGetValue(name, out var subclassType))
        {
            return (ApiProviderBase)serviceProvider.GetRequiredService(subclassType);
        }
        Console.WriteLine("No ApiProvider found for the provided name " + name);
        return null;
    }

    /// <summary>
    /// 注册Channels通道处理类
    /// </summary>
    /// <param name="services"></param>
    /// <typeparam name="TE">Channels参数类</typeparam>
    /// <typeparam name="TS">BackgroundService类</typeparam>
    private static void RegisterChannels<TE, TS>(IServiceCollection services) where TS:BackgroundService
    {
        services.AddSingleton(Channel.CreateUnbounded<TE>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = false }));
        services.AddSingleton(svc => svc.GetRequiredService<Channel<TE>>().Reader);
        services.AddSingleton(svc => svc.GetRequiredService<Channel<TE>>().Writer);
        services.AddHostedService<TS>();
    }
    
    public static bool IsModel(int id)
    {
        return _modelsAttributes.ContainsKey(id);
    }
    
    public static List<ApiClassAttribute> GetApiClassAttributes()
    {
        return _modelsAttributes.Values.Where(t=>!t.Hidden).OrderBy(t => t.Order).ToList();
    }
    
    public static ApiClassAttribute? GetApiClassAttribute(int id)
    { 
        return _modelsAttributes.GetValueOrDefault(id);
    }

    public static int GetModelIdByName(string name)
    {
        foreach (var kv in _modelsAttributes)
        {
            if (kv.Value.Name == name)
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
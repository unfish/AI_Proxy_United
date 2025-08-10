using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Apis.V2;

public abstract class ApiProviderBase
{
    protected readonly ConfigHelper configHelper;
    protected readonly IServiceProvider serviceProvider;
    protected ApiClassAttribute apiClassAttribute;
    public ApiProviderBase(ConfigHelper configHelper, IServiceProvider serviceProvider)
    {
        this.configHelper = configHelper;
        this.serviceProvider = serviceProvider;
    }
    
    protected string _host = string.Empty;
    protected string _key = string.Empty;
    protected string _modelName = string.Empty;
    protected string _visionModelName = string.Empty;
    protected int _maxTokens = 4096;
    protected bool _useThinkingMode = false;
    protected string _extraTools = string.Empty;
    protected List<KeyValuePair<string, string>> _extraHeaders = new List<KeyValuePair<string, string>>();

    protected string _chatUrl = string.Empty;
    public virtual void Setup(ApiClassAttribute attr)
    {
        apiClassAttribute = attr;
        _host = configHelper.GetProviderConfig<string>(attr.Provider,"Host");
        _key = configHelper.GetProviderConfig<string>(attr.Provider,"Key");
        _modelName = attr.ModelName;
        _visionModelName = attr.VisionModelName;
        _maxTokens = attr.MaxTokens;
        _useThinkingMode = attr.UseThinkingMode;
        _extraTools = attr.ExtraTools;
        if (!string.IsNullOrEmpty(attr.ExtraHeaders))
        {
            var ss = attr.ExtraHeaders.Split(';');
            foreach (var s in ss)
            {
                var ss1 = s.Split(':');
                _extraHeaders.Add(new KeyValuePair<string, string>(ss1[0], ss1[1]));
            }
        }
    }

    public abstract IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input);
    
    public virtual async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
    }
    
    /// <summary>
    /// 虚方法，用来给子类继承，实现各个子类特有的参数预处理逻辑
    /// </summary>
    /// <param name="input"></param>
    public virtual void InitSpecialInputParam(ApiChatInputIntern input)
    {
    }
    
    /// <summary>
    /// 虚方法，用来给子类继承，实现各个子类特有的开始新会话的处理，比如GPT Assistant在开启新会话的时候需要删除旧的ThreadId
    /// </summary>
    public virtual void StartNewContext(string ownerId)
    {
    }
    
    public virtual async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(
        List<ChatContext.ChatContextContent> qc, bool embedForQuery = false)
    {
        return (ResultType.Error, null, "该模型未实现向量化接口");
    }

    protected List<ExtraOption> extraOptionsList = null;
    /// <summary>
    /// 虚方法，用来获取子类的额外配置项，用于画图类模型的样式和尺寸选择，以及思考模型的可控的思考深度等选项
    /// </summary>
    /// <param name="ext_userId"></param>
    public List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        if (extraOptionsList != null)
        {
            foreach (var option in extraOptionsList)
            {
                var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
                var v = CacheService.Get<string>(cacheKey);
                option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
            }
        }
        return extraOptionsList;
    }
    
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
}
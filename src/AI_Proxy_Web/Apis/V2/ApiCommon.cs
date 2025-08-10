using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Apis.V2;

public class ApiCommon : ApiBase
{
    private ApiProviderBase? apiProvider;
    public ApiCommon(IServiceProvider serviceProvider, int modelId) : base(serviceProvider)
    {
        var configHelper = serviceProvider.GetRequiredService<ConfigHelper>();
        var attr = DI.GetApiClassAttribute(modelId);
        var apiType = configHelper.GetProviderConfig<string>(attr.Provider, "Api");
        //不需要外部参数的单一模型接口，不需要配置Providers段，直接通过模型配置中的Provider属性进行反射
        if (string.IsNullOrEmpty(apiType))
            apiProvider = DI.GetApiProvider(attr.Provider, serviceProvider);
        else
            apiProvider = DI.GetApiProvider(apiType, serviceProvider);
        apiProvider?.Setup(attr);
    }

    public ApiProviderBase? ApiProvider => apiProvider;
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        if (apiProvider == null)
        {
            yield return Result.Error("指定的模型不存在");
            yield break;
        }
        await foreach (var res in apiProvider.SendMessageStream(input))
        {
            yield return res;
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        if (apiProvider == null)
        {
            return Result.Error("指定的模型不存在");
        }
        return await apiProvider.SendMessage(input);
    }
    
    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        apiProvider?.InitSpecialInputParam(input);
    }

    public void StartNewContext(string ownerId)
    {
        apiProvider?.StartNewContext(ownerId);
    }

    /// <summary>
    /// 文本向量化接口，虚方法，留给子类覆盖
    /// 注意：不同的模型的向量化的输入长度和输出长度不同，务必使用同一个模型进行向量化索引和搜索
    /// </summary>
    /// <param name="qc">问题列表</param>
    /// <param name="embedForQuery">向量的目的：true for文档索引，false for 查询</param>
    /// <returns></returns>
    public async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        return await apiProvider.Embeddings(qc, embedForQuery);
    }
    
    /// <summary>
    /// 虚方法，用来获取子类的额外配置项，用于画图类模型的样式和尺寸选择，以及思考模型的可控的思考深度等选项
    /// </summary>
    /// <param name="ext_userId"></param>
    public List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return apiProvider?.GetExtraOptions(ext_userId);
    }
    
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        apiProvider?.SetExtraOptions(ext_userId, type, value);
    }
}
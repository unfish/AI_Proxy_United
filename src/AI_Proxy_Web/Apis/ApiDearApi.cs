using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis;

/// <summary>
/// Dearapi代理网站，用OpenAI接口实现了Grok3的访问，不知道怎么做到的。
/// </summary>
[ApiClass(M.GPT4MiniProxy, "GPT4o Mini备用", "GPT4o Mini 备用通道。", 46, canUseFunction:true,  priceIn: 1.08, priceOut: 4.32)]
public class ApiDearApi:ApiGPTOriginal
{
    public ApiDearApi(ConfigHelper configHelper, IServiceProvider serviceProvider) : base(configHelper, serviceProvider)
    {
        chatUrl = "https://api.dearapi.com/v1/chat/completions";
        apiKey = configHelper.GetConfig<string>("Service:DeerApi:Key");
    }
}

[ApiClass(M.GPT4Proxy, "GPT4o备用", "GPT 4o 备用通道，使用最新的4o 模型，但暂不支持 function call。", 47, canUseFunction:false, canProcessFile:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiDearApiGpt4 : ApiGPT4Original
{
    public ApiDearApiGpt4(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        chatUrl = "https://api.dearapi.com/v1/chat/completions";
        apiKey = configuration.GetConfig<string>("DeerApi:Key");
        modelName = "chatgpt-4o-latest";
    }
}

[ApiClass(M.Grok3, "Grok3备用", "Grok3 DearAPI备用通道。", 38, type: ApiClassTypeEnum.问答模型, priceIn: 4, priceOut: 16)]
public class ApiDearApiGrok3 : ApiDearApiGpt4
{
    public ApiDearApiGrok3(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3";
    }
}

[ApiClass(M.Grok3Thinking, "Grok3 Thinking", "Grok3 Thinking DearAPI备用通道。", 124, type: ApiClassTypeEnum.推理模型, priceIn: 4, priceOut: 16)]
public class ApiDearApiGrok3Thinking : ApiDearApiGpt4
{
    public ApiDearApiGrok3Thinking(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-reasoner";
    }
}

[ApiClass(M.Grok3DeepSearch, "Grok3 DeepSearch", "Grok3 DeepSearch DearAPI备用通道。", 193, type: ApiClassTypeEnum.搜索模型, priceIn: 4, priceOut: 16)]
public class ApiDearApiGrok3DeepSearch : ApiDearApiGpt4
{
    public ApiDearApiGrok3DeepSearch(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-deepsearch";
    }
}
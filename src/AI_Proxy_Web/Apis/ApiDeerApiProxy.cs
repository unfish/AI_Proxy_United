using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeerApi_GPT4oMini, "GPT4o Mini备用", "GPT4o Mini DeerApi 备用通道。", 46, canUseFunction:true,  priceIn: 1.08, priceOut: 4.32)]
public class ApiGptProxy:ApiOpenAIBase
{
    public ApiGptProxy(ConfigHelper configHelper, IServiceProvider serviceProvider) : base(configHelper, serviceProvider)
    {
        chatUrl = "https://api.deerapi.com/v1/chat/completions";
        apiKey = configHelper.GetConfig<string>("Service:DeerApi:Key");
    }
}

[ApiClass(M.DeerApi_GPT4o, "GPT4o备用", "GPT 4o DeerApi 备用通道，使用最新的4o 模型，但暂不支持 function call。", 47, canUseFunction:false, canProcessFile:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiGPT4Proxy : ApiGptProxy
{
    public ApiGPT4Proxy(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "chatgpt-4o-latest";
    }
}

[ApiClass(M.DeerApi_GPT45, "GPT4.5备用", "GPT 4.5 DeerApi 备用通道。", 47, canUseFunction:false, canProcessFile:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiGPT4_5Proxy : ApiGptProxy
{
    public ApiGPT4_5Proxy(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "gpt-4.5-preview";
    }
}

[ApiClass(M.Grok3, "Grok3备用", "Grok3 DeerApi备用通道。", 38, type: ApiClassTypeEnum.问答模型, priceIn: 4, priceOut: 16)]
public class ApiGrok3DeerApi : ApiGPT4Proxy
{
    public ApiGrok3DeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3";
    }
}

[ApiClass(M.Grok3Thinking, "Grok3 Thinking", "Grok3 Thinking DeerApi备用通道。", 124, type: ApiClassTypeEnum.推理模型, priceIn: 4, priceOut: 16)]
public class ApiGrok3ThinkingDeerApi : ApiGPT4Proxy
{
    public ApiGrok3ThinkingDeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-reasoner";
    }
}
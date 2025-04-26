using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeerApi_GPT4oMini, "GPT4.1 Mini备用", "GPT4.1 Mini DeerApi 备用通道。", 46, canUseFunction:true,  priceIn: 1.08, priceOut: 4.32)]
public class ApiGptProxy:ApiOpenAIBase
{
    public ApiGptProxy(ConfigHelper configHelper, IServiceProvider serviceProvider) : base(configHelper, serviceProvider)
    {
        chatUrl = "https://api.deerapi.com/v1/chat/completions";
        apiKey = configHelper.GetConfig<string>("Service:DeerApi:Key");
    }
}

[ApiClass(M.DeerApi_GPT4o, "GPT4.1备用", "GPT 4.1 DeerApi 备用通道。", 47, canUseFunction:false, canProcessFile:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiGPT4Proxy : ApiGptProxy
{
    public ApiGPT4Proxy(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "gpt-4.1";
    }
}

[ApiClass(M.Grok3备用, "Grok3备用", "X.ai Grok3 DeerApi通道，号称最强大模型。", 48, type: ApiClassTypeEnum.问答模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 30, priceOut: 75)]
public class ApiGrok3DeerApi : ApiGPT4Proxy
{
    public ApiGrok3DeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-fast";
    }
}

[ApiClass(M.Grok3DeepSearch, "Grok3 DeepSearch", "X.ai Grok3 DeepSearch DeerApi通道，带深度搜索和推理的Grok3模型。", 193, type: ApiClassTypeEnum.搜索模型, priceIn: 30, priceOut: 75)]
public class ApiGrok3DeepSearchDeerApi : ApiGPT4Proxy
{
    public ApiGrok3DeepSearchDeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-deepersearch";
    }
}


[ApiClass(M.DeerApi_o4Mini, "o4 Mini", "o4 Mini DeerApi通道，最强推理代码模型。", 102, type: ApiClassTypeEnum.推理模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 30, priceOut: 75)]
public class ApiO4MiniDeerApi : ApiGPT4Proxy
{
    public ApiO4MiniDeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "o4-mini";
        isReasoningModel = true;
    }
}

[ApiClass(M.DeerApi_o3, "GPT o3", "GPT o3 DeerApi通道，最强推理模型。", 103, type: ApiClassTypeEnum.推理模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 30, priceOut: 300)]
public class ApiO3DeerApi : ApiGPT4Proxy
{
    public ApiO3DeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "o3";
        isReasoningModel = true;
    }
}
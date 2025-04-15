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

[ApiClass(M.Grok3, "Grok3", "X.ai Grok3 DeerApi通道，号称最强大模型。", 38, type: ApiClassTypeEnum.问答模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 30, priceOut: 75)]
public class ApiGrok3DeerApi : ApiGPT4Proxy
{
    public ApiGrok3DeerApi(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "grok-3-fast";
    }
}
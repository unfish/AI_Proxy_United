using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis;

/// <summary>
/// 文档 https://ppinfra.com/model-api/pricing
/// </summary>
[ApiClass(M.PPIO_DeepSeekR1, "DS R1 PPIO版", "DeepSeek R1 PPIO服务器备用通道，官方API不可用时备用。", 117,  type: ApiClassTypeEnum.推理模型, canUseFunction:false, priceIn: 4, priceOut: 16)]
public class ApiPPIO_DeepSeekR1 : ApiGPT4Original
{
    public ApiPPIO_DeepSeekR1(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        chatUrl = "https://api.ppinfra.com/v3/openai/chat/completions";
        apiKey = configuration.GetConfig<string>("Service:PPIO:Key");
        modelName = "deepseek/deepseek-r1";
    }
}
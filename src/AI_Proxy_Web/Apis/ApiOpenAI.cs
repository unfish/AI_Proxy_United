using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.GPT4o_Mini, "GPT4.1 Mini", "GPT 4.1 mini, 回复速度快，接口更便宜也更稳定，100W上下文长度，能力强于GPT 4o，速度和价格更有优势。", 0, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 1.08, priceOut: 4.32)]
public class ApiOpenAIBase:ApiBase
{
    protected IServiceProvider _serviceProvider;
    protected string chatUrl;
    protected string hostUrl;
    protected string apiKey;
    protected string modelName;
    protected string visionModelName;
    protected bool isReasoningModel = false;
    protected string extraTools = string.Empty;
    public ApiOpenAIBase(ConfigHelper configHelper, IServiceProvider serviceProvider):base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
        hostUrl = configHelper.GetConfig<string>("Service:OpenAI:Host");
        chatUrl = hostUrl + "v1/chat/completions";
        //chatUrl = hostUrl + "v1/responses";
        apiKey = configHelper.GetConfig<string>("Service:OpenAI:Key");
        modelName = "gpt-4.1-mini";
        visionModelName = "gpt-4.1-mini";
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        _client.Setup(chatUrl, apiKey, modelName, visionModelName, isReasoningModel, extraTools);
        if (chatUrl.EndsWith("responses"))
        {
            await foreach (var res in _client.SendResponseApiStream(input))
            {
                yield return res;
            }
        }
        else
        {
            await foreach (var res in _client.SendMessageStream(input))
            {
                yield return res;
            }
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        _client.Setup(chatUrl, apiKey, modelName, visionModelName, isReasoningModel, extraTools);
        if (chatUrl.EndsWith("responses"))
        {
            return await _client.SendResponseApiMessage(input);
        }
        else
        {
            return await _client.SendMessage(input);
        }
    }
    
    /// <summary>
    /// 可输入数组，每个字符串长度1000字以内，返回向量长度1536
    /// </summary>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        _client.Setup(hostUrl, apiKey);
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.GPT4o, "GPT 4.1", "GPT 4.1 是OpenAI最新的多模态模型，超过GPT4o，同时速度快一倍，价格便宜一半。", 1, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 15, priceOut: 60)]
public class ApiOpenAI4o : ApiOpenAIBase
{
    public ApiOpenAI4o(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "gpt-4.1";
        visionModelName = "gpt-4.1";
    }
}

[ApiClass(M.GPT4oSearch, "GPT 4o Search", "GPT 4o Search 自带搜索，但搜索功能收费。", 194, type: ApiClassTypeEnum.搜索模型, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiGPT4OSearch : ApiOpenAIBase
{
    public ApiGPT4OSearch(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "gpt-4o-search-preview";
        visionModelName = "gpt-4o-search-preview";
        extraTools = "web_search_preview";
    }
}

[ApiClass(M.GPT_o1, "GPT o1", "GPT o1 正式版，会思考的AI，适用于有明确答案的复杂问题，不要用来提问其它大模型可以轻易解决的问题。非常非常贵。不支持流式返回，所以返回结果会比较慢，耐心等待。", 113, ApiClassTypeEnum.推理模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true,  priceIn: 108, priceOut: 432)]
public class ApiGPTo1 : ApiOpenAI4o
{
    public ApiGPTo1(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "o1";
        visionModelName = "o1";
        isReasoningModel = true;
        chatUrl = hostUrl + "v1/responses";
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        _client.SetExtraOptions(ext_userId, type, value);
    }
}

[ApiClass(M.GPT_o3Mini, "o3 Mini", "GPT o3 mini，会思考的AI mini版，适用于有明确答案的复杂问题，不要用来提问其它大模型可以轻易解决的问题。非常贵，暂时不能处理图片和函数调用。思考深度可选低中高三档，费用和解决问题的能力依次上升。", 112,ApiClassTypeEnum.推理模型, canUseFunction:true, priceIn: 8, priceOut: 32)]
public class ApiGPTo1Mini : ApiOpenAI4o
{
    public ApiGPTo1Mini(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "o3-mini";
        visionModelName = "gpt-4o-2024-11-20";
        isReasoningModel = true;
        chatUrl = hostUrl + "v1/responses";
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        var _client = _serviceProvider.GetRequiredService<OpenAIClient>();
        _client.SetExtraOptions(ext_userId, type, value);
    }
}

/// <summary>
/// 暂时不可用，模型还没有开放，也没有调试
/// </summary>
[ApiClass(M.OpenAIAgent, "GPT Agent", "GPT Agent，带Computer use功能的执行代理。", 321, type: ApiClassTypeEnum.辅助模型, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 18, priceOut: 72)]
public class ApiOpenAIAgent : ApiOpenAIBase
{
    public ApiOpenAIAgent(ConfigHelper configuration, IServiceProvider serviceProvider) : base(configuration, serviceProvider)
    {
        modelName = "computer-use-preview";
        visionModelName = "computer-use-preview";
        extraTools = "computer_use_preview";
        isReasoningModel = true;
        chatUrl = hostUrl + "v1/responses";
    }
}

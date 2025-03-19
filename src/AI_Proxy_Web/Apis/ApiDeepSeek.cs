using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeepSeek, "DeepSeekV3", "DeepSeek V3开源大模型。号称超越了GPT4的开源大模型，中文能力、推理能力、代码能力都很强。", 30, canUseFunction: false, priceIn: 1, priceOut: 4)]
public class ApiDeepSeek:ApiBase
{
    protected DeepSeekClient _client;
    public ApiDeepSeek(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DeepSeekClient>();
    }
    
    /// <summary>
    /// 使用DeepSeek来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendMessageStream(input))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 使用DeepSeek来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
}

[ApiClass(M.DeepSeekR1, "DeepSeek R1", "DeepSeek R1开源类o1思考大模型。号称超越了o1的推理大模型，不支持图片。", 114, ApiClassTypeEnum.推理模型, canUseFunction: false,
    priceIn: 4, priceOut: 16)]
public class ApiDeepSeekR1 : ApiDeepSeek
{
    public ApiDeepSeekR1(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModelName("deepseek-reasoner");
    }
}

/// <summary>
/// DeepSeek大模型接口
/// 文档地址 https://platform.deepseek.com/docs
/// </summary>
public class DeepSeekClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public DeepSeekClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:DeepSeek:Key");
    }
    
    private static String hostUrl = "https://api.deepseek.com/beta/v1/chat/completions";

    private String APIKEY;//从开放平台控制台中获取
    private string modelName = "deepseek-chat";

    public void SetModelName(string name)
    {
        modelName = name;
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        decimal? tmp = modelName.Contains("reasoner") ? null : input.Temprature;
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = tmp,
            stream,
            tools,
            max_tokens = 4096,
            user = input.External_UserId
        }, jSetting);
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl;
        var msg = GetMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        await foreach (var resp in ProcessStreamResponse(response))
            yield return resp;
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
}
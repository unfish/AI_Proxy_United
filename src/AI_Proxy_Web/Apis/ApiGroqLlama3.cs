using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Llama3_70B, "Llama3 70B", "Llama3.1 70B，最强开源大模型，不过中文能力一般。由Groq提供服务，主打响应速度极快，支持function call。", 41,
    canUseFunction: true, priceIn: 0, priceOut: 0)]
public class ApiGroqLlama3 : ApiBase
{
    private GroqLlama3Client _client;

    public ApiGroqLlama3(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<GroqLlama3Client>();
    }

    /// <summary>
    /// 
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
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
}



/// <summary>
/// 01大模型接口
/// 文档地址 https://platform.lingyiwanwu.com/docs
/// </summary>
public class GroqLlama3Client:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public GroqLlama3Client(IHttpClientFactory httpClientFactory, ConfigHelper configuration, IFunctionRepository functionRepository)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        var host = configuration.GetConfig<string>("Service:Groq:Host");
        hostUrl = host + "openai/v1/chat/completions";
        APIKEY = configuration.GetConfig<string>("Service:Groq:Key");
    }
    
    private static string hostUrl;
    private string APIKEY;//从开放平台控制台中获取
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = "llama-3.1-70b-versatile";
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msgs = GetFullMessages(input.ChatContexts);
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        msgs.Insert(0,new TextMessage(){role = "system", content = $"你是一名专业的工作助理，精通各种问题的解答。注意：在之后的所有对话中请使用中文回复用户的问题。{funcPrompt}"});
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            temperature = input.Temprature,
            stream,
            tools = tools,
            max_tokens =  2048,
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
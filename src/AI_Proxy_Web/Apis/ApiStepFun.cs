using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.阶跃星辰, "阶跃星辰", "阶跃星辰step-2 + step-1v，支持图文，千亿参数，号称逻辑性最强。而且速度很快。", 25, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 38, priceOut: 120)]
public class ApiStepFun:ApiBase
{
    protected StepFunClient _client;
    public ApiStepFun(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<StepFunClient>();
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

[ApiClass(M.阶跃R1Mini, "阶跃R1", "阶跃星辰step-r1-v-mini，图文推理模型。", 128, type: ApiClassTypeEnum.推理模型, canProcessImage: true,
    canProcessMultiImages: true, canUseFunction: false, priceIn: 38, priceOut: 120)]
public class ApiStepFunR1Mini : ApiStepFun
{
    public ApiStepFunR1Mini(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.ModelName = "step-r1-v-mini";
        _client.VisionModelName = "step-r1-v-mini";
    }
}


[ApiClass(M.阶跃画图, "阶跃画图", "阶跃星辰1x画图模型。", 203, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.1)]
public class ApiStepFunImage:ApiStepFun
{
    public ApiStepFunImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach (var resp in _client.TextToImage(input))
        {
            yield return resp;
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetExtraOptions(ext_userId, type, value);
    }
}

/// <summary>
/// 01大模型接口
/// 文档地址 https://platform.lingyiwanwu.com/docs
/// </summary>
public class StepFunClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public StepFunClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:StepFun:Key");
    }
    
    private static String hostUrl = "https://api.stepfun.com/v1/chat/completions";

    private String APIKEY;//从开放平台控制台中获取
    public string ModelName = "step-2-16k";
    public string VisionModelName = "step-1o-turbo-vision";
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? VisionModelName : ModelName;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            tools,
            temperature = input.Temprature,
            stream,
            max_tokens = 2048,
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
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1024x1024"),
                    new KeyValuePair<string, string>("横屏", "1280x800"),
                    new KeyValuePair<string, string>("竖屏", "800x1280")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> TextToImage(ApiChatInputIntern input)
    {
        var img = input.ChatContexts.Contexts.Last().QC.FirstOrDefault(t => t.Type == ChatType.图片Base64);
        var url = "https://api.stepfun.com/v1/images/" + (img==null ? "generations" : "image2image");
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(
            img == null
                ? new
                {
                    model = "step-1x-medium",
                    prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
                    size = GetExtraOptions(input.External_UserId)[0].CurrentValue
                }
                : new
                {
                    model = "step-1x-medium",
                    prompt = input.ChatContexts.Contexts.Last().QC.Last(t => t.Type == ChatType.文本).Content,
                    size = GetExtraOptions(input.External_UserId)[0].CurrentValue,
                    source_url = "data:" + (string.IsNullOrEmpty(img.MimeType) ? "image/jpeg" : img.MimeType) +
                                 ";base64," +
                                 img.Content,
                    source_weight = 0.6
                }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["data"] != null)
        {
            var image_url = json["data"][0]["url"].Value<string>();
            var bytes = await client.GetByteArrayAsync(image_url);
            yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
        }
        else
        {
            yield return Result.Error(content);
        }
    }
}
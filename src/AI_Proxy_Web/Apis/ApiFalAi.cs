using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.FLuxPro, "FLux Pro", "FLux Pro，新兴文生图开源模型玩家，质量很高。速度有点慢，发送提示后耐心等待。", 210, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.45)]
public class ApiFalAiFluxPro:ApiBase
{
    private FalAiClient _client;
    public ApiFalAiFluxPro(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<FalAiClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach(var resp in _client.SendMessage(input))
            yield return resp;
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
/// FalAiClient API接口
/// 文档地址 https://fal.ai/docs
/// </summary>
public class FalAiClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public FalAiClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;

        APIKEY = configHelper.GetConfig<string>("Service:FalAI:Key");
        hostUrl = "https://fal.run/fal-ai/";
    }
    private String hostUrl;
    private String APIKEY;
    private string modelName = "flux-pro/v1.1-ultra";

    public void SetModel(string name)
    {
        modelName = name;
    }
    
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "square_hd"),
                    new KeyValuePair<string, string>("横屏", "landscape_4_3"),
                    new KeyValuePair<string, string>("竖屏", "portrait_4_3"),
                    new KeyValuePair<string, string>("宽横屏", "landscape_16_9"),
                    new KeyValuePair<string, string>("长竖屏", "portrait_16_9")
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
    public async IAsyncEnumerable<Result> SendMessage(ApiChatInputIntern input)
    {
        var url = hostUrl+modelName;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Key "+APIKEY);
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            image_size = GetExtraOptions(input.External_UserId)[0].CurrentValue
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["images"] != null)
        {
            var image_url = json["images"][0]["url"].Value<string>();
            var image_type = json["images"][0]["content_type"].Value<string>();
            var bytes = await client.GetByteArrayAsync(image_url);
            yield return FileResult.Answer(bytes, image_type.Contains("jpeg")?"jpg":"png", ResultType.ImageBytes);
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
}
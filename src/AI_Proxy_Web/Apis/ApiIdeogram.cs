using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Ideogram, "Ideogram2", "Ideogram v2，新兴文生图服务，质量很高，支持中文提示词，支持生成带英文单词的图片。速度有点慢，发送提示后耐心等待。", 208, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.3)]
public class ApiIdeogram:ApiBase
{
    private IdeogramClient _client;
    public ApiIdeogram(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<IdeogramClient>();
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
/// IdeogramClient API接口
/// 文档地址 https://api-docs.ideogram.ai/reference/post_generate_image
/// </summary>
public class IdeogramClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public IdeogramClient(IHttpClientFactory httpClientFactory, ConfigHelper configuration)
    {
        _httpClientFactory = httpClientFactory;
        
        APIKEY = configuration.GetConfig<string>("Service:Ideogram:Key");
        hostUrl = configuration.GetConfig<string>("Service:Ideogram:Host") + "generate";
        imageHost = configuration.GetConfig<string>("Service:Ideogram:Image_Host");
    }
    private String hostUrl;
    private String imageHost;
    private String APIKEY;
    private string modelName = "V_2";

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
                Type = "风格", Contents = new []
                {
                    new KeyValuePair<string, string>("通用", "GENERAL"),
                    new KeyValuePair<string, string>("真实", "REALISTIC"),
                    new KeyValuePair<string, string>("设计", "DESIGN"),
                    new KeyValuePair<string, string>("3D卡通", "RENDER_3D"),
                    new KeyValuePair<string, string>("漫画", "ANIME")
                }
            },
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "ASPECT_1_1"),
                    new KeyValuePair<string, string>("横屏", "ASPECT_4_3"),
                    new KeyValuePair<string, string>("竖屏", "ASPECT_3_4"),
                    new KeyValuePair<string, string>("宽横屏", "ASPECT_16_10"),
                    new KeyValuePair<string, string>("长竖屏", "ASPECT_10_16")
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
        var url = hostUrl;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Api-Key",APIKEY);
        var options = GetExtraOptions(input.External_UserId);
        var msg = JsonConvert.SerializeObject(new
        {
            image_request = new {
                prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
                aspect_ratio = options[1].CurrentValue,
                style_type = options[0].CurrentValue,
                model = modelName
            }
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["data"] != null)
        {
            var arr = json["data"] as JArray;
            foreach (JToken item in arr)
            {
                var image_url = item["url"].Value<string>();
                image_url = image_url.Replace("https://ideogram.ai/", imageHost);
                var bytes = await client.GetByteArrayAsync(image_url);
                yield return FileResult.Answer(bytes, "png",
                    ResultType.ImageBytes);
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
}
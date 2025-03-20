using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.LumaAi视频, "LumaAi视频", "Luma.ai文生视频，即Dream Machine文生视频服务，目前质量一般，支持中文提示词，支持图生视频，注意图片和文字描述要一起发送，不可以分开发送。速度比较快，略贵，2块多一条。", 222, type: ApiClassTypeEnum.视频模型, priceIn:0, priceOut: 3)]
public class ApiLumaAiVideo:ApiBase
{
    protected LumaAiClient _client;
    public ApiLumaAiVideo(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<LumaAiClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach(var resp in _client.GenerateVideo(input))
            yield return resp;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("视频接口不支持Query调用");
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

[ApiClass(M.LumaAi画图, "LumaAi画图",
    "Luma.ai文生图，即Photon，号称超越Flux的文生图，比较便宜，1毛多一张，速度很慢，而且似乎不太稳定，支持中文。", 211,
    type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.15)]
public class ApiLumaAiImage : ApiLumaAiVideo
{
    public ApiLumaAiImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<LumaAiClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach(var resp in _client.GenerateImage(input))
            yield return resp;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
}


/// <summary>
/// LumaAiClient API接口
/// 文档地址 https://docs.lumalabs.ai/reference/creategeneration
/// </summary>
public class LumaAiClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IServiceProvider _serviceProvider;
    public LumaAiClient(IHttpClientFactory httpClientFactory, ConfigHelper configuration, IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        
        APIKEY = configuration.GetConfig<string>("Service:LumaAI:Key");
        hostUrl = configuration.GetConfig<string>("Service:LumaAI:Host");
        cdnHost = configuration.GetConfig<string>("Service:LumaAI:CdnHost");
    }
    private String hostUrl;
    private String cdnHost;
    private String APIKEY;
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1:1"),
                    new KeyValuePair<string, string>("横屏", "4:3"),
                    new KeyValuePair<string, string>("竖屏", "3:4"),
                    new KeyValuePair<string, string>("宽横屏", "16:9"),
                    new KeyValuePair<string, string>("长竖屏", "9:16")
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
    public async IAsyncEnumerable<Result> GenerateVideo(ApiChatInputIntern input)
    {
        var ctx = input.ChatContexts.Contexts.Last();
        if (ctx.QC.All(t => t.Type != ChatType.文本))
        {
            yield return Result.Error("缺少视频内容描述提示词。如果要使用图片生成视频功能，务必图片和文字一起发送。");
            yield break;
        }

        if (ctx.QC.Any(t => t.Type == ChatType.图片Base64))
        {
            var qc = ctx.QC.First(t => t.Type == ChatType.图片Base64);
            var fileService = _serviceProvider.GetRequiredService<IOssFileService>();
            var file = fileService.UploadFile("images.jpg", new MemoryStream(Convert.FromBase64String(qc.Content)), input.UserId);
            var ossPath = fileService.GetFileFullUrl(file.FilePath);
            qc.Content = ossPath;
            qc.Type = ChatType.图片Url;
        }
        var url = hostUrl+"dream-machine/v1/generations";
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var options = GetExtraOptions(input.External_UserId);
        var image = ctx.QC.FirstOrDefault(t => t.Type == ChatType.图片Url)?.Content;
        var keyframes = image == null
            ? null
            : new
            {
                frame0 = new
                {
                    type = "image", url = image
                }
            };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = ctx.QC.First(t => t.Type == ChatType.文本).Content,
            aspect_ratio = options[0].CurrentValue,
            loop = false,
            keyframes
        }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["id"] != null)
        {
            var id = json["id"].Value<string>();
            int times = 0;
            while (true)
            {
                url = hostUrl + "dream-machine/v1/generations/" + id;
                resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                content = await resp.Content.ReadAsStringAsync();
                json = JObject.Parse(content);
                var state = json["state"].Value<string>();
                if (state == "completed")
                {
                    yield return Result.Waiting("生成完成，下载视频...");
                    var image_url = json["assets"]["video"].Value<string>();
                    image_url = image_url.Replace("https://storage.cdn-luma.com/", cdnHost);
                    var bytes = await client.GetByteArrayAsync(image_url);
                    yield return VideoFileResult.Answer(bytes, "mp4",
                        "video.mp4", duration: 5000);
                    break;
                }
                else if (state == "queued" || state == "dreaming")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else
                {
                    yield return Result.Error(content);
                    break;
                }
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> GenerateImage(ApiChatInputIntern input)
    {
        var ctx = input.ChatContexts.Contexts.Last();
        if (ctx.QC.All(t => t.Type != ChatType.文本))
        {
            yield return Result.Error("请输入文生图提示词。如果要使用参考图片功能，务必图片和文字一起发送。");
            yield break;
        }

        if (ctx.QC.Any(t => t.Type == ChatType.图片Base64))
        {
            var qc = ctx.QC.First(t => t.Type == ChatType.图片Base64);
            var fileService = _serviceProvider.GetRequiredService<IOssFileService>();
            var file = fileService.UploadFile("images.jpg", new MemoryStream(Convert.FromBase64String(qc.Content)), input.UserId);
            var ossPath = fileService.GetFileFullUrl(file.FilePath);
            qc.Content = ossPath;
            qc.Type = ChatType.图片Url;
        }
        var url = hostUrl+"dream-machine/v1/generations/image";
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var options = GetExtraOptions(input.External_UserId);
        var image = ctx.QC.FirstOrDefault(t => t.Type == ChatType.图片Url)?.Content;
        var image_ref = image == null
            ? null
            : new[] { new { url = image } };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = ctx.QC.First(t => t.Type == ChatType.文本).Content,
            aspect_ratio = options[0].CurrentValue,
            image_ref
        }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["id"] != null)
        {
            var id = json["id"].Value<string>();
            int times = 0;
            while (times < 50)
            {
                url = hostUrl + "dream-machine/v1/generations/" + id;
                resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                if (resp.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                    continue;
                }
                content = await resp.Content.ReadAsStringAsync();
                json = JObject.Parse(content);
                var state = json["state"].Value<string>();
                if (state == "completed")
                {
                    yield return Result.Waiting("生成完成，下载图片...");
                    var image_url = json["assets"]["image"].Value<string>();
                    image_url = image_url.Replace("https://storage.cdn-luma.com/", cdnHost);
                    var bytes = await client.GetByteArrayAsync(image_url);
                    yield return FileResult.Answer(bytes, "jpg", ResultType.ImageBytes);
                    break;
                }
                else if (state == "queued" || state == "dreaming")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else
                {
                    yield return Result.Error(content);
                    break;
                }
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
}
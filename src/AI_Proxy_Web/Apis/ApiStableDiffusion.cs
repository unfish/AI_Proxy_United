using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.StableDiffusion3, "SD 3", "Stable Diffusion 3，文本画图开源届的老大，V3版可以准确处理英文字母，不支持中文指令。", 209, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 1)]
public class ApiStableDiffusion:ApiBase
{
    private StableDiffusionClient _client;
    public ApiStableDiffusion(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<StableDiffusionClient>();
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
}

/// <summary>
/// StableDiffusion API接口
/// 文档地址 https://platform.stability.ai/docs/api-reference
/// </summary>
public class StableDiffusionClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public StableDiffusionClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        
        APIKEY = configHelper.GetConfig<string>("Service:Stability:Key");
        hostUrl = configHelper.GetConfig<string>("Service:Stability:Host");
    }
    private String hostUrl;
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
                    new KeyValuePair<string, string>("横屏", "3:2"),
                    new KeyValuePair<string, string>("竖屏", "2:3"),
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
    public async IAsyncEnumerable<Result> SendMessage(ApiChatInputIntern input)
    {
        var url = hostUrl;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        client.DefaultRequestHeaders.Add("accept","image/*");
        var boundary = DateTime.Now.Ticks.ToString("X");
        var content = new MultipartFormDataContent(boundary);
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);
        var cnt = new StringContent("png");
        content.Add(cnt, "output_format");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"output_format\";");
        cnt = new StringContent(input.ChatContexts.Contexts.Last().QC.Last().Content);
        content.Add(cnt, "prompt");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"prompt\";");
        cnt = new StringContent(GetExtraOptions(input.External_UserId)[0].CurrentValue);
        content.Add(cnt, "aspect_ratio");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"aspect_ratio\";");

        var resp = await client.PostAsync(url, content);
        if (resp.IsSuccessStatusCode)
        {
            var response = await resp.Content.ReadAsByteArrayAsync();
            yield return FileResult.Answer(response, "png", ResultType.ImageBytes);
        }
        else
        {
            var response = await resp.Content.ReadAsStringAsync();
            yield return Result.Error(response);
        }
    }
    
}
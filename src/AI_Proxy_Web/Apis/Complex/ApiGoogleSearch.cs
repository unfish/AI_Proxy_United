using System.Net;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Google搜索, "Google搜索", "直接调用Google搜索接口，输入搜索词返回网站搜索结果的标题和链接，收费接口。", 186, type: ApiClassTypeEnum.辅助模型, priceIn: 0, priceOut: 0.1)]
public class ApiGoogleSearch:ApiBase
{
    private IServiceProvider _serviceProvider;
    private GoogleSearchClient _client;
    public ApiGoogleSearch(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<GoogleSearchClient>();
        _serviceProvider = serviceProvider;
    }

    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var res = await _client.SendMessage(input);
        if (res.resultType == ResultType.GoogleSearchResult)
        {
            var _apiFactory = _serviceProvider.GetRequiredService<IApiFactory>();
            var api2 = _apiFactory.GetService(DI.GetApiClassAttributeId(typeof(ApiJinaAi)));
            var result = ((GoogleSearchResult)res).result;
            await Parallel.ForEachAsync(
                result.Where(dto => !string.IsNullOrEmpty(dto.url) && dto.url.StartsWith("https://")),
                new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                async (dto, token) =>
                {
                    var res2 = await api2.ProcessQuery(new ApiChatInputIntern()
                        { ChatContexts = ChatContexts.New(dto.url)});

                    if (res2.resultType == ResultType.JinaArticle)
                    {
                        var con = ((JinaArticleResult)res2).result.Content;
                        if (!string.IsNullOrEmpty(con))
                            dto.content = con;
                    }
                });
        }

        yield return res;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var res = await _client.SendMessage(input);
        if (res.resultType == ResultType.GoogleSearchResult)
        {
            var _apiFactory = _serviceProvider.GetRequiredService<IApiFactory>();
            var api2 = _apiFactory.GetService(DI.GetApiClassAttributeId(typeof(ApiJinaAi)));
            var result = ((GoogleSearchResult)res).result;
            await Parallel.ForEachAsync(
                result.Where(dto => !string.IsNullOrEmpty(dto.url) && dto.url.StartsWith("https://")),
                new ParallelOptions(){MaxDegreeOfParallelism = 10},
                async (dto, token) =>
                {
                    var res2 = await api2.ProcessQuery(new ApiChatInputIntern()
                        { ChatContexts = ChatContexts.New(dto.url) });

                    if (res2.resultType == ResultType.JinaArticle)
                    {
                        var con = ((JinaArticleResult)res2).result.Content;
                        if (!string.IsNullOrEmpty(con))
                            dto.content = con;
                    }
                });
        }
        return res;
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
}


[ApiClass(M.搜索摘要, "搜索摘要", "调用Google搜索接口进行搜索，然后对返回的结果通过另一个大模型进行总结摘要，只返回摘要内容。", 192, type: ApiClassTypeEnum.辅助模型, priceIn: 0, priceOut: 0.1)]
public class ApiSearchAndSummarize:ApiBase
{
    private IServiceProvider _serviceProvider;
    public ApiSearchAndSummarize(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private int _SearchModel = (int)M.Google搜索;
    private int _SummarizeModel = (int)M.MiniMax大杯;
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var apiFactory = _serviceProvider.GetRequiredService<IApiFactory>();
        var searchApi = apiFactory.GetService(_SearchModel);
        await foreach (var res in searchApi.ProcessChat(input))
        {
            if (res.resultType == ResultType.GoogleSearchResult)
            {
                var results = ((GoogleSearchResult)res).result;
                if (results.Count > 0)
                {
                    var sb = new StringBuilder();
                    var waitMsgs = new StringBuilder();
                    var q = input.ChatContexts.Contexts.Last().QC.First().Content;
                    waitMsgs.AppendLine($"\n>正在阅读关于{q}的网页资料：");
                    sb.AppendLine("请根据以下参考资料，回答该问题：" +
                                  input.ChatContexts.Contexts.Last().QC.Last().Content);
                    sb.AppendLine("<refers>");
                    foreach (var dto in results)
                    {
                        sb.Append(
                            $"<refer><title>{dto.title}</title><url>{dto.url}></url><content>{dto.content}</content></refer>");
                        waitMsgs.AppendLine($"[{dto.title}]({dto.url})");
                    }
                    sb.AppendLine("</refers>");
                    sb.AppendLine(
                        "字数控制在1000-2000字左右，详略得当，关键数据与结论需要保留。请严格遵循参考资料内容，参考资料中找不到对应的答案的直接返回'搜索结果中没有找到对应问题的答案'");
                    waitMsgs.AppendLine("");
                    yield return Result.Reasoning(waitMsgs.ToString());
                    
                    input.ChatContexts = ChatContexts.New(sb.ToString());
                    input.IgnoreSaveLogs = true;
                    input.IgnoreAutoContexts = true;
                    input.Temprature = (decimal)0.2;
                    var summarizeApi = apiFactory.GetService(_SummarizeModel);
                    sb.Clear();
                    await foreach (var res2 in summarizeApi.ProcessChat(input))
                    {
                        if (res2.resultType == ResultType.Answer)
                        {
                            sb.Append(res2.ToString());
                        }
                        else
                        {
                            yield return res2;
                        }
                    }
                    if(sb.Length>0)
                        yield return Result.New(ResultType.FunctionResult, sb.ToString());
                    else
                        yield return Result.New(ResultType.FunctionResult, "出现未知错误。");
                }
                else
                {
                    yield return Result.New(ResultType.FunctionResult, "该关键词没有找到符合条件的搜索结果，请修改关键词并重试。");
                }
            }
            else
            {
                yield return res;
            }
        }
    }
    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该接口不支持Query调用");
    }
}

/// <summary>
/// GoogleSearch接口
/// 文档地址 https://developers.google.com/custom-search/v1/overview?hl=zh-cn
/// </summary>
public class GoogleSearchClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public GoogleSearchClient(IHttpClientFactory httpClientFactory, ConfigHelper configuration)
    {
        _httpClientFactory = httpClientFactory;
        hostUrl = configuration.GetConfig<string>("GoogleSearch:Host") + "customsearch/v1";
        APIKEY = configuration.GetConfig<string>("GoogleSearch:Key"); 
        cx = configuration.GetConfig<string>("GoogleSearch:cx");
    }
    
    private string hostUrl;
    private string APIKEY;
    private string cx;

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            var url = hostUrl + "?q=" + HttpUtility.UrlEncode(input.ChatContexts.Contexts.Last().QC.First().Content) +
                      "&num=6&key=" + APIKEY + "&cx=" + cx;
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["items"] != null)
            {
                var list = new List<GoogleSearchResultDto>();
                var hits = json["items"] as JArray;
                if (hits != null)
                {
                    foreach (var hit in hits)
                    {
                        var dto = new GoogleSearchResultDto()
                        {
                            title = hit["title"].Value<string>(),
                            url = hit["link"].Value<string>(),
                            content = hit["snippet"].Value<string>()
                        };
                        list.Add(dto);
                    }
                }

                return GoogleSearchResult.Answer(list);
            }
            else
            {
                if (json["searchInformation"]["totalResults"].Value<string>() == "0")
                {
                    return GoogleSearchResult.Answer(new List<GoogleSearchResultDto>());
                }
                else
                    return Result.Error(content);
            }
        }
        catch (Exception ex)
        {
            return Result.Error(ex.Message);
        }
    }
}

public class GoogleSearchResultDto
{
    public string title { get; set; }
    public string content { get; set; }
    public string url { get; set; }
}
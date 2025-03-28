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
        yield return await DoProcessQuery(input);
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var res = await _client.SendMessage(input);
        if (res.resultType == ResultType.SearchResult)
        {
            var _apiFactory = _serviceProvider.GetRequiredService<IApiFactory>();
            var api2 = _apiFactory.GetService(DI.GetApiClassAttributeId(typeof(ApiJinaAi)));
            var result = ((SearchResult)res).result;
            await Parallel.ForEachAsync(
                result.Where(dto => !string.IsNullOrEmpty(dto.url) && dto.url.StartsWith("https://")),
                new ParallelOptions(){MaxDegreeOfParallelism = 10},
                async (dto, token) =>
                {
                    var res2 = await api2.ProcessQuery(new ApiChatInputIntern()
                        { ChatContexts = ChatContexts.New(dto.url), ChatModel = (int)M.JinaReader });

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


/// <summary>
/// GoogleSearch接口
/// 文档地址 https://ohmygpt-docs.apifox.cn/api-141553709
/// </summary>
public class GoogleSearchClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public GoogleSearchClient(IHttpClientFactory httpClientFactory, ConfigHelper configuration)
    {
        _httpClientFactory = httpClientFactory;
        hostUrl = configuration.GetConfig<string>("Service:GoogleSearch:Host") + "customsearch/v1";
        APIKEY = configuration.GetConfig<string>("Service:GoogleSearch:Key"); 
        cx = configuration.GetConfig<string>("Service:GoogleSearch:cx");
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
            if (json["items"] is not null)
            {
                var list = new List<SearchResultDto>();
                var hits = json["items"] as JArray;
                if (hits != null)
                {
                    foreach (var hit in hits)
                    {
                        var dto = new SearchResultDto()
                        {
                            title = hit["title"].Value<string>(),
                            url = hit["link"].Value<string>(),
                            content = hit["snippet"].Value<string>()
                        };
                        list.Add(dto);
                    }
                }

                return SearchResult.Answer(list);
            }
            else
            {
                if (json["searchInformation"] is not null && json["searchInformation"]["totalResults"] is not null && json["searchInformation"]["totalResults"].Value<string>() == "0")
                {
                    return SearchResult.Answer(new List<SearchResultDto>());
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

public class SearchResultDto
{
    public string title { get; set; }
    public string content { get; set; }
    public string url { get; set; }
}
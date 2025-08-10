using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("GoogleSearch")]
public class ApiGoogleSearchProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiGoogleSearchProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private string cx = string.Empty;
    private int jinaReaderId;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "customsearch/v1";
        cx = configHelper.GetProviderConfig<string>(attr.Provider, "cx");
        jinaReaderId = configHelper.GetProviderConfig<int>(attr.Provider, "JinaReaderId");
    }
    
    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        yield return await SendMessage(input);
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        var res = await DoSearch(input);
        if (res.resultType == ResultType.SearchResult && jinaReaderId>0)
        {
            var _apiFactory = serviceProvider.GetRequiredService<IApiFactory>();
            var api2 = _apiFactory.GetApiCommon(jinaReaderId);
            var result = ((SearchResult)res).result;
            await Parallel.ForEachAsync(
                result.Where(dto => !string.IsNullOrEmpty(dto.url) && dto.url.StartsWith("https://")),
                new ParallelOptions(){MaxDegreeOfParallelism = 10},
                async (dto, token) =>
                {
                    var res2 = await api2.ProcessQuery(new ApiChatInputIntern()
                        { ChatContexts = ChatContexts.New(dto.url), ChatModel = DI.GetModelIdByName("JinaReader") });

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

    private async Task<Result> DoSearch(ApiChatInputIntern input)
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            var url = _chatUrl + "?q=" + HttpUtility.UrlEncode(input.ChatContexts.Contexts.Last().QC.First().Content) +
                      "&num=6&key=" + _key + "&cx=" + cx;
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
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
}

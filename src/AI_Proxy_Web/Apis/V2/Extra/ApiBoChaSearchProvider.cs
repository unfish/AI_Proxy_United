using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("BoChaSearch")]
public class ApiBoChaSearchProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiBoChaSearchProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private int jinaReaderId;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "web-search";
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
            client.DefaultRequestHeaders.Add("Authorization",$"Bearer {_key}");
            var url = _chatUrl;
            var msg = JsonConvert.SerializeObject(new
            {
                query = input.ChatContexts.Contexts.Last().QC.First().Content,
                freshness = "noLimit",
                summary = false,
                count = 6
            });
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(msg, Encoding.UTF8, "application/json")
            });

            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["code"].Value<int>()==200)
            {
                var list = new List<SearchResultDto>();
                var hits = json["data"]["webPages"]["value"] as JArray;
                if (hits != null)
                {
                    foreach (var hit in hits)
                    {
                        var dto = new SearchResultDto()
                        {
                            title = hit["name"].Value<string>(),
                            url = hit["url"].Value<string>(),
                            content = hit["snippet"].Value<string>()
                        };
                        list.Add(dto);
                    }
                }

                return SearchResult.Answer(list);
            }
            else
            {
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

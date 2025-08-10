using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("Qwen")]
public class ApiQwenProvider : ApiOpenAIProvider
{
    public ApiQwenProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            model = apiClassAttribute.EmbeddingModelName,
            input = new { texts = embeddings },
            parameters = new
            {
                text_type = embedForQuery ? "query" : "document"
            }
        });
    }
    private class EmbeddingsResponse
    {
        public EmbeddingsOutput Output { get; set; }
    } 
    private class EmbeddingsOutput
    {
        public EmbeddingObject[] Embeddings { get; set; }
    }
    private class EmbeddingObject
    {
        public double[] Embedding { get; set; }
        public int text_index { get; set; }
    }
    
    private static String embedUrl = "https://dashscope.aliyuncs.com/api/v1/services/embeddings/text-embedding/text-embedding";
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = embedUrl;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.Output.Embeddings.Select(t => t.Embedding).ToArray(), string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
}
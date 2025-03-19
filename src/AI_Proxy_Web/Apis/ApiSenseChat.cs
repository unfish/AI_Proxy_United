using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.商汤日日新, "商汤日日新", "商汤日日新V5 是商汤出品的大模型，测评排名靠前，接近GPT4，支持Function Call功能，支持图片问答。", 49,  canProcessImage:true, canProcessMultiImages:true, priceIn: 40, priceOut: 100)]
public class ApiSenseChat:ApiBase
{
    private SenseChatClient _client;
    public ApiSenseChat(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<SenseChatClient>();
    }
    
    /// <summary>
    /// 使用SenseChat来回答
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
    /// 使用SenseChat来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 可输入数组，总长度4000 token以内，返回向量长度1536
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}



/// <summary>
/// SenseChat大模型接口
/// 文档地址 https://platform.sensenova.cn/doc?path=/chat/ChatCompletions/ChatCompletions.md
/// </summary>
public class SenseChatClient: OpenAIClientBase, IApiClient
{
    private IFunctionRepository _functionRepository;
    private IHttpClientFactory _httpClientFactory;

    public SenseChatClient(IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _functionRepository = functionRepository;
        _httpClientFactory = httpClientFactory;
        APIKEY = configHelper.GetConfig<string>("Service:SenseChat:Key");
    }
    
    private static String hostUrl = "https://api.sensenova.cn/v1/llm/chat-completions";
    private String APIKEY;//从开放平台控制台中获取

    private string GetJwtToken()
    {
        var header = "{\"typ\":\"JWT\",\"alg\":\"HS256\"}";
        var ss = APIKEY.Split(".");
        var payload = JsonConvert.SerializeObject(new
        {
            iss = ss[0], exp = GetUnixSeconds(DateTime.Now.AddMinutes(10)), nbf = GetUnixSeconds(DateTime.Now.AddMinutes(-1))
        });
        var bHeader = Base64UrlEncode(header);
        var bPayload = Base64UrlEncode(payload);
        var t = Base64UrlEncode(HmacSha256($"{bHeader}.{bPayload}", ss[1]));
        return $"{bHeader}.{bPayload}.{t}";
    }   
    private static long GetUnixSeconds(DateTime dt)
    {
        return new DateTimeOffset(dt).ToUnixTimeMilliseconds()/1000;
    }
    private static string Base64UrlEncode(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        return Base64UrlEncode(bytes);
    }
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);
    }
    private static byte[] HmacSha256(string str, string key)
    {
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(str));
    }
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    private string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts, useSystem:false);
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? "SenseChat-Vision" : "SenseChat-5";
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            tools,
            temperature = input.Temprature,
            max_new_tokens = 2048,
            stream
        }, jSetting);
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var url = hostUrl;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetJwtToken()}");
        var msg = GetMsgBody(input, true);
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        string funcId = "";
        string funcName = "";
        StringBuilder funcArgs = new StringBuilder();
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(line);
                yield break;
            }
            while ((line = await reader.ReadLineAsync()) != null)
            {
                //Console.WriteLine(line);
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);

                line = line.TrimStart();
                if (line == "[DONE]"||line.StartsWith(":"))
                {
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var res = JObject.Parse(line)["data"];
                    if (res["choices"]!=null && !(res["choices"] as JArray is null) && res["choices"][0] != null)
                    {
                        if (res["choices"][0]["tool_calls"] != null)
                        {
                            var tools = res["choices"][0]["tool_calls"] as JArray;
                            foreach (var tool in tools)
                            {
                                if (tool["id"] != null)
                                {
                                    if (!string.IsNullOrEmpty(funcId))
                                    {
                                        functionCalls.Add(new FunctionCall(){Id = funcId, Name = funcName, Arguments = funcArgs.ToString()});
                                        funcArgs.Clear();
                                    }
                                    funcId = tool["id"].Value<string>();
                                }

                                if (tool["function"]["name"] != null)
                                {
                                    funcName = tool["function"]["name"].Value<string>();
                                }
                                if(tool["function"]["arguments"]!=null)
                                    funcArgs.Append(tool["function"]["arguments"].Value<string>());
                            }
                        }
                        else if (res["choices"][0]["delta"] != null)
                        {
                            yield return Result.Answer(res["choices"][0]["delta"].ToString());
                        }
                    }
                    else
                    {
                        yield return Result.Error(line);
                    }
                }
            }
        }
        if (!string.IsNullOrEmpty(funcId))
        {
            functionCalls.Add(new FunctionCall(){Id = funcId, Name = funcName, Arguments = funcArgs.ToString()});
            yield return FunctionsResult.Answer(functionCalls);
        }
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        var url = hostUrl;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetJwtToken()}");
        var msg = GetMsgBody(input, false);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if(content.StartsWith("{\"error\":"))
            return Result.Error(content);
        
        var json = JObject.Parse(content)["data"];
        if (json["choices"][0]["tool_calls"] != null)
        {
            List<FunctionCall> functionCalls = new List<FunctionCall>();
            var funcs = json["choices"][0]["tool_calls"] as JArray;
            foreach (var func in funcs)
            {
                functionCalls.Add(new FunctionCall() {Name = func["function"]["name"].ToString(), Arguments = func["function"]["arguments"].ToString(), Id = func["id"].ToString()});
            }
            if (functionCalls.Count>0)
            {
                return FunctionsResult.Answer(functionCalls);
            }
        }

        if (json["choices"][0]["message"] != null)
            return Result.Answer(json["choices"][0]["message"].Value<string>());
        else
            return Result.Error(content);
    }
    
    
    private static String embedUrl = "https://api.sensenova.cn/v1/llm/embeddings";
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings,
            model = "nova-embedding-stable"
        });
    }
    private class EmbeddingsResponse
    {
        public EmbeddingObject[] Embeddings { get; set; }
    }
    private class EmbeddingObject
    {
        public double[] Embedding { get; set; }
        public int Index { get; set; }
    }
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetJwtToken()}");
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, embedUrl)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.Embeddings.Select(t => t.Embedding).ToArray(), string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
}
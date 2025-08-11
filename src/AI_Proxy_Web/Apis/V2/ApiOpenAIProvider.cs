using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("OpenAI")]
public class ApiOpenAIProvider : ApiProviderBase
{
    protected IFunctionRepository _functionRepository;
    protected IHttpClientFactory _httpClientFactory;
    public ApiOpenAIProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _functionRepository = functionRepository;
        _httpClientFactory = httpClientFactory;
    }
    
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "chat/completions";
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    private string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var jSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? _visionModelName : _modelName;
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        if (input.AgentSystem == "web")
            tools = GetWebControlTools();
        var msgs = GetFullMessages(input.ChatContexts);
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            tools,
            temperature = _useThinkingMode ? 1 : input.Temprature,
            max_completion_tokens = _maxTokens,
            user = input.External_UserId,
            stream
        }, jSetting);
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        if (_modelName == "o1")
        {
            yield return Result.Waiting("正在思考，请耐心等待...");
            yield return await SendMessage(input);
            yield break;
        }

        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        var url = _chatUrl;
        var msg = GetMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        await foreach (var resp in ProcessStreamResponse(response))
            yield return resp;
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+_key);
        var url = _chatUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }

    protected bool IsImageMsg(ChatContexts chatContexts)
    {
        return chatContexts.HasImage();
    }
    
    /// <summary>
    /// 标准OpenAI格式完整消息列表格式，包含VL图像功能，和Function功能
    /// </summary>
    /// <param name="chatContexts"></param>
    /// <param name="useSystem"></param>
    /// <returns></returns>
    protected List<Message> GetFullMessages(ChatContexts chatContexts, bool useSystem = true)
    {
        bool isImageMsg = false;
        var msgs = new List<Message>();
        if (!string.IsNullOrEmpty(chatContexts.SystemPrompt) && useSystem)
        {
            msgs.Add(new TextMessage()
            {
                role = "system", content = chatContexts.SystemPrompt
            });
        }
        var resultImageIndex = 0;
        var resultHtmlIndex = 0;
        foreach (var ctx in chatContexts.Contexts)
        {
            isImageMsg = ctx.QC.Any(x => x.Type == ChatType.图片Base64 || x.Type == ChatType.图片Url);
            if (isImageMsg)
            {
                List<VisionMessageContent> contents = new List<VisionMessageContent>();
                foreach (var qc in ctx.QC)
                {
                    if (qc.Type == ChatType.图片Base64)
                    {
                        contents.Add(new VisionMessageContent()
                            { Type = "image_url", ImageUrl = new VisionMessageImageUrl() { url = $"data:{(string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType)};base64," + qc.Content } });
                    }
                    else  if (qc.Type == ChatType.图片Url)
                    {
                        contents.Add(new VisionMessageContent()
                            { Type = "image_url", ImageUrl = new VisionMessageImageUrl() { url = qc.Content } });
                    }
                    else if (qc.Type == ChatType.文本 || qc.Type== ChatType.提示模板 || qc.Type== ChatType.图书全文)
                    {
                        contents.Add(new VisionMessageContent() { Type = "text", Text = qc.Content });
                    }
                }
                msgs.Add(new VisionMessage {role = "user", content = contents.ToList()});
            }
            else
            {
                foreach (var qc in ctx.QC)
                {
                    if (qc.Type == ChatType.文本 || qc.Type== ChatType.提示模板 || qc.Type== ChatType.图书全文)
                    {
                        msgs.Add(new TextMessage()
                        {
                            role = "user", content = qc.Content
                        });
                    }
                }
            }

            //处理输出部分的参数
            foreach (var ac in ctx.AC)
            {
                if (ac.Type == ChatType.文本 && !string.IsNullOrEmpty(ac.Content))
                {
                    msgs.Add(new AssistantMessage()
                    {
                        role = "assistant", content = ac.Content
                    });
                }
                else if (ac.Type == ChatType.FunctionCall && !string.IsNullOrEmpty(ac.Content))
                {
                    var acalls = JsonConvert.DeserializeObject<List<FunctionCall>>(ac.Content);
                    msgs.Add(new AssistantToolsMessage()
                    {
                        role = "assistant", tool_calls = acalls.Select(t => new ToolCall()
                        {
                            Id = t.Id, Type = "function",
                            Function = new FunctionCall() { Name = t.Name, Arguments = t.Arguments }
                        }).ToList()
                    });
                    foreach (var call in acalls)
                    {
                        if (call.Result != null && call.Result.resultType == ResultType.ImageBytes)
                        {
                            resultImageIndex++;
                            if (resultImageIndex >= chatContexts.ResultImagesCount - 2) //带上最近3张图片
                            {
                                var result = (FileResult)call.Result;
                                msgs.Add(new ToolMessage()
                                {
                                    role = "tool",
                                    content = new[]
                                    {
                                        new
                                        {
                                            type = "image",
                                            source = new
                                            {
                                                type = "base64",
                                                media_type = result.fileExt == "png" ? "image/png" : "image/jpeg",
                                                data = result.ToString()
                                            }
                                        }
                                    },
                                    tool_call_id = call.Id
                                });
                            }
                            else
                            {
                                msgs.Add(new ToolMessage()
                                {
                                    role = "tool",
                                    content = "",
                                    tool_call_id = call.Id
                                });
                            }
                        }
                        else
                        {
                            if (call.Name == "GetPageHtml")
                                resultHtmlIndex++;
                            msgs.Add(new ToolMessage()
                            {
                                role = "tool",
                                content = (call.Name != "GetPageHtml" ||
                                           resultHtmlIndex == chatContexts.ResultFullHtmlCount)
                                    ? call.Result.ToString()
                                    : "",
                                tool_call_id = call.Id
                            });
                        }
                    }
                }
            }
        }
        
        return msgs;
    }

    protected List<ToolParamter>? GetToolParamters(string[]? funcNames, IFunctionRepository functionRepository, out string prompt)
    {
        var functions = functionRepository.GetFunctionList(funcNames);
        prompt = "";
        if (functions.Any(t => !string.IsNullOrEmpty(t.Prompt)))
        {
            prompt = "注意事项：" + string.Join("\n",
                functions.Where(t => !string.IsNullOrEmpty(t.Prompt)).Select(t => t.Prompt));
        }

        return functions.Any()
            ? functions.Select(t => (ToolParamter) new FunctionToolParamter()
            {
                function = t
            }).ToList()
            : null;
    }

    protected List<ToolParamter> GetWebControlTools()
    {
        var tools = new List<ToolParamter>();
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "OpenUrl", Description= "Use the current web browser to open an URL.", Parameters= new
                {
                    type="object", required=new[]{"url"}, properties = new
                    {
                        url = new{type="string", description="the full URL need to be opened."}
                    }
                }
            }
        });
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "GetPageHtml", Description= "Get full html content of current web page.", Parameters= new
                {
                    type="object", properties = new {}
                }
            }
        });
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "Screenshot", Description= "Take a screenshot of current web page and send it to user.", Parameters= new
                {
                    type="object", properties = new {}
                }
            }
        });
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "GoBack", Description= "Let the web browser go back to previous page.", Parameters= new
                {
                    type="object", properties = new {}
                }
            }
        });
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "ClickElement", Description= "Click an element in page by css xpath selector.", Parameters= new
                {
                    type="object", required=new[]{"selector"}, properties = new
                    {
                        selector = new{type="string", description="The xpath selector for the element."}
                    }
                }
            }
        });
        tools.Add(new FunctionToolParamter()
        {
            function = new()
            {
                Name= "InputElement", Description= "Input text to an element in page by css xpath selector.", Parameters= new
                {
                    type="object", required=new[]{"selector", "text"}, properties = new
                    {
                        selector = new{type="string", description="The xpath selector for the element."},
                        text = new{type="string", description="The text content need input to the element."},
                    }
                }
            }
        });
        return tools;
    }

    public async IAsyncEnumerable<Result> ProcessStreamResponse(HttpResponseMessage resp)
    {
        string funcId = "";
        string funcName = "";
        StringBuilder funcArgs = new StringBuilder();
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        bool reasoning = false;
        bool answering = false;
        using (var stream = await resp.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(resp.StatusCode + " : " + line);
                yield break;
            }

            while ((line = await reader.ReadLineAsync()) != null)
            {
                //Console.WriteLine(line);
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);
                line = line.TrimStart();

                if (line == "[DONE]")
                {
                    break;
                }
                else if (line.StartsWith(":")) //通常用于返回注释
                {
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var res = JObject.Parse(line);
                    if (res["choices"] != null && (res["choices"] as JArray).Count>0)
                    {
                        var tk = res["choices"][0]["delta"];
                        if (tk != null)
                        {
                            if (tk["tool_calls"] != null && (tk["tool_calls"] as JArray != null) && (tk["tool_calls"] as JArray).Count>0)
                            {
                                var tools = tk["tool_calls"] as JArray;
                                foreach (var tool in tools)
                                {
                                    if (tool["id"] != null && !string.IsNullOrEmpty(tool["id"].Value<string>()) && funcId != tool["id"].Value<string>())
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
                                        if(!string.IsNullOrEmpty(tool["function"]["name"].Value<string>()))
                                            funcName = tool["function"]["name"].Value<string>();
                                    }
                                    if(tool["function"]["arguments"]!=null)
                                        funcArgs.Append(tool["function"]["arguments"].Value<string>());
                                }
                            }
                            else if (tk["content"] != null && !string.IsNullOrEmpty(tk["content"].Value<string>()))
                            {
                                var content = tk["content"].Value<string>();
                                if (reasoning)
                                {
                                    if (content.IndexOf("</think>", StringComparison.Ordinal) >= 0)
                                    {
                                        reasoning = false;
                                        var reason = content.Substring(0, content.IndexOf("</think>", StringComparison.Ordinal));
                                        yield return Result.Reasoning(reason);
                                        if (content.Length > reason.Length + "</think>".Length)
                                        {
                                            var answer = content.Substring(reason.Length + "</think>".Length).Trim();
                                            if (answer.Length > 0)
                                                yield return Result.Answer(answer);
                                        }
                                    }
                                    else
                                    {
                                        yield return Result.Reasoning(content);
                                    }
                                }
                                else
                                {
                                    if (!answering && content.IndexOf("<think>", StringComparison.Ordinal) >= 0 && content.IndexOf("</think>", StringComparison.Ordinal) < 0)
                                    {
                                        reasoning = true;
                                        var reason = content.Substring(
                                            content.IndexOf("<think>", StringComparison.Ordinal) + "<think>".Length);
                                        yield return Result.Reasoning(reason);
                                    }
                                    else
                                    {
                                        answering = true;
                                        yield return Result.Answer(content);
                                    }
                                }
                            }
                            else if (tk["reasoning_content"] != null && !string.IsNullOrEmpty(tk["reasoning_content"].Value<string>()))
                            {
                                var reason = tk["reasoning_content"].Value<string>();
                                yield return Result.Reasoning(reason);
                            }
                            else if (tk["reasoning"] != null && !string.IsNullOrEmpty(tk["reasoning"].Value<string>()))
                            {
                                var reason = tk["reasoning"].Value<string>();
                                yield return Result.Reasoning(reason);
                            }
                        }
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

    public async Task<Result> ProcessQueryResponse(HttpResponseMessage resp)
    {
        var content = await resp.Content.ReadAsStringAsync();
        var res = JObject.Parse(content);
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        if (res["choices"] != null)
        {
            var tk = res["choices"][0]["message"];
            if (tk != null)
            {
                if (tk["tool_calls"] != null && (tk["tool_calls"] as JArray).Count>0)
                {
                    var tools = tk["tool_calls"] as JArray;
                    foreach (var tool in tools)
                    {
                        if (tool["type"].Value<string>() == "function") //如果传入了其它可调用工具，它有可能会返回其它的
                        {
                            var funcId = tool["id"].Value<string>();
                            var funcName = tool["function"]["name"].Value<string>();
                            var funcArgs = tool["function"]["arguments"].Value<string>();
                            functionCalls.Add(new FunctionCall(){Id = funcId, Name = funcName, Arguments = funcArgs});
                        }
                    }
                }
                else if (tk["content"] != null)
                    return Result.Answer(tk["content"].Value<string>());
                else
                    return Result.Error(content);
            }
        }
        
        if (functionCalls.Count>0)
        {
            return FunctionsResult.Answer(functionCalls);
        }
        return Result.Error(content);
    }
    
    
    public async IAsyncEnumerable<Result> ProcessResponseApiStreamResponse(HttpResponseMessage resp)
    {
        string itemId = "";
        string funcId = "";
        string funcName = "";
        string funcArgs = "";
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        bool reasoning = false;
        using (var stream = await resp.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string lineEvent="";
            string lineData="";
            string line;
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(resp.StatusCode + " : " + line);
                yield break;
            }

            var processEvents = new HashSet<string>()
            {
                "response.output_item.added", "response.output_text.delta", "response.reasoning_summary_part.added",
                "response.reasoning_summary_text.delta", "response.function_call_arguments.done", "error"
            };
            while ((line = await reader.ReadLineAsync()) != null)
            {
                //Console.WriteLine(line);
                if (line.StartsWith("event:"))
                {
                    lineEvent = line;
                }
                else if (line.StartsWith("data:"))
                {
                    lineData = line;
                }
                else if (line.Length == 0 && !string.IsNullOrEmpty(lineEvent))
                {
                    lineEvent = lineEvent.Substring("event: ".Length);
                    if (!processEvents.Contains(lineEvent))
                    {
                        continue;
                    }

                    line = lineData.Substring("data: ".Length);
                    var res = JObject.Parse(line);
                    var type = res["type"].Value<string>();
                    if (type == "response.output_text.delta")
                    {
                        var content = res["delta"].Value<string>();
                        yield return Result.Answer(content);
                    }
                    else if (type == "response.reasoning_summary_text.delta")
                    {
                        reasoning = true;
                        var content = res["delta"].Value<string>();
                        yield return Result.Reasoning(content);
                    }
                    else if (type == "response.reasoning_summary_part.added")
                    {
                        if (reasoning)
                        {
                            yield return Result.Reasoning("\n\n");
                        }
                    }
                    else if (type == "response.output_item.added")
                    {
                        var itemType = res["item"]["type"].Value<string>();
                        if (itemType == "function_call")
                        {
                            itemId = res["item"]["id"].Value<string>();
                            funcId = res["item"]["call_id"].Value<string>();
                            funcName = res["item"]["name"].Value<string>();
                            funcArgs = "";
                        }
                    }
                    else if (type == "response.function_call_arguments.done")
                    {
                        funcArgs = res["arguments"].Value<string>();
                        functionCalls.Add(new FunctionCall()
                            { Id = funcId, ItemId = itemId, Name = funcName, Arguments = funcArgs });
                    }else if (type == "error")
                    {
                        yield return Result.Error(res["error"]["message"].Value<string>());
                    }
                }
            }
        }
        
        if (functionCalls.Count>0)
        {
            yield return FunctionsResult.Answer(functionCalls);
        }
    }

    
    public async Task<Result> ProcessResponseApiQueryResponse(HttpResponseMessage resp)
    {
        var content = await resp.Content.ReadAsStringAsync();
        var res = JObject.Parse(content);
        if (res["error"] is not null)
            return Result.Error(content);
        
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        var status = res["status"].Value<string>();
        var format = res["text"]["format"]["type"].Value<string>(); //text或json_schema
        var outputs = res["output"] as JArray;
        var sb = new StringBuilder();
        foreach (var output in outputs)
        {
            var type = output["type"].Value<string>();
            if (type == "message")
            {
                var contents = output["content"] as JArray;
                foreach (var cnt in contents)
                {
                    var cntType =  cnt["type"].Value<string>();
                    if (cntType == "output_text")
                        sb.AppendLine(cnt["text"].Value<string>());
                    if (cntType == "refusal")
                        sb.AppendLine(cnt["refusal"].Value<string>());
                }
            }
            else if (type == "reasoning")
            {
                var contents = output["summary"] as JArray;
                foreach (var cnt in contents)
                {
                    var cntType =  cnt["type"].Value<string>();
                    if (cntType == "summary_text")
                        sb.AppendLine(cnt["text"].Value<string>());
                }
            }
            else if (type == "function_call")
            {
                var itemId = output["id"].Value<string>();
                var funcId = output["call_id"].Value<string>();
                var funcName = output["name"].Value<string>();
                var funcArgs = output["arguments"].Value<string>();
                functionCalls.Add(new FunctionCall(){Id = funcId, ItemId = itemId, Name = funcName, Arguments = funcArgs});
            }
        }
        if (functionCalls.Count>0)
            return FunctionsResult.Answer(functionCalls);
        if(sb.Length>0)
            return Result.Answer(sb.ToString());
        
        return Result.Error(content);
    }
    
    #region 参数类型定义

    protected class Message
    {
        public string role { get; set; } = string.Empty;
    }
    protected class TextMessage:Message
    {
        public string content { get; set; } = string.Empty;
    }
    protected class ToolMessage:Message
    {
        public object? content { get; set; }
        public string? tool_call_id { get; set; } = null;
    }
    protected class AssistantMessage:Message
    {
        public string content { get; set; } = string.Empty;
    }
    protected class AssistantToolsMessage:Message
    {
        public List<ToolCall> tool_calls { get; set; }
        public string content { get; set; } = string.Empty;
    }
    protected class ToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("function")]
        public FunctionCall Function { get; set; }
    }
    protected class VisionMessage:Message
    {
        public List<VisionMessageContent> content { get; set; }
    }
    protected class VisionMessageContent
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("text")]
        public string? Text { get; set; }
        [JsonProperty("image_url")]
        public VisionMessageImageUrl? ImageUrl { get; set; }
        
        [JsonProperty("image_base64")]
        public string? ImageBase64 { get; set; } //商汤专用
    }
    protected class VisionMessageImageUrl
    {
        public string url { get; set; }
    }

    protected class ToolParamter
    {
        public string type { get; set; } = "function";
    }
    protected class FunctionToolParamter:ToolParamter
    {
        public Function function { get; set; }
    }
    protected class WebSearchToolParamter:ToolParamter
    {
        public object web_search { get; set; } //各家格式不太一样，留一个通用格式处理
    }

    #endregion
    
    
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings,
            model = apiClassAttribute.EmbeddingModelName,
            dimensions = apiClassAttribute.EmbeddingDimensions
        });
    }
    private class EmbeddingsResponse
    {
        public string Object { get; set; }
        public EmbeddingObject[] Data { get; set; }
    }
    private class EmbeddingObject
    {
        public string Object { get; set; }
        public double[] Embedding { get; set; }
        public int Index { get; set; }
    }
    public override async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        if (string.IsNullOrEmpty(apiClassAttribute.EmbeddingModelName))
        {
            return await base.Embeddings(qc, embedForQuery);
        }

        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        var url = _host + "v1/embeddings";
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.Data.Select(t => t.Embedding).ToArray(), string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
}


public interface IOpenAIRestClient
{
    RestClient GetRestClient();

    HttpClient GetHttpClient();
}

/// <summary>
/// 新版的RestClient建议使用Singleton模式
/// </summary>
public class OpenAIRestClient:IOpenAIRestClient
{
    private RestClient _client;
    private IHttpClientFactory _httpClientFactory;
    private ConfigHelper _configuration;
    
    public OpenAIRestClient(ConfigHelper configuration, IHttpClientFactory httpClientFactory)
    {
        var proxy = configuration.GetConfig<string>("Providers:OpenAI:Host");
        var apikey = configuration.GetConfig<string>("Providers:OpenAI:Key");
        _client = new RestClient(proxy);
        _client.AddDefaultHeader("Authorization", "Bearer " + apikey);
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public RestClient GetRestClient()
    {
        return _client;
    }
    
    public HttpClient GetHttpClient()
    {
        var proxy = _configuration.GetConfig<string>("Providers:OpenAI:Host");
        var apikey = _configuration.GetConfig<string>("Providers:OpenAI:Key");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
        client.BaseAddress = new Uri(proxy);
        client.Timeout = TimeSpan.FromSeconds(300);
        return client;
    }
}

[ApiProvider("OpenAI_Response")]
public class ApiOpenAIResponseProvider : ApiOpenAIProvider
{
    public ApiOpenAIResponseProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "responses";
        if (attr.UseThinkingMode)
        {
            extraOptionsList = new List<ExtraOption>()
            {
                new ExtraOption()
                {
                    Type = "思考深度", Contents = new[]
                    {
                        new KeyValuePair<string, string>("禁用", "disable"),
                        new KeyValuePair<string, string>("低", "low"),
                        new KeyValuePair<string, string>("中", "medium"),
                        new KeyValuePair<string, string>("高", "high")
                    }
                }
            };
        }
    }

    /// <summary>
    /// 最新Response API流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+_key);
        var url = _chatUrl;
        var msg = GetResponseApiMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        await foreach (var resp in ProcessResponseApiStreamResponse(response))
            yield return resp;
    }
    
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+_key);
        var url = _chatUrl;
        var msg = GetResponseApiMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessResponseApiQueryResponse(resp);
    }

    
    private class ResponseApiInput
    {
        public string role { get; set; }
        public object[] content { get; set; }
    }
    /// <summary>
    /// ResponseApi消息体构建
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream"></param>
    /// <returns></returns>
    private string GetResponseApiMsgBody(ApiChatInputIntern input, bool stream)
    {
        var functions = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        List<object> funcs = new List<object>();
        if (functions != null)
        {
            foreach (var t in functions)
            {
                var func = (FunctionToolParamter)t;
                funcs.Add(new
                {
                    type = "function", name = func.function.Name, description = func.function.Description,
                    parameters = func.function.Parameters
                });
            }
        }
        if (!string.IsNullOrEmpty(_extraTools))
        {
            if (_extraTools == "computer_use_preview")
            {
                funcs.Add(new
                {
                    type = _extraTools,
                    display_width = input.DisplayWidth,
                    display_height = input.DisplayHeight,
                    environment = "browser", // other possible values: "mac", "windows", "ubuntu"
                });
            }
            else
            {
                funcs.Add(new { type = _extraTools });
            }
        }

        var tools = funcs.Count > 0 ? funcs.ToArray() : null;
        
        var jSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? _visionModelName : _modelName;
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = new List<object>();
        if (!string.IsNullOrEmpty(input.ChatContexts.SystemPrompt))
        {
            msgs.Add(new ResponseApiInput()
            {
                role = "developer",
                content = new[] { new { type = "input_text", text = input.ChatContexts.SystemPrompt } }
            });
        }
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            var contents = new List<object>();
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.图片Base64)
                {
                    contents.Add(new
                    {
                        type = "input_image",
                        image_url = new
                        {
                            url = $"data:{(string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType)};base64," +
                                  qc.Content
                        }
                    });
                }
                else if (qc.Type == ChatType.图片Url)
                {
                    contents.Add(new { type = "input_image", image_url = new { url = qc.Content } });
                }
                else if (qc.Type == ChatType.文件Bytes)
                {
                    contents.Add(new
                    {
                        type = "file", file = new
                        {
                            filename = qc.FileName,
                            file_data = qc.Bytes != null ? Convert.ToBase64String(qc.Bytes) : qc.Content
                        }
                    });
                }
                else if (qc.Type == ChatType.文本 || qc.Type== ChatType.提示模板 || qc.Type== ChatType.图书全文)
                {
                    contents.Add(new { type = "input_text", text = qc.Content });
                }
            }
            if (contents.Count > 0)
                msgs.Add(new ResponseApiInput() { role = "user", content = contents.ToArray() });
            
            foreach (var ac in ctx.AC)
            {
                if (ac.Type == ChatType.文本 && !string.IsNullOrEmpty(ac.Content))
                {
                    msgs.Add(new ResponseApiInput()
                    {
                        role = "assistant", content = new[] { new { type = "output_text", text = ac.Content } }
                    });
                }
                else if (ac.Type == ChatType.FunctionCall && !string.IsNullOrEmpty(ac.Content))
                {
                    var acalls = JsonConvert.DeserializeObject<List<FunctionCall>>(ac.Content);
                    foreach (var t in acalls)
                    {
                        msgs.Add(new
                        {
                            type = "function_call", id = t.ItemId, call_id = t.Id, name = t.Name, arguments = t.Arguments
                        });
                    }
                    foreach (var t in acalls)
                    {
                        msgs.Add(new
                        {
                            type = "function_call_output",call_id = t.Id, output = t.ResultStr
                        });
                    }
                }
            }
        }

        var effort = GetExtraOptions(input.External_UserId)[0].CurrentValue;
        return JsonConvert.SerializeObject(new
        {
            model,
            reasoning = apiClassAttribute.UseThinkingMode && effort != "disable" ?  new { summary = "auto", effort = effort } : null,
            input = msgs,
            tools,
            store = false,
            max_output_tokens = _maxTokens,
            user = input.External_UserId,
            stream
        }, jSetting);
    }
}


/// <summary>
/// OpenAI流式语音转文字扩展类
/// </summary>
[ApiProvider("OpenAI_Stream")]
public class ApiOpenAIStreamProvider : ApiOpenAIProvider, IAiWebSocketProxy
{
    private string wssHost;
    public ApiOpenAIStreamProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
       
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        wssHost = configHelper.GetProviderConfig<string>(attr.Provider, "WssHost");
    }

    private BlockingCollection<Result>? _results;
    private ClientWebSocket ws;
    private List<Task> tasks = new List<Task>();
    bool addAnswerFinished = false;
    bool closeCallbackCalled = false;


    public async Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="")
    {
        _results = new BlockingCollection<Result>();
        var url = $"{wssHost}v1/realtime?model=gpt-4o-realtime-preview-2024-12-17";
        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + _key);
        ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        var t = Task.Run(async () =>
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            try
            {
                while (ws.State == WebSocketState.Open) //持续读服务端返回的消息
                {
                    WebSocketReceiveResult result;
                    var byteList = new List<byte>();
                    do
                    {
                        result =
                            await ws.ReceiveAsync(buffer, CancellationToken.None);
                        if(result.Count>0)
                            byteList.AddRange(buffer.Array[..result.Count]);
                    }while(!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        //对方断开连接
                        break;
                    }
                    else
                    {
                        var bytes = byteList.ToArray();
                        var str = Encoding.UTF8.GetString(bytes);
                        var o = JObject.Parse(str);
                        //Console.WriteLine(str);
                        var type = o["type"].Value<string>();
                        if (type == "error")
                            _results.Add(Result.Error(str));
                        else if (type == "response.audio_transcript.delta")
                            _results.Add(Result.Answer(o["delta"].Value<string>()));
                        else if (type == "response.audio.delta")
                            _results.Add(FileResult.Answer(Convert.FromBase64String(o["delta"].Value<string>()), "pcm",
                                ResultType.AudioBytes));
                    }
                }
            }catch{}

            //读消息结束，将结果标记为完成
            if (!addAnswerFinished)
            {
                _results.Add(Result.New(ResultType.AnswerFinished));
                _results.CompleteAdding();
                addAnswerFinished = true;
            }
        });
        tasks.Add(t);
        var t1 = Task.Run(async () =>
        {
            foreach (var res in _results.GetConsumingEnumerable())
            {
                OnMessageReceived?.Invoke(this, res);
            }
            //要发送的消息全部发送完成，断开连接并通知结束事件
            await CloseAsync();
            if(!closeCallbackCalled)
                OnProxyDisconnect?.Invoke(this, EventArgs.Empty);
        });
        tasks.Add(t1);
        var t2 = Task.Run(async () =>
        {
            foreach (var data in messageQueue.GetConsumingEnumerable())
            {
                if (ws.State != WebSocketState.Open)
                    break;
                if (data is string s)
                {
                    if (s == "audio_commit")
                    {
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetAudioChunkEndMsg())), WebSocketMessageType.Text,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetWaitResponseMsg())), WebSocketMessageType.Text,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                    }else if (s == "response_cancel")
                    {
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetCancelResponseMsg())), WebSocketMessageType.Text,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                    }
                    else if(s.Length>2)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetTextInputMsg(s))), WebSocketMessageType.Text,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetWaitResponseMsg())), WebSocketMessageType.Text,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                    }
                }
                else if (data is byte[] bytes)
                {
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetAudioChunkMsg(bytes))), WebSocketMessageType.Text,
                        WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                }
            }
        });
        tasks.Add(t2);
        //发送初始化语句
        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(GetSystemMsg())), WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
    }

    private string GetAudioChunkMsg(byte[] bytes)
    {
        return JsonConvert.SerializeObject(new
        {
            type = "input_audio_buffer.append",
            audio = Convert.ToBase64String(bytes)
        });
    } 
    private string GetAudioChunkEndMsg()
    {
        return JsonConvert.SerializeObject(new
        {
            type = "input_audio_buffer.commit"
        });
    }
    private string GetWaitResponseMsg()
    {
        return JsonConvert.SerializeObject(new
        {
            type = "response.create"
        });
    }
    private string GetCancelResponseMsg()
    {
        return JsonConvert.SerializeObject(new
        {
            type = "response.cancel"
        });
    }
    private string GetTextInputMsg(string text)
    {
        return JsonConvert.SerializeObject(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role="user",
                content=new[]
                {
                    new{type="input_text", text = text}
                }
            }
        });
    }
    private string GetAudioInputMsg(byte[] bytes)
    {
        return JsonConvert.SerializeObject(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role="user",
                content=new[]
                {
                    new{type="input_audio", audio = Convert.ToBase64String(bytes)}
                }
            }
        });
    }
    private string GetSystemMsg()
    {
        var msg = JsonConvert.SerializeObject(new
        {
            type = "session.update",
            session = new
            {
                instructions = "You are a professional personal assistant who always communicates with users in a tone, manner, and attitude that is full of emotion and empathy. You strive to meet users' requests as much as possible and provide them with various forms of assistance.",
                input_audio_transcription = "NULL",
                turn_detection = "NULL"
            }
        });
        return msg.Replace("\"NULL\"", "null");
    }

    public async Task CloseAsync()
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }catch{}
    }

    public void Wait()
    {
        Task.WaitAll(tasks.ToArray());
    }

    public event EventHandler<Result>? OnMessageReceived;
    public event EventHandler? OnProxyDisconnect;
}

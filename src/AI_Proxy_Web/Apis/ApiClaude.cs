using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Claude中杯, "Claude 3.7", "Claude 3.7 Sonet是Open AI目前最强劲的竞争对手，代码能力最强，支持图文，200K上下文长度，价格跟GPT4o一样，比较适中。", 3, canProcessImage:true, canProcessMultiImages:true, canProcessFile:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaude:ApiBase
{
    protected ClaudeClient _client;
    public ApiClaude(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ClaudeClient>();
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
        _client.ExtraTools = new[] { ClaudeClient.ExtraTool.WebSearch };
    }
    
    /// <summary>
    /// 使用Claude来回答
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
    /// 使用Claude来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
}

[ApiClass(M.Claude小杯, "Claude小杯*", "强烈推荐：Claude 3.5 Haiku小杯是Open AI目前最强劲的竞争对手，体积小，推理能力强，支持图文，200K上下文长度，价格便宜速度极快，能力介于GPT4o mini和4o之间。", 2,  canProcessImage:true, canProcessMultiImages:true, canProcessFile:true, canUseFunction:true, priceIn: 1.8, priceOut: 9)]
public class ApiClaude3Haiku : ApiClaude
{
    public ApiClaude3Haiku(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-5-haiku-20241022"); //小杯
    }
}

[ApiClass(M.ClaudeAgent, "Claude RPA", "Claude 3.7 RPA版可以操作一个虚拟浏览器，通过自动分解操作步骤并控制浏览器一步一步进行点击或输入的方式进行一系列自主操作来完成用户任务，需要有客户端来调用并负责实际完成任务。", 320, type: ApiClassTypeEnum.辅助模型,  canProcessImage:true, canProcessMultiImages:true, canProcessFile:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaudeAgent : ApiClaude
{
    public ApiClaudeAgent(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
        _client.ExtraTools = new[]
            { ClaudeClient.ExtraTool.Computer, ClaudeClient.ExtraTool.TextEditor, ClaudeClient.ExtraTool.Bash };
    }
}

[ApiClass(M.ClaudeEditor, "Claude Editor", "Claude 3.7 Editor版可以操作操作指定的文件，自主查看文件当前内容，并根据需要完成文件的内容编辑指令，需要有客户端来调用并负责实际完成任务。", 321, type: ApiClassTypeEnum.辅助模型, canProcessImage:true, canProcessMultiImages:true, canProcessFile:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaudeEditor : ApiClaude
{
    public ApiClaudeEditor(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
        _client.ExtraTools = new[] { ClaudeClient.ExtraTool.TextEditor,  ClaudeClient.ExtraTool.Bash };
    }
}

[ApiClass(M.ClaudeThinking, "Claude Thinking", "Claude 3.7 sonet推理版使用带推理能力的Claude模型，适合复杂的编程任务及数理问题。", 122, type: ApiClassTypeEnum.推理模型, canProcessImage:true, canProcessMultiImages:true, canProcessFile:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaudeThinking : ApiClaude
{
    public ApiClaudeThinking(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
        _client.ExtraTools = new[] { ClaudeClient.ExtraTool.Thinking, ClaudeClient.ExtraTool.WebSearch };
    }
}

/// <summary>
/// Claude大模型接口
/// 文档：https://docs.anthropic.com/claude/reference/messages_post
/// </summary>
public class ClaudeClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public ClaudeClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper, IFunctionRepository functionRepository)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:Claude:Key");
        hostUrl = configHelper.GetConfig<string>("Service:Claude:Host") + "v1/messages";
    }
    
    private String hostUrl;
    private String APIKEY;//从开放平台控制台中获取
    private string modelName = "";

    public void SetModelName(string model)
    {
        this.modelName = model;
    }
    public void SetHostAndKey(string host, string key)
    {
        this.hostUrl = host + "v1/messages";
        this.APIKEY = key;
    }
    
    public enum ExtraTool
    {
        Thinking,
        Computer,
        TextEditor,
        Bash,
        WebSearch
    }

    public ExtraTool[] ExtraTools { get; set; } = new ExtraTool[0];

    /// <summary>
    /// 注意prompt构造方式
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        List<Message> msgs = new List<Message>();
        var resultImageIndex = 0;
        var resultHtmlIndex = 0;
        List<object> contents = new List<object>();
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            contents.Clear();
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.图片Base64)
                {
                    contents.Add(new VisionMessageContent() {Type = "image", Source = new VisionMessageSource()
                    {
                        Type = "base64", MediaType = string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType, Data = qc.Content
                    }});
                }else if (qc.Type == ChatType.文件Bytes)
                {
                    if (qc.FileName.ToLower().EndsWith(".pdf"))
                    {
                        contents.Add(new VisionMessageContent()
                        {
                            Type = "document", Source = new VisionMessageSource()
                            {
                                Type = "base64", MediaType = "application/pdf",
                                Data = qc.Bytes != null ? Convert.ToBase64String(qc.Bytes) : qc.Content
                            },
                            CacheControl = new { type = "ephemeral" }
                        });
                    }
                }
                else if (qc.Type == ChatType.文本|| qc.Type== ChatType.提示模板|| qc.Type== ChatType.图书全文)
                {
                    contents.Add(new VisionMessageContent(){Type="text", Text = qc.Content});
                }
            }

            if (contents.Count > 0)
            {
                msgs.Add(new VisionMessage {role = "user", content = contents.ToList()});
            }

            foreach (var ac in ctx.AC)
            {
                if(ac.Type== ChatType.文本)
                    msgs.Add(new AssistantMessage() {role = "assistant", content = ac.Content});
                else if (ac.Type == ChatType.FunctionCall)
                {
                    var acalls = JsonConvert.DeserializeObject<List<FunctionCall>>(ac.Content);
                    var callList = new List<ToolUseResponse>();
                    foreach (var call in acalls)
                    {
                        callList.Add(new ToolUseResponse(){type = "tool_use", id = call.Id, name=call.Name, input = string.IsNullOrEmpty(call.Arguments)?new JObject():JObject.Parse(call.Arguments)});
                    }
                    msgs.Add(new ToolUseMessage()
                    {
                        role = "assistant", content = callList.ToArray()
                    });
                    contents.Clear();
                    foreach (var call in acalls)
                    {
                        if (call.Result?.resultType == ResultType.ImageBytes)
                        {
                            resultImageIndex++;
                            if(resultImageIndex >= input.ChatContexts.ResultImagesCount-2){ //带上最近3张图片
                                var result = (FileResult)call.Result;
                                contents.Add(new ToolResponse()
                                {
                                    type = "tool_result", tool_use_id = call.Id, content = new[]
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
                                    }
                                });
                            }
                            else
                            {
                                contents.Add(new ToolResponse() { type = "tool_result", tool_use_id = call.Id, content = "" });
                            }
                        }
                        else if (call.Name == "GetPageHtml")
                        {
                            resultHtmlIndex++;
                            contents.Add(new ToolResponse()
                            {
                                type = "tool_result", tool_use_id = call.Id,
                                content = resultHtmlIndex == input.ChatContexts.ResultFullHtmlCount //只保留最后一条
                                    ? call.Result?.ToString()
                                    : ""
                            });
                        }
                        else
                        {
                            var result = call.Result?.ToString();
                            if (result == "[]") result = null;
                            contents.Add(new ToolResponse() { type = "tool_result", tool_use_id = call.Id, content = result });
                        }
                    }
                    msgs.Add(new VisionMessage {role = "user", content = contents.ToList()});
                }
            }
        }
        var functions = _functionRepository.GetFunctionList(input.WithFunctions);
        var tools = new List<object>();
        if (ExtraTools.Contains(ExtraTool.Computer))
        {
            tools.Add(new
            {
                type = "computer_20250124", name = "computer", display_width_px = input.DisplayWidth ?? 1024,
                display_height_px = input.DisplayHeight ?? 768,
                display_number = 1
            });
            tools.Add(new{name="OpenUrl", description="Use the current web browser to open an URL.", input_schema=new
            {
                type="object", required=new[]{"url"}, properties = new
                {
                    url = new{type="string", description="the full URL need to be opened."}
                }
            }});
            tools.Add(new{name="GetPageHtml", description="Get full html content of current web page.", input_schema=new
            {
                type="object", properties = new {}
            }});
            tools.Add(new{name="GoBack", description="Let the web browser go back to previous page.", input_schema=new
            {
                type="object", properties = new {}
            }});
        }

        if (ExtraTools.Contains(ExtraTool.TextEditor))
        {
            tools.Add(new { type = "text_editor_20250124", name = "str_replace_editor" });
            tools.Add(new{name="SendFile", description="Send file to user.", input_schema=new
            {
                type="object", required=new[]{"path"}, properties = new
                {
                    path = new{type="string", description="the relative file path need to be send."}
                }
            }});
        }

        if (ExtraTools.Contains(ExtraTool.Bash))
        {
            tools.Add(new { type = "bash_20250124", name = "bash" });
        }
        if (ExtraTools.Contains(ExtraTool.WebSearch))
        {
            tools.Add(new { type = "web_search_20250305", name = "web_search", max_uses = 5 });
        }
        
        if(functions!=null)
        {
            functions.ForEach(t =>
                tools.Add(
                    new
                    {
                        name = t.Name, description = t.Description, input_schema = t.Parameters
                    }));
        }
        var max_tokens = ExtraTools.Contains(ExtraTool.Computer)||ExtraTools.Contains(ExtraTool.TextEditor)||ExtraTools.Contains(ExtraTool.Bash)
            ? 4096
            : (ExtraTools.Contains(ExtraTool.Thinking) ? 64000 : 32000);
        var think = ExtraTools.Contains(ExtraTool.Thinking)
            ? new { type = "enabled", budget_tokens = max_tokens / 2 }
            : null;
        var system = new[]
        {
            new { type = "text", text = input.ChatContexts.SystemPrompt, cache_control = new { type = "ephemeral" } }
        };
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = ExtraTools.Contains(ExtraTool.Thinking)? 1 : input.Temprature,
            max_tokens = max_tokens,
            tools,
            stream,
            thinking = think,
            system
        }, jSetting);
    }

    
    protected class Message
    {
        public string role { get; set; } = string.Empty;
    }
    protected class TextMessage:Message
    {
        public string content { get; set; } = string.Empty;
    }
    protected class AssistantMessage:Message
    {
        public string content { get; set; } = string.Empty;
    }
    protected class ToolMessage:Message
    {
        public ToolResponse[] content { get; set; }
    }
    protected class ToolResponse
    {
        public string type { get; set; }
        public string tool_use_id { get; set; }
        public object? content { get; set; }
    }
    protected class ToolUseMessage:Message
    {
        public ToolUseResponse[] content { get; set; }
    }
    protected class ToolUseResponse
    {
        public string type { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public JObject input { get; set; }
    }
    protected class VisionMessage:Message
    {
        public List<object> content { get; set; }
    }
    protected class VisionMessageContent
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("text")]
        public string? Text { get; set; }
        [JsonProperty("source")]
        public VisionMessageSource Source  { get; set; }
        [JsonProperty("cache_control")]
        public object? CacheControl { get; set; }
    }
    protected class VisionMessageSource
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("media_type")]
        public string? MediaType { get; set; }
        [JsonProperty("data")]
        public string? Data { get; set; }
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <param name="thinking">是否使用推理模式</param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input, bool thinking = false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key",APIKEY);
        client.DefaultRequestHeaders.Add("anthropic-version","2023-06-01");
        if(ExtraTools.Contains(ExtraTool.Thinking) && ExtraTools.Length==1)
            client.DefaultRequestHeaders.Add("anthropic-beta","output-128k-2025-02-19");
        if(ExtraTools.Contains(ExtraTool.Computer)||ExtraTools.Contains(ExtraTool.TextEditor)||ExtraTools.Contains(ExtraTool.Bash))
            client.DefaultRequestHeaders.Add("anthropic-beta","computer-use-2025-01-24");
            
        var url = hostUrl;
        var msg = GetMsgBody(input, true);
        HttpResponseMessage response = null;
        var errorMsg = "";
        try
        {
            response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(msg, Encoding.UTF8, "application/json")
            }, HttpCompletionOption.ResponseHeadersRead);
        }
        catch(Exception ex)
        {
            errorMsg =  ex.Message;
        }

        if (response == null)
        {
            yield return Result.Error("Claude请求错误 : " + errorMsg);
            yield break;
        }
        using (var stream = await response.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(response.StatusCode.ToString() + " : " + line);
                yield break;
            }

            var isFunction = false;
            var function = new FunctionCall();
            List<FunctionCall> functionCalls = new List<FunctionCall>();
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);
                else if(!line.StartsWith("{"))
                    continue;
                line = line.TrimStart();

                if (line == "[DONE]")
                {
                    break;
                }
                else if (line.StartsWith(":"))
                {
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    //Console.WriteLine(line);
                    var res = JObject.Parse(line);
                    if (res["type"] is null)
                        continue;
                    var type = res["type"].Value<string>(); //单行消息类型
                    if (type == "content_block_start")
                    {
                        if (res["content_block"]["type"].Value<string>() == "tool_use")
                        {
                            if (!string.IsNullOrEmpty(function.Id))
                            {
                                functionCalls.Add(function);
                                function = new FunctionCall();
                            }
                            function.Id = res["content_block"]["id"].Value<string>();
                            function.Name = res["content_block"]["name"].Value<string>();
                            function.Arguments = "";
                            isFunction = true;
                        }
                    }

                    if (type == "content_block_delta" && res["delta"] != null)
                    {
                        if (isFunction && res["delta"]["type"].Value<string>()=="input_json_delta" && res["delta"]["partial_json"] != null)
                            function.Arguments += res["delta"]["partial_json"].Value<string>();
                        if(res["delta"]["type"].Value<string>()=="text_delta" && res["delta"]["text"] != null)
                            yield return Result.Answer(res["delta"]["text"].Value<string>());
                        if(res["delta"]["type"].Value<string>()=="thinking_delta" && res["delta"]["thinking"] != null)
                            yield return Result.Reasoning(res["delta"]["thinking"].Value<string>());
                    }
                    else if (res["error"] != null)
                        yield return Result.Error(line);
                }
            }

            if (!string.IsNullOrEmpty(function.Id))
            {
                functionCalls.Add(function);
                yield return FunctionsResult.Answer(functionCalls);
            }
        }
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key",APIKEY);
        client.DefaultRequestHeaders.Add("anthropic-version","2023-06-01");
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["content"] != null)
        {
            List<FunctionCall> functionCalls = new List<FunctionCall>();
            var sb = new StringBuilder();
            var arr = json["content"] as JArray;
            foreach (var tk in arr)
            {
                var type = tk["type"].Value<string>();
                if(type=="tool_use")
                    functionCalls.Add(new FunctionCall()
                    {
                        Id = tk["id"].Value<string>(), Name = tk["name"].Value<string>(), Arguments = tk["input"].ToString()
                    });
                else if (type == "text")
                {
                    sb.Append(tk["text"].Value<string>());
                }
            }
            if (functionCalls.Count>0)
            {
                return FunctionsResult.Answer(functionCalls);
            }
            else
            {
                return Result.Answer(sb.ToString());
            }
        }
        else
            return Result.Error(content);
    }
}
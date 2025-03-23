using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Claude中杯, "Claude 3.7", "Claude 3.7 Sonet是Open AI目前最强劲的竞争对手，代码能力最强，支持图文，200K上下文长度，价格跟GPT4o一样，比较适中。", 3, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaude:ApiBase
{
    protected ClaudeClient _client;
    public ApiClaude(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ClaudeClient>();
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
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

[ApiClass(M.Claude小杯, "Claude小杯*", "强烈推荐：Claude 3.5 Haiku小杯是Open AI目前最强劲的竞争对手，体积小，推理能力强，支持图文，200K上下文长度，价格便宜速度极快，能力介于GPT4o mini和4o之间。", 2,  canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 1.8, priceOut: 9)]
public class ApiClaude3Haiku : ApiClaude
{
    public ApiClaude3Haiku(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-5-haiku-20241022"); //小杯
    }
}

[ApiClass(M.ClaudeAgent, "Claude Agent", "Claude 3.7 Agent版特别增加了电脑操作相关的语法和上下文，可以通过自动化分解指令的方式完成一系列需要操作电脑的任务，需要客户端调用。", 320, type: ApiClassTypeEnum.辅助模型,  canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaudeAgent : ApiClaude
{
    public ApiClaudeAgent(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModelName("claude-3-7-sonnet-20250219"); //中杯
    }
    
    /// <summary>
    /// 覆盖掉消息方法，使用特殊方法调用
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendComputerUseMessageStream(input))
        {
            yield return resp;
        }
    }
}


[ApiClass(M.ClaudeThinking, "Claude Thinking", "Claude 3.7 sonet推理版使用带推理能力的Claude模型，适合复杂的编程任务及数理问题。", 122, type: ApiClassTypeEnum.推理模型, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 21.6, priceOut: 108)]
public class ApiClaudeThinking : ApiClaude
{
    public ApiClaudeThinking(IServiceProvider serviceProvider):base(serviceProvider)
    {
        
    }
    
    /// <summary>
    /// 覆盖掉消息方法，使用特殊方法调用
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendMessageStream(input, true))
        {
            yield return resp;
        }
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
    //给备用站点使用
    public void SetHostUrl(string host)
    {
        this.hostUrl = host;
    }
    //给备用站点使用
    public void SetApiKey(string apikey)
    {
        this.APIKEY = apikey;
    }
    
    /// <summary>
    /// 注意prompt构造方式
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <param name="thinking">是否使用推理模式</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream, bool thinking = false)
    {
        List<Message> msgs = new List<Message>();
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            List<object> contents = new List<object>();
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.图片Base64)
                {
                    contents.Add(new VisionMessageContent() {Type = "image", Source = new VisionMessageSource()
                    {
                        Type = "base64", MediaType = string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType, Data = qc.Content
                    }});
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
                        var result = call.Result?.ToString();
                        if(result == "[]") result = null;
                        contents.Add(new ToolResponse() {type = "tool_result", tool_use_id = call.Id, content = result});
                    }
                    msgs.Add(new VisionMessage {role = "user", content = contents.ToList()});
                }
            }
        }
        var functions = _functionRepository.GetFunctionList(input.WithFunctions);
        var tools = functions == null
            ? null
            : functions.Select(t => new
            {
                name = t.Name, description = t.Description, input_schema = t.Parameters
            }).ToList();

        var think = thinking ? new { type = "enabled", budget_tokens = 32000 } : null;
        var system = input.ChatContexts.SystemPrompt;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = thinking? 1 : input.Temprature,
            max_tokens = thinking? 64000 : 32000,
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
        if(thinking)
            client.DefaultRequestHeaders.Add("anthropic-beta","output-128k-2025-02-19");
            
        var url = hostUrl;
        var msg = GetMsgBody(input, true, thinking);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

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
    
    
    /// <summary>
    /// 注意prompt构造方式
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetComputerUseMsgBody(ApiChatInputIntern input, bool stream)
    {
        List<Message> msgs = new List<Message>();
        var totalImages = 0;
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            foreach (var qc in ctx.AC)
            {
                if (qc.Type == ChatType.FunctionCall)
                {
                    var qcalls = JsonConvert.DeserializeObject<List<FunctionCall>>(qc.Content);
                    foreach (var call in qcalls)
                    {
                        if (call.Result?.resultType == ResultType.ImageBytes)
                            totalImages++;
                    }
                }
            }
        }

        var index = 0;
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            List<object> contents = new List<object>();
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.图片Base64)
                {
                    contents.Add(new VisionMessageContent() {Type = "image", Source = new VisionMessageSource()
                    {
                        Type = "base64", MediaType = string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType, Data = qc.Content
                    }});
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
                            index++;
                            if(index > totalImages-2){
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
        var tools = new List<object>()
        {
            new
            {
                type = "computer_20250124", name = "computer", display_width_px = input.DisplayWidth ?? 1024,
                display_height_px = input.DisplayHeight ?? 768,
                display_number = 1
            }
        };
        tools.Add(new { type = "text_editor_20250124", name = "str_replace_editor" });
        tools.Add(new { type = "bash_20250124", name = "bash" });

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
        tools.Add(new{name="SendFile", description="Send file content to user.", input_schema=new
        {
            type="object", required=new[]{"path"}, properties = new
            {
                path = new{type="string", description="the relative file path need to be send."}
            }
        }});
        
        if(functions!=null)
        {
            functions.ForEach(t =>
                tools.Add(
                    new
                    {
                        name = t.Name, description = t.Description, input_schema = t.Parameters
                    }));
        }

        var system = input.ChatContexts.SystemPrompt;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = input.Temprature,
            max_tokens = 4096,
            tools,
            stream,
            system
        }, jSetting);
    }
    
    /// <summary>
    /// 流式接口，电脑自动化操作接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendComputerUseMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key",APIKEY);
        client.DefaultRequestHeaders.Add("anthropic-version","2023-06-01");
        client.DefaultRequestHeaders.Add("anthropic-beta","computer-use-2025-01-24");
        var url = hostUrl;
        var msg = GetComputerUseMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

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

}
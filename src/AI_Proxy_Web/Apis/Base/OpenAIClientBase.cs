using System.Net;
using System.Text;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.Base;

/// <summary>
/// 用来标识所有的Client，做自动注册
/// </summary>
public interface IApiClient
{
    
}

/// <summary>
/// 用来实现公用Client的方法，适用于符合OpenAI接口标准的模型
/// </summary>
public class OpenAIClientBase
{
    public class BasicMessage
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    /// <summary>
    /// 用于简单消息模型，每条上下文只处理一条文本输入和一条文本回复
    /// </summary>
    /// <param name="chatContexts"></param>
    /// <returns></returns>
    public List<BasicMessage> GetBasicMessages(ChatContexts chatContexts)
    {
        var msgs = new List<BasicMessage>();
        foreach (var ctx in chatContexts.Contexts)
        {
            msgs.Add(new BasicMessage(){role="user", content = ctx.QC.Last().Content});
            if(ctx.AC.Count>0)
                msgs.Add(new BasicMessage(){role="assistant", content = ctx.AC.Last().Content});
        }
        return msgs;
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
                        if (this is SenseChatClient)
                        {
                            contents.Add(new VisionMessageContent()
                            {
                                Type = "image_base64",
                                ImageBase64 = qc.Content
                            });
                        }
                        else
                        {
                            contents.Add(new VisionMessageContent()
                                { Type = "image_url", ImageUrl = new VisionMessageImageUrl() { url = $"data:{(string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType)};base64," + qc.Content } });
                        }
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
                        msgs.Add(new ToolMessage()
                        {
                            role = "tool", content = call.Result.ToString(),
                            tool_call_id = call.Id
                        });
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
            if (prompt.IndexOf("{CURRENT_DATE}", StringComparison.Ordinal) >= 0)
            {
                var date = DateTime.Now.ToString("yyyy-MM-dd");
                prompt = prompt.Replace("{CURRENT_DATE}", date);
            }
        }

        return functions.Any()
            ? functions.Select(t => (ToolParamter) new FunctionToolParamter()
            {
                function = t
            }).ToList()
            : null;
    }

    public async IAsyncEnumerable<Result> ProcessStreamResponse(HttpResponseMessage resp)
    {
        string funcId = "";
        string funcName = "";
        StringBuilder funcArgs = new StringBuilder();
        List<FunctionCall> functionCalls = new List<FunctionCall>();
        bool reasoning = false;
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
                                if (reasoning)
                                {
                                    reasoning = false;
                                    yield return Result.Reasoning("\n\n");
                                }
                                yield return Result.Answer(tk["content"].Value<string>());
                            }
                            else if (tk["reasoning_content"] != null && !string.IsNullOrEmpty(tk["reasoning_content"].Value<string>()))
                            {
                                if (!reasoning)
                                {
                                    reasoning = true;
                                    yield return Result.Reasoning("> ");
                                }
                                var reason = tk["reasoning_content"].Value<string>();
                                if (reason.Contains("\n\n"))
                                {
                                    reason = reason.Replace("\n\n", "\n\n> ");
                                }
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
                    if (lineEvent != "response.output_text.delta" &&
                        lineEvent != "response.output_item.added" &&
                        lineEvent != "response.function_call_arguments.done"&&
                        lineEvent != "error")
                    {
                        continue;
                    }

                    line = lineData.Substring("data: ".Length);
                    var res = JObject.Parse(line);
                    var type = res["type"].Value<string>();
                    if (type == "response.output_text.delta")
                    {
                        if (reasoning)
                        {
                            reasoning = false;
                            yield return Result.Reasoning("\n\n");
                        }

                        yield return Result.Answer(res["delta"].Value<string>());
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
        public string content { get; set; } = string.Empty;
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
}
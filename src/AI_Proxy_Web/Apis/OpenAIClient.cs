using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace AI_Proxy_Web.Apis;


/// <summary>
/// OpenAI接口的公共类，OhMyGPT、原生GPT、AzureGPT的请求都由这个类来实现，前两种实现的接口完全一样，Azure的略有区别
/// 文档地址 https://ohmygpt-docs.apifox.cn/api-105130827
/// 但它的域名也不能直接调用，所以自己建了一层转发代理
/// http://chat-gpt-bot.yesmro.cn:9000/
/// </summary>
public class OpenAIClient: OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public OpenAIClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
    }

    public void Setup(string chatUrl, string apiKey, string _modelName, string _visionModelName, bool isReasoningModel = false, string extraTools = "")
    {
        APIKEY = apiKey;
        this.chatUrl = chatUrl;
        this.modelName = _modelName;
        this.visionModelName = _visionModelName;
        this.isReasoningModel = isReasoningModel;
        this.extraTools = extraTools;
    }

    public void Setup(string _hostUrl, string apiKey)
    {
        this.hostUrl = _hostUrl;
        this.APIKEY = apiKey;
    }
    
    private string chatUrl;
    private string hostUrl;
    private string APIKEY;
    private string modelName;
    private string visionModelName;
    private bool isReasoningModel;
    private string extraTools =  string.Empty;
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "思考深度", Contents = new []
                {
                    new KeyValuePair<string, string>("中", "medium"),
                    new KeyValuePair<string, string>("高", "high"),
                    new KeyValuePair<string, string>("低", "low")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
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
        var model = isImageMsg ? visionModelName : modelName;
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
            temperature = input.Temprature,
            max_tokens = 16000,
            user = input.External_UserId,
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
        if (modelName == "o1")
        {
            yield return Result.Waiting("正在思考，请耐心等待...");
            yield return await SendMessage(input);
            yield break;
        }
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = chatUrl;
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
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = chatUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
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
        if (!string.IsNullOrEmpty(extraTools))
        {
            if (extraTools == "computer_use_preview")
            {
                funcs.Add(new
                {
                    type = extraTools,
                    display_width = input.DisplayWidth,
                    display_height = input.DisplayHeight,
                    environment = "browser", // other possible values: "mac", "windows", "ubuntu"
                });
            }
            else
            {
                funcs.Add(new { type = extraTools });
            }
        }

        var tools = funcs.Count > 0 ? funcs.ToArray() : null;
        
        var jSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? visionModelName : modelName;
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
                    contents.Add(new { type = "input_image", image_url = $"data:{(string.IsNullOrEmpty(qc.MimeType) ? "image/jpeg" : qc.MimeType)};base64," + qc.Content });
                }
                else if (qc.Type == ChatType.图片Url)
                {
                    contents.Add(new { type = "input_image", image_url = qc.Content });
                }else if (qc.Type == ChatType.文本 || qc.Type== ChatType.提示模板 || qc.Type== ChatType.图书全文)
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

        if (isReasoningModel)
        {
            return JsonConvert.SerializeObject(new
            {
                model,
                input = msgs,
                tools,
                store = false,
                user = input.External_UserId,
                stream,
                truncation = "auto",
                reasoning = new
                {
                    generate_summary = extraTools.Contains("computer") ? "concise" : null,
                    effort = GetExtraOptions(input.External_UserId)[0].CurrentValue
                }
            }, jSetting);
        }
        else
            return JsonConvert.SerializeObject(new
            {
                model,
                input = msgs,
                tools,
                store = false,
                temperature = input.Temprature,
                max_output_tokens = 16000,
                user = input.External_UserId,
                stream
            }, jSetting);
    }
    
    /// <summary>
    /// 最新Response API流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendResponseApiStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = chatUrl;
        var msg = GetResponseApiMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        await foreach (var resp in ProcessResponseApiStreamResponse(response))
            yield return resp;
    }
    
    public async Task<Result> SendResponseApiMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = chatUrl;
        var msg = GetResponseApiMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessResponseApiQueryResponse(resp);
    }
    
    /// <summary>
    /// OpenAI的语音转文字，支持多种文件格式
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> VoiceToText(byte[]  bytes, string fileName)
    {
        if (fileName.EndsWith(".opus"))
        {
            var random =  new Random().Next(100000, 999999).ToString();
            bytes = AudioService.ConvertOpusToWav(bytes, random);
            fileName = fileName.Replace(".opus", ".wav");
        }
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        client.Timeout = TimeSpan.FromSeconds(300);
        var url = hostUrl + "v1/audio/transcriptions";
        var defaultPrompt = "日常问题，可能涉及工业品专用品牌或术语，[火也]是一个专有品牌名词。";
        var content = new MultipartFormDataContent
        {
            { new StringContent("whisper-1"), "model" }, //固定值
            { new StringContent("zh"), "language" },
            { new StringContent(defaultPrompt), "prompt" },
            { new StringContent("json"), "response_format" },
            { new ByteArrayContent(bytes), "file", fileName}
        };
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        });
        var respContent = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var json = JObject.Parse(respContent);
            return Result.Answer(json["text"].Value<string>());
        }
        else
            return Result.Error(respContent);
    }
    
    private string GetTTSMsgBody(string text, string voiceName, string audioFormat)
    {
        var voice = "nova";
        if (!string.IsNullOrEmpty(voiceName) && voiceName.StartsWith("openai_"))
            voice = voiceName.Replace("openai_", "");
        return JsonConvert.SerializeObject(new
        {
            input = text,
            model = "tts-1-hd-1106",
            voice = voice,
            response_format = string.IsNullOrEmpty(audioFormat) ? "mp3" : audioFormat
        });
    }

    /// <summary>
    /// OpenAI文字转语音
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> TextToVoice(string text, string voideName, string audioFormat)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = hostUrl + "v1/audio/speech";
        var msg = GetTTSMsgBody(text, voideName, audioFormat);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsByteArrayAsync();
        if (resp.IsSuccessStatusCode)
            return FileResult.Answer(content, "mp3", ResultType.AudioBytes, duration:text.Length*15*1000/68); //拿不到返回的音频时长，根据字数预估
        else
            return Result.Error(Encoding.UTF8.GetString(content));
    }
    
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings,
            model = "text-embedding-3-large",
            dimensions = 1536
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
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = hostUrl + "v1/embeddings";
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


/// <summary>
/// OpenAI流式语音转文字扩展类
/// </summary>
public class OpenAIAudioStreamClient : OpenAIClient, IAiWebSocketProxy
{
    private string hostUrl;
    private string apiKey;
    public OpenAIAudioStreamClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository,  ConfigHelper configuration) : base(httpClientFactory, functionRepository)
    {
        hostUrl = configuration.GetConfig<string>("Service:OpenAI:WssHost");
        apiKey = configuration.GetConfig<string>("Service:OpenAI:Key");
    }

    private BlockingCollection<Result>? _results;
    private ClientWebSocket ws;
    private List<Task> tasks = new List<Task>();
    bool addAnswerFinished = false;
    bool closeCallbackCalled = false;
    public async Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="")
    {
        _results = new BlockingCollection<Result>();
        var url = $"{hostUrl}v1/realtime?model=gpt-4o-realtime-preview-2024-12-17";
        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
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
        var proxy = configuration.GetConfig<string>("Service:OpenAI:Host");
        var apikey = configuration.GetConfig<string>("Service:OpenAI:Key");
        _client = new RestClient(proxy);
        _client.AddDefaultHeader("Authorization", "Bearer " + apikey);
        _client.AddDefaultParameter("OpenAI-Beta", "assistants=v2", ParameterType.HttpHeader);
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public RestClient GetRestClient()
    {
        return _client;
    }
    
    public HttpClient GetHttpClient()
    {
        var proxy = _configuration.GetConfig<string>("Service:OpenAI:Host");
        var apikey = _configuration.GetConfig<string>("Service:OpenAI:Key");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apikey);
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
        client.BaseAddress = new Uri(proxy);
        client.Timeout = TimeSpan.FromSeconds(300);
        return client;
    }
}
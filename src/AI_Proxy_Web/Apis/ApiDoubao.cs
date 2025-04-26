using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebHeaderCollection = System.Net.WebHeaderCollection;
using WebSocketState = System.Net.WebSockets.WebSocketState;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.字节豆包, "字节豆包", "豆包 1.5Pro 是字节新推出的大模型，32K上下文，图片理解能力强。", 28, canProcessImage:true, canProcessMultiImages:true, canUseFunction:false, priceIn: 0.8, priceOut: 2)]
public class ApiDoubao:ApiBase
{
    protected DoubaoClient _client;
    public ApiDoubao(IServiceProvider serviceProvider, ConfigHelper configHelper):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DoubaoClient>();
        _client.SetModel(configHelper.GetConfig<string>("Service:Doubao:ModelId"));
        _client.SetVisionModel(configHelper.GetConfig<string>("Service:Doubao:VisionModelId"));
    }

    /// <summary>
    /// 
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
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 支持字符串数组，返回向量2048维
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.Doubao_DeepSeekR1, "DS R1火山版", "DeepSeek R1火山引擎备用接口，自带搜索引擎可以联网搜索后回答。", 121, type: ApiClassTypeEnum.推理模型, priceIn: 4, priceOut: 16)]
public class ApiDoubaoDeepSeekR1 : ApiDoubao
{
    public ApiDoubaoDeepSeekR1(IServiceProvider serviceProvider, ConfigHelper configHelper):base(serviceProvider, configHelper)
    {
        _client.SetModel(configHelper.GetConfig<string>("Service:Doubao:R1ModelId"));
    }
}

[ApiClass(M.Doubao_DeepSeekV3, "DS V3火山版", "DeepSeek V3火山引擎备用接口，自带搜索引擎可以联网搜索后回答。", 38, type: ApiClassTypeEnum.问答模型, priceIn: 2, priceOut: 8)]
public class ApiDoubaoDeepSeekV3 : ApiDoubao
{
    public ApiDoubaoDeepSeekV3(IServiceProvider serviceProvider, ConfigHelper configHelper):base(serviceProvider, configHelper)
    {
        _client.SetModel(configHelper.GetConfig<string>("Service:Doubao:V3ModelId"));
    }
}

[ApiClass(M.Doubao_Thinking, "豆包Thinking", "豆包Thinking Pro推理模型，支持图片推理。", 129, type: ApiClassTypeEnum.推理模型, canProcessImage:true, priceIn: 4, priceOut: 16)]
public class ApiDoubaoThinking : ApiDoubao
{
    public ApiDoubaoThinking(IServiceProvider serviceProvider, ConfigHelper configHelper):base(serviceProvider, configHelper)
    {
        _client.SetModel(configHelper.GetConfig<string>("Service:Doubao:ThinkingModelId"));
        _client.SetVisionModel(configHelper.GetConfig<string>("Service:Doubao:ThinkingVisionModelId"));
        _client.MaxTokens = 16000;
    }
}

[ApiClass(M.豆包Seaweed, "豆包Seaweed", "豆包Seaweed 是字节推出的文本生成视频模型，选定视频的尺寸之后直接输入要画的场景描述，中英文都可以，可以上传一张图片作为首帧。", 224, type: ApiClassTypeEnum.视频模型, priceIn: 0, priceOut: 2)]
public class ApiDpubapSeaweed:ApiDoubao
{
    public ApiDpubapSeaweed(IServiceProvider serviceProvider, ConfigHelper configHelper):base(serviceProvider, configHelper)
    {
        _client.SetVideoModel(configHelper.GetConfig<string>("Service:Doubao:SeaweedModelId"));
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.TextToVideo(input))
        {
            yield return resp;
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("视频接口不支持Query调用");
    }

    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetExtraOptions(ext_userId, type, value);
    }
}


/// <summary>
/// 豆包大模型接口
/// 文档地址 https://www.volcengine.com/docs/82379/1298454
/// </summary>
public class DoubaoClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public DoubaoClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:Doubao:Key");
        ttsAppId = configHelper.GetConfig<string>("Service:Doubao:TtsAppId");
        ttsToken = configHelper.GetConfig<string>("Service:Doubao:TtsToken");
    }
    
    private static string hostUrl = "https://ark.cn-beijing.volces.com/api/v3/chat/completions";
    private static string botHostUrl = "https://ark.cn-beijing.volces.com/api/v3/bots/chat/completions";
    private static string embedUrl = "https://ark.cn-beijing.volces.com/api/v3/embeddings";
    private string modelName = "";
    private string visionModelName = "";
    private string videoModelName = "";
    private string APIKEY ;
    private bool isBot = false;
    protected string ttsAppId;
    protected string ttsToken;

    public void SetModel(string name)
    {
        modelName = name;
        if(name.StartsWith("bot-"))
            isBot = true;
    }   
    public void SetVisionModel(string name)
    {
        visionModelName = name;
    }

    public void SetVideoModel(string name)
    {
        videoModelName = name;
    }

    public int MaxTokens = 8192;
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? visionModelName : modelName;
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            temperature = input.Temprature,
            tools = tools,
            stream,
            max_tokens = MaxTokens,
            user = input.External_UserId
        }, jSetting);
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = isBot ? botHostUrl : hostUrl;
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = isBot ? botHostUrl : hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
    
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings,
            model="ep-20241219074135-4nz8h"
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
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+ APIKEY);
        var url = embedUrl;
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

    public async Task<Result> TextToVoice(string text, string voiceName, string audioFormat)
    {
        voiceName = "zh_male_yuanboxiaoshu_moon_bigtts"; //zh_male_ahu_conversation_wvae_bigtts
        var url = "https://openspeech.bytedance.com/api/v1/tts";
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer; {ttsToken}");
        var random =  new Random().Next(1000000, 9999999).ToString();
        var msg = JsonConvert.SerializeObject(new
        {
            app = new
            {
                appid = ttsAppId,
                token = "access_token",
                cluster = "volcano_tts"
            },
            user = new { uid = random },
            audio = new
            {
                voice_type = voiceName,
                encoding = string.IsNullOrEmpty(audioFormat)?"mp3":audioFormat , //wav / pcm / ogg_opus / mp3
                rate = 24000
            },
            request = new
            {
                reqid = Guid.NewGuid().ToString("N"),
                text = text,
                operation = "query"
            }
        });
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["code"] != null && json["code"].Value<int>() == 3000)
        {
            var base64 = json["data"].Value<string>();
            var file = Convert.FromBase64String(base64);
            var format = "mp3";
            if (audioFormat != format)
            {
                file = AudioService.ConvertAudioFormat(file, format, audioFormat, random);
                format = audioFormat;
            }

            return FileResult.Answer(file, audioFormat, ResultType.AudioBytes,
                duration: int.Parse(json["addition"]["duration"].Value<string>()));
        }
        else
            return Result.Error(content);
    }
    
    public async IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input)
    {
        var content = input.ChatContexts.Contexts.Last().QC.Last().Content;
        //该接口每次最大输入文字有1000字限制，需要自己进行截取，分段转语音
        var ss = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var s1 in ss)
        {
            List<string> subs = new List<string>();
            if (s1.Length < 200)
                subs.Add(s1);
            else
            {
                var start = 0;
                var eIndex = s1.IndexOf('。');
                while (eIndex > 0)
                {
                    subs.Add(s1.Substring(start, eIndex + 1 - start));
                    start = eIndex + 2;
                    if (start >= s1.Length)
                        break;
                    eIndex = s1.IndexOf('。', start);
                }

                if (start < s1.Length)
                    subs.Add(s1.Substring(start));
            }

            foreach (var s in subs)
            {
                sb.AppendLine(s);
                if (sb.Length + s.Length >= 600)
                {
                    input.ChatContexts.Contexts.Last().QC.Last().Content = sb.ToString();
                    await foreach (var resp in DoTextToVoiceStream(input))
                    {
                        yield return resp;
                    }

                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
        {
            input.ChatContexts.Contexts.Last().QC.Last().Content = sb.ToString();
            await foreach (var resp in DoTextToVoiceStream(input))
            {
                yield return resp;
            }
        }
    }


    private async IAsyncEnumerable<Result> DoTextToVoiceStream(ApiChatInputIntern input)
    {
        var results = new BlockingCollection<Result>();
        input.AudioVoice = "zh_male_yuanboxiaoshu_moon_bigtts"; //zh_male_ahu_conversation_wvae_bigtts
        var url = "wss://openspeech.bytedance.com/api/v1/tts/ws_binary";
        using (var ws = new ClientWebSocket())
        { 
            ws.Options.SetRequestHeader("Authorization", $"Bearer; {ttsToken}");
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            Task.Run(async () =>
            {
                var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
                while (ws.State == WebSocketState.Open)
                {
                    var bytesList = new List<byte>();
                    WebSocketReceiveResult result;
                    do
                    {
                        result =
                            await ws.ReceiveAsync(buffer, CancellationToken.None);
                        bytesList.AddRange(buffer.Array[..result.Count]);
                    }while(!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        break;
                    }
                    else
                    {
                        var bytes = bytesList.ToArray();
                        int protocolVersion = (bytes[0] & 0xff) >> 4;
                        int headerSize = bytes[0] & 0x0f;
                        int messageType = (bytes[1] & 0xff) >> 4;
                        int messageTypeSpecificFlags = bytes[1] & 0x0f;
                        int serializationMethod = (bytes[2] & 0xff) >> 4;
                        int messageCompression = bytes[2] & 0x0f;
                        int reserved = bytes[3] & 0xff;
                        int position = headerSize * 4;
                        byte[] fourByte = new byte[4];

                        if (messageType == 11)
                        {
                            // Audio-only server response
                            if (messageTypeSpecificFlags == 0)
                            {
                                // Ack without audio data
                            }
                            else
                            {
                                Array.Copy(bytes, position, fourByte, 0, 4);
                                position += 4;
                                int sequenceNumber = IntFromBigEndianBytes(fourByte);
                                Array.Copy(bytes, position, fourByte, 0, 4);
                                position += 4;
                                int payloadSize = IntFromBigEndianBytes(fourByte);
                                byte[] payload = new byte[payloadSize];
                                Array.Copy(bytes, position, payload, 0, payloadSize);
                                
                                results.Add(FileResult.Answer(payload, "pcm", ResultType.AudioBytes));
                                if (sequenceNumber < 0)
                                {
                                    // received the last segment
                                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                                    break;
                                }
                            }
                        }
                        else if (messageType == 15)
                        {
                            // Error message from server
                            Array.Copy(bytes, position, fourByte, 0, 4);
                            position += 4;
                            int code = IntFromBigEndianBytes(fourByte);
                            Array.Copy(bytes, position, fourByte, 0, 4);
                            position += 4;
                            int messageSize = IntFromBigEndianBytes(fourByte);
                            byte[] messageBytes = new byte[messageSize];
                            Array.Copy(bytes, position, messageBytes, 0, messageSize);
                            string message = Encoding.UTF8.GetString(messageBytes);
                            Console.WriteLine($"{code} {message}");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"Received unknown response message type: {messageType}");
                            break;
                        }
                    }
                }
                results.CompleteAdding();
            });
            if (ws.State == WebSocketState.Open)
            {
                var msg = JsonConvert.SerializeObject(new
                {
                    app = new
                    {
                        appid = ttsAppId,
                        token = "access_token",
                        cluster = "volcano_tts"
                    },
                    user = new { uid = input.External_UserId },
                    audio = new
                    {
                        voice_type = input.AudioVoice,
                        encoding = string.IsNullOrEmpty(input.AudioFormat)
                            ? "mp3"
                            : input.AudioFormat, //wav / pcm / ogg_opus / mp3
                        rate = 24000
                    },
                    request = new
                    {
                        reqid = Guid.NewGuid().ToString("N"),
                        text = input.ChatContexts.Contexts.Last().QC.Last().Content,
                        operation = "submit"
                    }
                });
                byte[] jsonBytes = Encoding.UTF8.GetBytes(msg);
                byte[] header = [0x11, 0x10, 0x10, 0x00];

                using (var requestStream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(requestStream))
                    {
                        writer.Write(header);
                        writer.Write(IntToBigEndianBytes(jsonBytes.Length));
                        writer.Write(jsonBytes);
                    }

                    byte[] requestBytes = requestStream.ToArray();   
                    await ws.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Binary,
                        WebSocketMessageFlags.EndOfMessage, CancellationToken.None);

                }

                foreach (var result in results.GetConsumingEnumerable())
                {
                    yield return result;
                }
            }
        }
    }
    
    protected byte[] IntToBigEndianBytes(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);

        // 如果系统是小端序，将字节数组反转为大端序
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }
    protected int IntFromBigEndianBytes(byte[] bytes)
    { 
        Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }
    
    //视频生成参数
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("横屏", "16:9"),
                    new KeyValuePair<string, string>("竖屏", "9:16")
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

    public async IAsyncEnumerable<Result> TextToVideo(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var url = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks";
        var prompt = input.ChatContexts.Contexts.Last().QC.First(t => t.Type == ChatType.文本).Content;
        var image = input.ChatContexts.Contexts.Last().QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var opts = GetExtraOptions(input.External_UserId);
        prompt += " --ratio " + opts[0].CurrentValue;
        var msg = JsonConvert.SerializeObject(new
        {
            model = videoModelName,
            content = new[]
            {
                new { type = "text", text = prompt }
            }
        }, jSetting);
        if (!string.IsNullOrEmpty(image))
        {
            msg = JsonConvert.SerializeObject(new
            {
                model = videoModelName,
                content = new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + image } }
                }
            }, jSetting);
        }

        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var result = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(result);
        if (o["id"] != null)
        {
            var taskId = o["id"].Value<string>();
            await foreach (var resp2 in CheckVideoTask(taskId))
            {
                yield return resp2;
            }
        }
        else
        {
            yield return Result.Error(result);
        }
    }

    public async IAsyncEnumerable<Result> CheckVideoTask(string taskid)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks/"+taskid;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["status"] != null)
            {
                var state = json["status"].Value<string>();
                if (state == "queued" || state == "running")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "succeeded")
                {
                    yield return Result.Waiting("生成完成，下载视频...");
                    var video_url = json["content"]["video_url"].Value<string>();
                    var bytes = await client.GetByteArrayAsync(video_url);
                    yield return VideoFileResult.Answer(bytes, "mp4", "video.mp4");
                    yield break;
                }
                else
                {
                    yield return Result.Error(content);
                    yield break;
                }
            }
            else
            {
                yield return Result.Error(content);
                yield break;
            }
        }
    }
}


/// <summary>
/// 豆包流式语音转文字扩展类
/// </summary>
public class DoubaoAudioStreamClient : DoubaoClient, IAiWebSocketProxy
{
    public DoubaoAudioStreamClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper) : base(httpClientFactory, functionRepository, configHelper)
    {
    }

    private BlockingCollection<Result>? _results;
    private ClientWebSocket ws;
    private List<Task> tasks = new List<Task>();
    bool addAnswerFinished = false;
    bool closeCallbackCalled = false;
    public async Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="")
    {
        _results = new BlockingCollection<Result>();
        var url = "wss://openspeech.bytedance.com/api/v2/asr";
        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer; {ttsToken}");
        
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        var t = Task.Run(async () =>
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            try
            {
                while (ws.State == WebSocketState.Open)
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
                        int protocolVersion = (bytes[0] & 0xff) >> 4;
                        int headerSize = bytes[0] & 0x0f;
                        int messageType = (bytes[1] & 0xff) >> 4;
                        int messageTypeSpecificFlags = bytes[1] & 0x0f;
                        int serializationMethod = (bytes[2] & 0xff) >> 4;
                        int messageCompression = bytes[2] & 0x0f;
                        int reserved = bytes[3] & 0xff;
                        int position = headerSize * 4;
                        byte[] fourByte = new byte[4];

                        Array.Copy(bytes, position, fourByte, 0, 4);
                        position += 4;
                        int payloadSize = IntFromBigEndianBytes(fourByte);
                        byte[] payload = new byte[payloadSize];
                        Array.Copy(bytes, position, payload, 0, payloadSize);
                        string message = Encoding.UTF8.GetString(payload);
                        if (messageType == 9)
                        {
                            var o = JObject.Parse(message);
                            if (o["result"] != null && o["result"][0]["text"] != null)
                                _results.Add(Result.New(ResultType.AnswerSummation, o["result"][0]["text"].Value<string>()));
                            if (o["sequence"].Value<int>() < 0)
                            {
                                //收到标记为最后一条消息
                                // break;
                            }
                        }
                        else if (messageType == 15)
                        {
                            Console.WriteLine(message);
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"Received unknown response message type: {messageType}");
                            break;
                        }
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
        //发送第一条消息，确定请求参数
        if (ws.State == WebSocketState.Open)
        {
            var msg = JsonConvert.SerializeObject(new
            {
                app = new
                {
                    appid = ttsAppId,
                    token = "access_token",
                    cluster = "volcengine_streaming_common" // volcengine_streaming_common 通用 volcengine_streaming 办公 volcengine_tele_general_streaming 试用
                },
                user = new { uid = Guid.NewGuid().ToString("N") },
                audio = new
                {
                    format = "raw",
                    codec = "raw", //raw=pcm
                    rate = 16000
                },
                request = new
                {
                    reqid = Guid.NewGuid().ToString("N"),
                    sequence = 1
                }
            });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(msg);
            byte[] header = [0x11, 0x10, 0x10, 0x00];

            using (var requestStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(requestStream))
                {
                    writer.Write(header);
                    writer.Write(IntToBigEndianBytes(jsonBytes.Length));
                    writer.Write(jsonBytes);
                }

                byte[] requestBytes = requestStream.ToArray();
                await ws.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Binary,
                    WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
            }
        }
        
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
            byte[]? lastData = null;
            bool isFinal = false;
            foreach (var data in messageQueue.GetConsumingEnumerable())
            {
                if(ws.State != WebSocketState.Open)
                    break;
                if (data is string s)
                {
                    if (s == AiWebSocketServer.finishMessage)
                    {
                        isFinal = true;
                    }
                }

                if (lastData != null)
                {
                    byte[] header = [0x11, 0x20, 0x10, 0x00];
                    if (isFinal)
                        header = [0x11, 0x22, 0x10, 0x00];
                    
                    using (var requestStream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(requestStream))
                        {
                            writer.Write(header);
                            writer.Write(IntToBigEndianBytes(lastData.Length));
                            writer.Write(lastData);
                        }
                        byte[] requestBytes = requestStream.ToArray();
                        await ws.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Binary,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                    }
                }

                lastData = null;
                if (data is byte[] bytes)
                    lastData = bytes;
            }
        });
        tasks.Add(t2);
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


/// <summary>
/// 豆包流式语音转文字扩展类，大模型版V3接口
/// 文档地址：https://www.volcengine.com/docs/6561/1324606
/// </summary>
public class DoubaoAudioStreamV3Client : DoubaoClient, IAiWebSocketProxy
{
    public DoubaoAudioStreamV3Client(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper) : base(httpClientFactory, functionRepository, configHelper)
    {
    }

    private BlockingCollection<Result>? _results;
    private ClientWebSocket ws;
    private List<Task> tasks = new List<Task>();
    bool addAnswerFinished = false;
    bool closeCallbackCalled = false;
    public async Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="")
    {
        _results = new BlockingCollection<Result>();
        var url = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel";  //bigmodel实时返回每个字，bigmodel_nostream按整句返回
        ws = new ClientWebSocket();
        //验证方式与V2版有区别
        ws.Options.SetRequestHeader("X-Api-App-Key", ttsAppId);
        ws.Options.SetRequestHeader("X-Api-Access-Key", ttsToken);
        ws.Options.SetRequestHeader("X-Api-Resource-Id", $"volc.bigasr.sauc.duration");
        ws.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString("D"));
        
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        var t = Task.Run(async () =>
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            try
            {
                while (ws.State == WebSocketState.Open)
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
                        int protocolVersion = (bytes[0] & 0xff) >> 4;
                        int headerSize = bytes[0] & 0x0f;
                        int messageType = (bytes[1] & 0xff) >> 4;
                        int messageTypeSpecificFlags = bytes[1] & 0x0f;
                        int serializationMethod = (bytes[2] & 0xff) >> 4;
                        int messageCompression = bytes[2] & 0x0f;
                        int reserved = bytes[3] & 0xff;
                        int position = headerSize * 4;
                        byte[] fourByte = new byte[4];

                        Array.Copy(bytes, position, fourByte, 0, 4);
                        position += 4;
                        int payloadSize = IntFromBigEndianBytes(fourByte);
                        byte[] payload = new byte[payloadSize];
                        Array.Copy(bytes, position, payload, 0, payloadSize);
                        string message = Encoding.UTF8.GetString(payload);
                        if (messageType == 9)
                        {
                            var o = JObject.Parse(message);
                            if (o["result"] != null && o["result"][0]["text"] != null)
                                _results.Add(Result.New(ResultType.AnswerSummation, o["result"][0]["text"].Value<string>()));
                        }
                        else if (messageType == 15)
                        {
                            Console.WriteLine(message);
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"Received unknown response message type: {messageType}");
                            break;
                        }
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
        //发送第一条消息，确定请求参数
        if (ws.State == WebSocketState.Open)
        {
            var msg = JsonConvert.SerializeObject(new
            {
                user = new { uid = Guid.NewGuid().ToString("N") },
                audio = new
                {
                    format = "raw",
                    codec = "raw", //raw=pcm
                    rate = 16000
                },
                request = new
                {
                    model_name = "bigmodel",
                    enable_itn = false,
                    enable_ddc = false, //启用顺滑
                    enable_punc = false //启用标点
                }
            });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(msg);
            byte[] header = [0x11, 0x10, 0x10, 0x00];

            using (var requestStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(requestStream))
                {
                    writer.Write(header);
                    writer.Write(IntToBigEndianBytes(jsonBytes.Length));
                    writer.Write(jsonBytes);
                }

                byte[] requestBytes = requestStream.ToArray();
                await ws.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Binary,
                    WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
            }
        }
        
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
            byte[]? lastData = null;
            bool isFinal = false;
            foreach (var data in messageQueue.GetConsumingEnumerable())
            {
                if(ws.State != WebSocketState.Open)
                    break;
                if (data is string s)
                {
                    if (s == AiWebSocketServer.finishMessage)
                    {
                        isFinal = true;
                    }
                }

                if (lastData != null)
                {
                    byte[] header = [0x11, 0x20, 0x10, 0x00];
                    if (isFinal)
                        header = [0x11, 0x22, 0x10, 0x00];
                    
                    using (var requestStream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(requestStream))
                        {
                            writer.Write(header);
                            writer.Write(IntToBigEndianBytes(lastData.Length));
                            writer.Write(lastData);
                        }
                        byte[] requestBytes = requestStream.ToArray();
                        await ws.SendAsync(new ArraySegment<byte>(requestBytes), WebSocketMessageType.Binary,
                            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                    }
                }

                lastData = null;
                if (data is byte[] bytes)
                    lastData = bytes;
            }
        });
        tasks.Add(t2);
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
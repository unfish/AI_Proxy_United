using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Apis.V2.Extra;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("Doubao")]
public class ApiDoubaoProvider : ApiOpenAIProvider
{
    public ApiDoubaoProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }

    protected string ttsAppId;
    protected string ttsToken;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        if (_modelName.StartsWith("bot-"))
        {
            _host += "bots/";
            _chatUrl = _host + "chat/completions";
        }
        ttsAppId = configHelper.GetProviderConfig<string>(attr.Provider, "TtsAppId");
        ttsToken = configHelper.GetProviderConfig<string>(attr.Provider, "TtsToken");
        
        if (attr.UseThinkingMode)
        {
            extraOptionsList = new List<ExtraOption>()
            {
                new ExtraOption()
                {
                    Type = "思考深度", Contents = new[]
                    {
                        new KeyValuePair<string, string>("极简", "minimal"),
                        new KeyValuePair<string, string>("低", "low"),
                        new KeyValuePair<string, string>("中", "medium"),
                        new KeyValuePair<string, string>("高", "high")
                    }
                }
            };
        }
    }

    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => new { type = "text", text = t.Content }).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings,
            model= apiClassAttribute.EmbeddingModelName,
            dimensions = apiClassAttribute.EmbeddingDimensions
        });
    }
    private class EmbeddingsResponse
    {
        public string Object { get; set; }
        public EmbeddingObject Data { get; set; }
    }
    private class EmbeddingObject
    {
        public string Object { get; set; }
        public double[] Embedding { get; set; }
    }

    private string embedUrl = "https://ark.cn-beijing.volces.com/api/v3/embeddings/multimodal";
    public override async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+ _key);
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
            return (ResultType.Answer, new double[][]{result.Data.Embedding}, string.Empty);
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
                file = ApiAudioServiceProvider.ConvertAudioFormat(file, format, audioFormat, random);
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
                    await foreach (var resp in DoTextToVoiceStreamV2(input))
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
            await foreach (var resp in DoTextToVoiceStreamV2(input))
            {
                yield return resp;
            }
        }
    }

    private async IAsyncEnumerable<Result> DoTextToVoiceStreamV2(ApiChatInputIntern input)
    {
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-App-Id", ttsAppId);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Access-Key", ttsToken);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Resource-Id", "seed-tts-2.0");
        var voice = "zh_female_vv_uranus_bigtts";
        if (!string.IsNullOrEmpty(input.AudioVoice) && input.AudioVoice.StartsWith("doubao_"))
            voice = input.AudioVoice.Replace("doubao_", "");
        var msg = JsonConvert.SerializeObject(new
        {
            user = new { uid = input.External_UserId },
            req_params = new
            {
                text = input.ChatContexts.Contexts.Last().QC.Last().Content,
                speaker = voice,
                audio_params = new
                {
                    format = input.AudioFormat,
                    sample_rate = 24000
                }
            }
        });
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://openspeech.bytedance.com/api/v3/tts/unidirectional")
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);
        using (var stream = await resp.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(line);
                yield break;
            }

            var index = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);
                if (!string.IsNullOrEmpty(line))
                {
                    var o = JObject.Parse(line);
                    if (o["data"] != null && o["code"].Value<int>() == 0)
                    {
                        var audio = o["data"].Value<string>();
                        if (!string.IsNullOrEmpty(audio))
                        {
                            var bytes = Convert.FromBase64String(audio);
                            if (bytes.Length > 0)
                                yield return FileResult.Answer(bytes, input.AudioFormat, ResultType.AudioBytes);
                        }
                    }
                }
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
    
}

[ApiProvider("DoubaoVideo")]
public class ApiDoubaoVideoProvider : ApiDoubaoProvider
{
    public ApiDoubaoVideoProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        extraOptionsList = new List<ExtraOption>()
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
    }
    
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        await foreach (var resp in TextToVideo(input))
        {
            yield return resp;
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
    }
    
    public async IAsyncEnumerable<Result> TextToVideo(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        var url = "https://ark.cn-beijing.volces.com/api/v3/contents/generations/tasks";
        var prompt = input.ChatContexts.Contexts.Last().QC.First(t => t.Type == ChatType.文本).Content;
        var image = input.ChatContexts.Contexts.Last().QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var opts = GetExtraOptions(input.External_UserId);
        prompt += "  --resolution 1080p  --duration 5 --ratio " + opts[0].CurrentValue;
        var msg = JsonConvert.SerializeObject(new
        {
            model = _modelName,
            content = new[]
            {
                new { type = "text", text = prompt }
            }
        }, jSetting);
        if (!string.IsNullOrEmpty(image))
        {
            msg = JsonConvert.SerializeObject(new
            {
                model = _modelName,
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
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
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
/// 豆包流式语音转文字扩展类，大模型版V3接口
/// 文档地址：https://www.volcengine.com/docs/6561/1324606
/// </summary>
[ApiProvider("DoubaoSSR")]
public class ApiDoubaoSSRV3Client : ApiDoubaoProvider, IAiWebSocketProxy
{
    public ApiDoubaoSSRV3Client(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
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
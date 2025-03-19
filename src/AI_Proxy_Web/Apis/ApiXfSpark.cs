using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using FFMpegCore.Arguments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;


[ApiClass(M.讯飞星火, "讯飞星火", "讯飞星火4.0。", 39,  canUseFunction:false, priceIn: 70, priceOut: 70)]
public class ApiXfSpark:ApiBase
{
    private XfSparkClient _client;

    public ApiXfSpark(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<XfSparkClient>();
    }
    
    /// <summary>
    /// 使用讯飞星火来回答
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
    /// 星火没有非流式的接口，所以使用流式接口接收数据，拼起来再返回
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }

}


/// <summary>
/// 讯飞星火大模型接口
/// 文档地址 https://www.xfyun.cn/doc/spark/Web.html
/// </summary>
public class XfSparkClient: OpenAIClientBase, IApiClient
{
    private static String hostUrl = "https://spark-api-open.xf-yun.com/v1/chat/completions"; //星火问答
    private static string modelName = "4.0Ultra";
    private static String ttsHostUrl = "wss://tts-api.xfyun.cn/v2/tts"; //文本转语音
    protected String APPID;//从开放平台控制台中获取
    private String APIKEY;//从开放平台控制台中获取
    private String APISecret;//从开放平台控制台中获取

    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public XfSparkClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APPID = configHelper.GetConfig<string>("Service:XunFei:APPID");
        APIKEY = configHelper.GetConfig<string>("Service:XunFei:Key");
        APISecret = configHelper.GetConfig<string>("Service:XunFei:Secret");
    }
    
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = GetBasicMessages(input.ChatContexts),
            temperature = input.Temprature,
            stream,
            user = input.External_UserId
        });
    }
    
    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}:{APISecret}");
        var url = hostUrl;
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}:{APISecret}");
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        
        return await ProcessQueryResponse(resp);
    }

    
    //鉴权url
    protected string getAuthorizationUrl(string hostUrl)
    {
        var url = new Uri(hostUrl);
        String date = DateTime.UtcNow.ToString("R");
        //获取signature_origin字段
        StringBuilder builder = new StringBuilder("host: ").Append(url.Host).Append("\n").Append("date: ").Append(date)
            .Append("\n").Append("GET ").Append(url.AbsolutePath).Append(" HTTP/1.1");
        //获得signatue
        String signature = HashHelper.GetSha256Str(APISecret, builder.ToString());
        //获得 authorization_origin
        String authorization_origin =
            String.Format("api_key=\"{0}\",algorithm=\"{1}\",headers=\"{2}\",signature=\"{3}\"", APIKEY, "hmac-sha256",
                "host date request-line", signature);
        //获得authorization
        String authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(authorization_origin));

        var dateEnc = HttpUtility.UrlEncode(date);
        return hostUrl+"?authorization="+HttpUtility.UrlEncode(authorization)+"&date="+dateEnc+"&host="+url.Host;
    }
    
    public async IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input)
    {
        var text = input.ChatContexts.Contexts.Last().QC.Last().Content;
        using (var ws = new ClientWebSocket())
        {
            var url = getAuthorizationUrl(ttsHostUrl);
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            var msg = new
            {
                common = new{app_id=APPID},
                business=new
                {
                    aue = "lame", sfl = 1, vcn = input.AudioVoice, tte="UTF8"
                },
                data=new
                {
                    status = 2,
                    text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                }
            };
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(msg))), WebSocketMessageType.Text,
                WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            IEnumerable<byte> audio = new List<byte>();
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
                else
                {
                    var str = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    sb.Append(str);
                    if (result.EndOfMessage)
                    {
                        var json = JObject.Parse(sb.ToString());
                        if (json["message"].Value<string>() == "success" && json["data"]["audio"]!=null)
                        {
                            var audioText = json["data"]["audio"].Value<string>();
                            if (!string.IsNullOrEmpty(audioText))
                            {
                                yield return FileResult.Answer(Convert.FromBase64String(audioText), "mp3", ResultType.AudioBytes);
                                if (json["data"]["status"].Value<int>() == 2)
                                {
                                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null,
                                        CancellationToken.None);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            yield return Result.Error(sb.ToString());
                        }
                        sb.Clear();
                    }
                }
            }
        }
    }
}


/// <summary>
/// 讯飞流式语音转文字扩展类
/// https://www.xfyun.cn/doc/nlp/simultaneous-interpretation/API.html
/// </summary>
public class XfAudioStreamClient : XfSparkClient, IAiWebSocketProxy
{
    public XfAudioStreamClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper) : base(httpClientFactory, functionRepository, configHelper)
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
        var url = getAuthorizationUrl("wss://ws-api.xf-yun.com/v1/private/simult_interpretation")+"&serviceId=simult_interpretation";
        ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(url), CancellationToken.None);
        var t = Task.Run(async () =>
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            string srcFinal = "", srcTmp = "", dstFinal = "", dstTmp = "";
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
                        if (o["header"]["code"].Value<int>() != 0)
                            _results.Add(Result.Error(o["header"]["message"].Value<string>()));
                        else
                        {
                            if (o["payload"] != null)
                            {
                                if (o["payload"]["streamtrans_results"] != null)
                                {
                                    var trans = o["payload"]["streamtrans_results"]["text"].Value<string>();
                                    if (!string.IsNullOrEmpty(trans))
                                    {
                                        trans = Encoding.UTF8.GetString(Convert.FromBase64String(trans));
                                        var ot = JObject.Parse(trans);
                                        if (ot["is_final"].Value<int>() == 1)
                                        {
                                            srcFinal += ot["src"].Value<string>();
                                            dstFinal += ot["dst"].Value<string>();
                                            srcTmp = "";
                                            dstTmp = "";
                                        }
                                        else
                                        {
                                            srcTmp = ot["src"].Value<string>();
                                            dstTmp = ot["dst"].Value<string>();
                                        }
                                    }

                                    _results.Add(Result.New(ResultType.AnswerSummation, srcFinal + srcTmp));
                                    _results.Add(Result.New(ResultType.Translation, dstFinal + dstTmp));
                                }

                                if (o["payload"]["tts_results"] != null)
                                {
                                    var tts = o["payload"]["tts_results"]["audio"].Value<string>();
                                    _results.Add(FileResult.Answer(Convert.FromBase64String(tts), "pcm", ResultType.AudioBytes));
                                }
                            }
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
            int status = 0;
            foreach (var data in messageQueue.GetConsumingEnumerable())
            {
                if (ws.State != WebSocketState.Open)
                    break;
                if (data is byte[] bytes)
                {
                    //发送语音数据
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(GetMessageBody(status, bytes, extraParams));
                    if (status == 0) status = 1;
                    await ws.SendAsync(new ArraySegment<byte>(jsonBytes), WebSocketMessageType.Text,
                        WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                }
            }
            //发送结束消息
            byte[] jsonBytes2 = Encoding.UTF8.GetBytes(GetMessageBody(2, new byte[0], extraParams));
            await ws.SendAsync(new ArraySegment<byte>(jsonBytes2), WebSocketMessageType.Text,
                WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
        });
        tasks.Add(t2);
    }

    private string GetMessageBody(int status, byte[] bytes, string extraParams)
    {
        if (extraParams != "cn-en" && extraParams != "en-cn")
            extraParams = "en-cn";
        var l2l = extraParams.Split("-");
        var msg = JsonConvert.SerializeObject(new
        {
            header = new
            {
                app_id = APPID,
                status = status
            },
            parameter = new
            {
                ist = new
                {
                    accent = "mandarin",
                    domain = "ist_ed_open",
                    language = "zh_cn",
                    vto = 15000,
                    eos = 150000,
                    language_type = l2l[0] == "en" ? 3 : 2
                },
                streamtrans = new
                {
                    from = l2l[0],
                    to = l2l[1]
                },
                tts = new
                {
                    vcn = l2l[1] == "cn" ? "x2_xiaozhong" : "x2_john",
                    tts_results = new
                    {
                        encoding = "raw",
                        sample_rate = 16000,
                        channels = 1,
                        bit_depth = 16,
                        frame_size = 0
                    }
                }
            },
            payload = new
            {
                data = new
                {
                    audio = Convert.ToBase64String(bytes),
                    encoding = "raw",
                    sample_rate = 16000,
                    seq = 1,
                    status = status
                }
            }
        });
        return msg;
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
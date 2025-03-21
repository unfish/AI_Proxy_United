using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
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
using TencentCloud.Asr.V20190614;
using TencentCloud.Asr.V20190614.Models;
using TencentCloud.Common;
using TencentCloud.Hunyuan.V20230901;
using TencentCloud.Hunyuan.V20230901.Models;
using TencentCloud.Ocr.V20181119;
using TencentCloud.Ocr.V20181119.Models;
using TencentCloud.Tts.V20190823;
using TencentCloud.Tts.V20190823.Models;
using Task = System.Threading.Tasks.Task;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.腾讯混元, "腾讯混元", "腾讯混元 是腾讯出品的大模型。支持图片问答。", 16, canProcessImage:true, canUseFunction:false, priceIn: 15, priceOut: 50)]
public class ApiTencent:ApiBase
{
    protected TencentClient _client;
    public ApiTencent(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<TencentClient>();
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
    /// 只能输入单个字符串，1024 token以内，返回向量长度1024
    /// </summary>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.混元Tools, "混元Tools", "腾讯混元Function call版，返回数组型参数有问题。", 17, canUseFunction:true,  priceIn: 4, priceOut: 8)]
public class ApiTencentTools : ApiTencent
{
    public ApiTencentTools(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModel("hunyuan-functioncall");
    }
}

[ApiClass(M.混元T1, "混元T1", "腾讯混元T1，腾讯推出的类R1推理模型。", 125, type: ApiClassTypeEnum.推理模型, canUseFunction:false,  priceIn: 4, priceOut: 8)]
public class ApiTencentT1 : ApiTencent
{
    public ApiTencentT1(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModel("hunyuan-t1-latest");
    }
}

/// <summary>
/// 腾讯混元大模型接口
/// 文档地址 https://cloud.tencent.com/document/product/1729/101837
/// </summary>
public class TencentClient:OpenAIClientBase, IApiClient
{
    protected IHttpClientFactory _httpClientFactory;
    protected IFunctionRepository _functionRepository;
    public TencentClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:Tencent:Key");
        AppId = configHelper.GetConfig<string>("Service:Tencent:AppId");
        SecretId = configHelper.GetConfig<string>("Service:Tencent:SecretId");
        SecretKey = configHelper.GetConfig<string>("Service:Tencent:SecretKey");
    }

    protected string AppId;
    protected string SecretId;
    protected string SecretKey;
    private string modelName = "hunyuan-turbos-latest";

    private string APIKEY; //OpenAI 兼容模式 APIKEY
    private string hostUrl = "https://api.hunyuan.cloud.tencent.com/v1/chat/completions";
    public void SetModel(string name)
    {
        modelName = name;
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? "hunyuan-vision" : modelName;
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var max_tokens = isImageMsg ? 1024 : 4096;
        if(modelName.Contains("t1"))
            max_tokens = 32000;
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            temperature = input.Temprature,
            tools = tools,
            stream,
            max_tokens =  max_tokens,
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "风格", Contents = new []
                {
                    new KeyValuePair<string, string>("写实", "xieshi"),
                    new KeyValuePair<string, string>("日漫", "riman"),
                    new KeyValuePair<string, string>("水墨", "shuimo"),
                    new KeyValuePair<string, string>("莫奈", "monai"),
                    new KeyValuePair<string, string>("素描", "<sketch>"),
                    new KeyValuePair<string, string>("插画", "bianping"),
                    new KeyValuePair<string, string>("绘本", "ertonghuiben"),
                    new KeyValuePair<string, string>("3D", "3dxuanran"),
                    new KeyValuePair<string, string>("漫画", "manhua"),
                    new KeyValuePair<string, string>("动漫", "dongman"),
                    new KeyValuePair<string, string>("毕加索", "bijiasuo"),
                    new KeyValuePair<string, string>("朋克", "saibopengke"),
                    new KeyValuePair<string, string>("油画", "youhua"),
                    new KeyValuePair<string, string>("剪纸", "xinnianjianzhi"),
                    new KeyValuePair<string, string>("青花瓷", "qinghuaci")
                }
            },
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1024:1024"),
                    new KeyValuePair<string, string>("横屏", "1280:768"),
                    new KeyValuePair<string, string>("竖屏", "768:1280")
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
    /// 画图
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> TextToImage(ApiChatInputIntern input)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        HunyuanClient _client = new HunyuanClient(cred, "ap-guangzhou");
        var req = new SubmitHunyuanImageJobRequest()
        {
            Prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            Style = input.ImageStyle,
            Resolution = input.ImageSize,
            LogoAdd = 0,
            Revise = 1
        };
        var resp = await _client.SubmitHunyuanImageJob(req);
        if (!string.IsNullOrEmpty(resp.JobId))
        {
            int times = 0;
            while (times<60)
            {
                times++;
                yield return Result.Waiting(times.ToString());
                var jobRes =
                    await _client.QueryHunyuanImageJob(new QueryHunyuanImageJobRequest() { JobId = resp.JobId });
                if (jobRes.JobStatusCode == "5") //1：等待中、2：运行中、4：处理失败、5：处理完成。
                {
                    if (jobRes.RevisedPrompt?.Length > 0)
                    {
                        yield return Result.Answer(jobRes.RevisedPrompt.First());
                    }

                    if (jobRes.ResultImage?.Length > 0)
                    {
                        var url = jobRes.ResultImage.First();
                        var http = _httpClientFactory.CreateClient();
                        var file = await http.GetByteArrayAsync(url);
                        yield return FileResult.Answer(file, "jpg", ResultType.ImageBytes, "image.jpg");
                    }
                    break;
                }else if (jobRes.JobStatusCode == "4")
                {
                    yield return Result.Error(jobRes.JobErrorMsg);
                    break;
                }
                Thread.Sleep(1000);
            }
        }
    }

    
    /// <summary>
    /// 腾讯云 文本转语音，支持长文本
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> TextToVoice(string text, string voiceName, string audioFormat)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        TtsClient client = new TtsClient(cred, "ap-shanghai");
        try
        {
            var resp = await client.TextToVoice(new TextToVoiceRequest()
            {
                Text = text,
                SessionId = DateTime.Now.Ticks.ToString(),
                VoiceType = int.Parse(voiceName.Split("_")[1]),
                ModelType = 1,
                Codec = "mp3"
            });
            if (!string.IsNullOrEmpty(resp.Audio))
            {
                var file = Convert.FromBase64String(resp.Audio);
                var format = "mp3";
                if (audioFormat != format)
                {
                    var random =  new Random().Next(100000, 999999).ToString();
                    file = AudioService.ConvertAudioFormat(file, format, audioFormat, random);
                    format = audioFormat;
                }
                return FileResult.Answer(file, audioFormat, ResultType.AudioBytes, duration:text.Length * 36 * 1000 / 173);//拿不到返回的音频时长，根据字数预估
            }
            else
            {
                return Result.Error("语音转换失败: " + resp.RequestId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return Result.Error("语音转换失败: " + ex.Message);
        }
    }
    
    public async Task<Result> LongTextToVoice(string text, string voiceName, string audioFormat)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };               
        TtsClient client = new TtsClient(cred, "ap-shanghai");
        try
        {
            var resp = await client.CreateTtsTask(new CreateTtsTaskRequest()
            {
                Text = text,
                VoiceType = int.Parse(voiceName.Split("_")[1]),
                ModelType = 1,
                Codec = "mp3"
            });
            if (!string.IsNullOrEmpty(resp.Data.TaskId))
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    var ts = await client.DescribeTtsTaskStatus(new DescribeTtsTaskStatusRequest()
                    {
                        TaskId = resp.Data.TaskId
                    });
                    if (ts.Data.Status == 2)
                    {
                        var _client = _httpClientFactory.CreateClient();
                        var file = await _client.GetByteArrayAsync(ts.Data.ResultUrl);
                        var format = "mp3";
                        if (audioFormat != format)
                        {
                            var random =  new Random().Next(100000, 999999).ToString();
                            file = AudioService.ConvertAudioFormat(file, format, audioFormat, random);
                            format = audioFormat;
                        }
                        return FileResult.Answer(file, audioFormat, ResultType.AudioBytes, duration:text.Length * 36 * 1000 / 173);//拿不到返回的音频时长，根据字数预估
                    }
                    else if (ts.Data.Status == 3)
                    {
                        return Result.Error("语音转换失败: " + resp.RequestId);
                    }
                    else if (ts.Data.Status == 0 || ts.Data.Status == 1)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return Result.Error("语音转换失败: " + resp.RequestId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return Result.Error("语音转换失败: " + ex.Message);
        }
    }
    
    /// <summary>
    /// 腾讯语音转文字
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> VoiceToText(byte[]  bytes, string fileName)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        try
        {
            AsrClient client = new AsrClient(cred, "ap-shanghai");
            var ext = "wav";
            if (fileName.EndsWith(".mp3"))
                ext = "mp3";
            else if (fileName.EndsWith(".m4a"))
                ext = "m4a";
            else if (fileName.EndsWith(".pcm"))
                ext = "pcm";
            else if (fileName.EndsWith(".opus")||fileName.EndsWith(".ogg"))
                ext = "ogg-opus";
            var resp = await client.SentenceRecognition(new SentenceRecognitionRequest()
            {
                EngSerViceType = "16k_zh-PY", SourceType = 1, VoiceFormat = ext,
                Data = Convert.ToBase64String(bytes), DataLen = bytes.Length
            });
            return Result.Answer(resp.Result);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return Result.Error(ex.Message);
        }
    }

    public async Task<(ResultType resultType, string[,]? result, string error)> OcrTableText(byte[] bytes)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        try
        {
            OcrClient client = new OcrClient(cred, "ap-shanghai");
            var resp = await client.RecognizeTableAccurateOCR(new RecognizeTableAccurateOCRRequest()
            {
                ImageBase64 = Convert.ToBase64String(ImageHelper.Compress(bytes))
            });
            if (resp.TableDetections != null && resp.TableDetections.Length > 0)
            {
                var table = resp.TableDetections[0];
                var rows = table.Cells.Max(t => t.RowBr) ?? 0;
                var cols = table.Cells.Max(t => t.ColBr) ?? 0;
                var values = new string[rows, cols];
                foreach (var cell in table.Cells)
                {
                    for (int i = (int)(cell.RowTl??0); i < cell.RowBr; i++)
                    {
                        for (int j = (int)(cell.ColTl??0); j < cell.ColBr; j++)
                        {
                            values[i, j] = cell.Text;
                        }
                    }
                }

                return (ResultType.Answer, values, string.Empty);
            }
            return (ResultType.Error, null, "没有识别到数据");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return (ResultType.Error, null, ex.Message);
        }
    }
    
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        HunyuanClient _client = new HunyuanClient(cred, "ap-guangzhou");
        var req = new GetEmbeddingRequest()
        {
            Input = qc.Last().Content
        };
        var resp = await _client.GetEmbedding(req);
        return (ResultType.Answer, resp.Data.Select(t=>t.Embedding.Select(x=>(double)x.Value).ToArray()).ToArray(), string.Empty);
    }

}

/// <summary>
/// 腾讯流式语音转文字扩展类
/// </summary>
public class TencentAudioStreamClient : TencentClient, IAiWebSocketProxy
{
    public TencentAudioStreamClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper) : base(httpClientFactory, functionRepository,  configHelper)
    {
    }

    public string GetWssVoiceToTextUrl()
    {
        var timestamp = GetUnixSeconds(DateTime.Now);
        var expired = GetUnixSeconds(DateTime.Now.AddDays(1));
        var nonce = Random.Shared.Next(100000, 1000000).ToString();
        var engine_model_type = "16k_zh_large"; //16k_zh_large大模型版是单独计费的，需要单独买资源包，16k_zh是普通版
        var voice_id = Guid.NewGuid().ToString("N");
        var voice_format = 1;
        var filter_modal = 1;
        var url =
            $"asr.cloud.tencent.com/asr/v2/{AppId}?engine_model_type={engine_model_type}&expired={expired}&filter_modal={filter_modal}&nonce={nonce}&secretid={SecretId}&timestamp={timestamp}&voice_format={voice_format}&voice_id={voice_id}";
        var sign = Base64UrlEncode(HmacSha1(url, SecretKey));
        return $"wss://{url}&signature={sign}";
    }

    private static long GetUnixSeconds(DateTime dt)
    {
        return new DateTimeOffset(dt).ToUnixTimeMilliseconds() / 1000;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return HttpUtility.UrlEncode(Convert.ToBase64String(bytes));
    }

    private static byte[] HmacSha1(string str, string key)
    {
        return HMACSHA1.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(str));
    }


    private BlockingCollection<Result>? _results;
    private ClientWebSocket ws;
    private List<Task> tasks = new List<Task>();
    bool addAnswerFinished = false;
    bool closeCallbackCalled = false;
    public async Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="")
    {
        _results = new BlockingCollection<Result>();
        var url = GetWssVoiceToTextUrl();
        ws = new ClientWebSocket();
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
                        if (o["code"].Value<int>() != 0)
                            _results.Add(Result.Error(o["message"].Value<string>()));
                        else
                        {
                            if (o["result"] != null && o["result"]["voice_text_str"] != null)
                                _results.Add(Result.Answer(o["result"]["voice_text_str"].Value<string>()));
                            if (o["final"] != null && o["final"].Value<int>() == 1)
                            {
                                //收到标记为最后一条消息
                                break;
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
            foreach (var data in messageQueue.GetConsumingEnumerable())
            {
                if (ws.State != WebSocketState.Open)
                    break;
                if (data is string s)
                {
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)), WebSocketMessageType.Text,
                        WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
                }
                else if (data is byte[] bytes)
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary,
                        WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
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
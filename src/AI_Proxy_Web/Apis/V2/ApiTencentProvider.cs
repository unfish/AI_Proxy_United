using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Apis.V2.Extra;
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

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("Tencent")]
public class ApiTencentProvider : ApiOpenAIProvider
{
    public ApiTencentProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    protected string AppId;
    protected string SecretId;
    protected string SecretKey;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        AppId = configHelper.GetProviderConfig<string>(attr.Provider, "AppId");
        SecretId = configHelper.GetProviderConfig<string>(attr.Provider, "SecretId");
        SecretKey = configHelper.GetProviderConfig<string>(attr.Provider, "SecretKey");
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
                    file = ApiAudioServiceProvider.ConvertAudioFormat(file, format, audioFormat, random);
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
                            file = ApiAudioServiceProvider.ConvertAudioFormat(file, format, audioFormat, random);
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
    
    public override async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
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

[ApiProvider("TencentImage")]
public class ApiTencentImageProvider : ApiTencentProvider
{
    public ApiTencentImageProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        extraOptionsList = new List<ExtraOption>()
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
    }

    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        await foreach (var resp in TextToImage(input))
        {
            yield return resp;
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
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
        var opts = GetExtraOptions(input.External_UserId);
        var req = new SubmitHunyuanImageJobRequest()
        {
            Prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            Style = opts[0].CurrentValue,
            Resolution = opts[1].CurrentValue,
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
}

[ApiProvider("Tencent3D")]
public class ApiTencent3DProvider : ApiTencentImageProvider
{
    public ApiTencent3DProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        await foreach (var resp in TextTo3D(input))
        {
            yield return resp;
        }
    }
    
    /// <summary>
    /// 文生3D
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> TextTo3D(ApiChatInputIntern input)
    {
        Credential cred = new Credential {
            SecretId = SecretId,
            SecretKey = SecretKey
        };
        HunyuanClient _client = new HunyuanClient(cred, "ap-guangzhou");
        SubmitHunyuanTo3DJobRequest req;
        var qc = input.ChatContexts.Contexts.Last().QC;
        if(qc.Any(t=>t.Type== ChatType.文本))
            req = new SubmitHunyuanTo3DJobRequest()
            {
                Prompt = qc.Last(t=>t.Type== ChatType.文本).Content,
            };
        else if (qc.Any(t => t.Type == ChatType.图片Base64))
            req = new SubmitHunyuanTo3DJobRequest()
            {
                ImageBase64 = qc.Last(t => t.Type == ChatType.图片Base64).Content,
            };
        else
        {
            yield return Result.Error("参数至少需要一个文本提示或一张图片");
            yield break;
        }
        var resp = await _client.SubmitHunyuanTo3DJob(req);
        if (!string.IsNullOrEmpty(resp.JobId))
        {
            int times = 0;
            while (times<120)
            {
                times++;
                yield return Result.Waiting(times.ToString());
                var jobRes =
                    await _client.QueryHunyuanTo3DJob(new QueryHunyuanTo3DJobRequest() { JobId = resp.JobId });
                if (jobRes.Status == "DONE") //1：等待中、2：运行中、4：处理失败、5：处理完成。
                {
                    if (jobRes.ResultFile3Ds?.Length > 0)
                    {
                        foreach (var file3D in jobRes.ResultFile3Ds)
                        {
                            foreach (var file in file3D.File3D)
                            {
                                var url = file.Url;
                                var type = file.Type;
                                Console.WriteLine(url);
                                var http = _httpClientFactory.CreateClient();
                                var bytes = await http.GetByteArrayAsync(url);
                                yield return FileResult.Answer(bytes, type == "GIF" ? "gif" : "zip",
                                    type == "GIF" ? ResultType.ImageBytes : ResultType.FileBytes,
                                    url.Substring(url.LastIndexOf("/") + 1));
                            }
                        }
                    }
                    break;
                }else if (jobRes.Status == "FAIL")
                {
                    yield return Result.Error(jobRes.ErrorMessage);
                    break;
                }
                Thread.Sleep(2000);
            }
        }
    }
}

/// <summary>
/// 腾讯流式语音转文字扩展类
/// </summary>
[ApiProvider("TencentSSR")]
public class ApiTencentSSRProvider : ApiTencentProvider, IAiWebSocketProxy
{
    public ApiTencentSSRProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }

    public string GetWssVoiceToTextUrl()
    {
        var timestamp = GetUnixSeconds(DateTime.Now);
        var expired = GetUnixSeconds(DateTime.Now.AddDays(1));
        var nonce = Random.Shared.Next(100000, 1000000).ToString();
        var engine_model_type = _modelName;
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
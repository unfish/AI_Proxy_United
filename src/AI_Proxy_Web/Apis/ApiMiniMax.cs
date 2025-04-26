using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.MiniMax, "MiniMax *", "强烈推荐：MiniMax 6.5s极速版 是WPS AI背后的大模型，专业类写作能力很强，创意类头脑风暴能力也不错，国产能力第一梯队，支持Function Call，支持图片问答。", 8,  canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 1, priceOut: 1)]
public class ApiMiniMax:ApiBase
{
    protected MiniMaxClient _client;
    public ApiMiniMax(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MiniMaxClient>();
    }
    
    /// <summary>
    /// 使用MiniMax来回答
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
    /// 使用MiniMax来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 可输入数组，总长度4000 token以内，返回向量长度1536
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.MiniMax大杯, "MiniMax *",
    "MiniMax-Text-01，最新版本，专业类写作能力很强，创意类头脑风暴能力也不错，国产能力第一梯队，支持Function Call最好的国产模型。支持100万上下文。", 9, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 1, priceOut: 8)]
public class ApiMiniMaxLarge : ApiMiniMax
{
    public ApiMiniMaxLarge(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModelName("MiniMax-Text-01");
    }
}


[ApiClass(M.MiniMax画图, "MiniMax画图", "MiniMax文生图模型。支持图生图，提供一张图片实现主体参考文生图功能。", 214, type: ApiClassTypeEnum.画图模型, canProcessImage:true, priceIn: 0, priceOut: 2)]
public class ApiMiniMaxImage:ApiBase
{
    protected MiniMaxClient _client;
    public ApiMiniMaxImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MiniMaxClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach (var resp in _client.TextToImage(input))
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
        return Result.Error("画图接口不支持Query调用");
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


[ApiClass(M.MiniMax视频, "MiniMax视频", "MiniMax视频生成模型，可以用文字描述来生成6秒视频，还可以上传一张包含人脸的照片作为主体参考来生成指定人物的视频。略贵，4.5元生成一次。\n文生视频时可以在提示中加入镜头控制指令，格式[动作1,动作2]，可用动作包括[左移, 右移, 左摇, 右摇, 推进, 拉远, 上升, 下降, 上摇, 下摇, 变焦推近, 变焦拉远, 晃动, 跟随, 固定]。", 221, type: ApiClassTypeEnum.视频模型, priceIn: 0, priceOut: 4.5)]
public class ApiMiniMaxVideo:ApiBase
{
    protected MiniMaxClient _client;
    public ApiMiniMaxVideo(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MiniMaxClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.TextToVideo(input))
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
        return Result.Error("视频接口不支持Query调用");
    }
    
}


/// <summary>
/// MiniMax大模型接口
/// 文档地址 https://api.minimax.chat/document/guides/chat-pro?id=64b79fa3e74cddc5215939f4
/// </summary>
public class MiniMaxClient: OpenAIClientBase, IApiClient
{
    private IFunctionRepository _functionRepository;
    private IHttpClientFactory _httpClientFactory;

    public MiniMaxClient(IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _functionRepository = functionRepository;
        _httpClientFactory = httpClientFactory;
        APIKEY = configHelper.GetConfig<string>("Service:MiniMax:Key");
        GroupId = configHelper.GetConfig<string>("Service:MiniMax:GroupId");
    }
    
    private static String hostUrl = "https://api.minimax.chat/v1/text/chatcompletion_v2";
    private string APIKEY;//从开放平台控制台中获取
    private string GroupId;
    private string modelName = "abab6.5s-chat";

    public void SetModelName(string name)
    {
        modelName = name;
    }
    
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    private string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var functions = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        var tools = functions == null
            ? null
            : functions.Select(t =>
            {
                var t1 = (FunctionToolParamter)t;
                return new
                {
                    type = t.type, function = new
                    {
                        name = t1.function.Name,
                        description = t1.function.Description,
                        parameters = JsonConvert.SerializeObject(t1.function.Parameters)
                    }
                };
            }).ToList();
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var model = modelName;
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            tools,
            temperature = input.Temprature,
            max_tokens = 4096,
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
        var url = hostUrl;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        _client.Timeout = TimeSpan.FromMinutes(5);
        var msg = GetMsgBody(input, true);
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        Console.WriteLine($"Trace-Id: {response.Headers.GetValues("Trace-Id").FirstOrDefault()}");
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
        var url = hostUrl;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        _client.Timeout = TimeSpan.FromMinutes(5);
        var msg = GetMsgBody(input, false);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
    
    public async IAsyncEnumerable<Result> TextToImage(ApiChatInputIntern input)
    {
        var url = "https://api.minimax.chat/v1/image_generation";
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var prompt = input.ChatContexts.Contexts.Last().QC.Last(t => t.Type == ChatType.文本).Content;
        var img = input.ChatContexts.Contexts.Last().QC.LastOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var imgRef = img == null
            ? null
            : new
            {
                type = "character", image_file = "data:image/jpeg;base64," + img
            };
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            model = "image-01",
            prompt = prompt,
            subject_reference = imgRef,
            response_format = "base64",
            n = 1,
            prompt_optimizer = true,
            aspect_ratio = GetExtraOptions(input.External_UserId)[0].CurrentValue
        }, jSetting);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(content);
        if (o["base_resp"]["status_msg"].Value<string>() == "success")
        {
            var arr = o["data"]["image_base64"].Values<string>();
            foreach (var b64 in arr)
            {
                yield return FileResult.Answer(Convert.FromBase64String(b64), "png", ResultType.ImageBytes);
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1:1"),
                    new KeyValuePair<string, string>("横屏", "4:3"),
                    new KeyValuePair<string, string>("竖屏", "3:4"),
                    new KeyValuePair<string, string>("长横屏", "16:9"),
                    new KeyValuePair<string, string>("长竖屏", "9:16"),
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
        var url = "https://api.minimax.chat/v1/video_generation";
        var prompt = input.ChatContexts.Contexts.Last().QC.First(t => t.Type == ChatType.文本).Content;
        var image = input.ChatContexts.Contexts.Last().QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            model="T2V-01-Director",
            prompt = prompt
        }, jSetting);
        if (!string.IsNullOrEmpty(image))
        {
            msg = JsonConvert.SerializeObject(new
            {
                model = "S2V-01",
                prompt = prompt,
                subject_reference = new[]
                {
                    new { type = "character", image = new[] { "data:image/jpeg;base64," + image } }
                }
            }, jSetting);
        }

        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var result = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(result);
        if (o["task_id"] != null)
        {
            var taskId = o["task_id"].Value<string>();
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
        var url = "https://api.minimax.chat/v1/query/video_generation?task_id="+taskid;
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
                if (state == "Queueing" || state == "Processing" || state == "Preparing")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "Success")
                {
                    yield return Result.Waiting("生成完成，下载视频...");
                    var file_id = json["file_id"].Value<string>();
                    var video_url = "https://api.minimax.chat/v1/files/retrieve?file_id=" + file_id;
                    resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, video_url));
                    content = await resp.Content.ReadAsStringAsync();
                    json = JObject.Parse(content);
                    if (json["file"]["download_url"] != null)
                    {
                        var bytes = await client.GetByteArrayAsync(json["file"]["download_url"].Value<string>());
                        yield return VideoFileResult.Answer(bytes, "mp4", "video.mp4");
                    }
                    else
                    {
                        yield return Result.Error(content);
                    }
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
    
    
    private string MiniMaxGetTTSMsg(string text, string voiceName, string audioFormat, bool stream)
    {
        var voice = "male-qn-jingying-jingpin";
        if (!string.IsNullOrEmpty(voiceName) && voiceName.StartsWith("minimax_"))
            voice = voiceName.Replace("minimax_", "");
        var formats = new[] { "mp3", "wav", "pcm", "flac" };
        return JsonConvert.SerializeObject(new
        {
            model = "speech-02-hd",
            text = text,
            voice_setting = new
            {
                voice_id = voice,
            },
            audio_setting = new
            {
                sample_rate = 16000,
                bitrate = 32000,
                format = !string.IsNullOrEmpty(audioFormat) && formats.Contains(audioFormat) ? audioFormat : "mp3"
            },
            stream = stream
        });
    }
        
    private static String ttsHostUrl = "https://api.minimax.chat/v1/t2a_v2?GroupId=";
   
    /// <summary>
    /// MiniMax 文本转语音，支持长文本
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> TextToVoice(string text, string voiceName, string audioFormat)
    {
        var url = ttsHostUrl+GroupId;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = MiniMaxGetTTSMsg(text, voiceName, audioFormat, false);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["base_resp"] != null && json["base_resp"]["status_code"].Value<int>() == 0)
        {
            if (json["data"] is not null && json["data"]["audio"] is not null)
            {
                var audio = json["data"]["audio"].Value<string>();
                if (!string.IsNullOrEmpty(audio))
                {
                    var bytes = StringToByteArray(audio);
                    if (bytes.Length > 0)
                    {
                        var format = "mp3";
                        if (audioFormat != format)
                        {
                            var random = new Random().Next(100000, 999999).ToString();
                            bytes = AudioService.ConvertAudioFormat(bytes, format, audioFormat, random);
                        }
                        return FileResult.Answer(bytes, audioFormat, ResultType.AudioBytes, duration:json["extra_info"]["audio_length"].Value<int>());
                    }
                }
            }
        }
        return Result.Error(content);
    }
    
    /// <summary>
    /// MiniMax 文本转语音，流式返回语音片段
    /// </summary>
    /// <param name="content"></param>
    /// <param name="voice"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input)
    {
        var content = input.ChatContexts.Contexts.Last().QC.Last().Content;
        //该接口每次最大输入文字有500字限制，需要自己进行截取，分段转语音
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
                if (sb.Length + s.Length >= 300)
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
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = MiniMaxGetTTSMsg(input.ChatContexts.Contexts.Last().QC.Last().Content, input.AudioVoice, input.AudioFormat, true);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, ttsHostUrl+GroupId)
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
                    if (o["data"] != null && o["extra_info"] == null && o["data"]["audio"] != null)
                    {
                        var audio = o["data"]["audio"].Value<string>();
                        if (!string.IsNullOrEmpty(audio))
                        {
                            var bytes = StringToByteArray(audio);
                            if (bytes.Length > 0)
                                yield return FileResult.Answer(bytes, "mp3", ResultType.AudioBytes);
                        }
                    }
                }
            }
        }
    }
    private static byte[] StringToByteArray(string hex) {
        if (hex.Length % 2 == 1)
            throw new Exception("The binary key cannot have an odd number of digits");
        byte[] arr = new byte[hex.Length >> 1];
        for (int i = 0; i < hex.Length >> 1; ++i)
        {
            arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
        }
        return arr;
    }
    private static int GetHexVal(char hex) {
        int val = (int)hex;
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
    }
    
    private static String embedUrl = "https://api.minimax.chat/v1/embeddings?GroupId=";
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            texts = embeddings,
            model = "embo-01",
            type=embedForQuery?"query":"db"
        });
    }
    private class EmbeddingsResponse
    {
        public double[][] vectors { get; set; }
    }
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, embedUrl+GroupId)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.vectors, string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
    
}
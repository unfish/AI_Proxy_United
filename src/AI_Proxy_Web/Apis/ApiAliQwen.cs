using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.阿里通义, "阿里通义", "阿里通义千问 2.5 Max，号称中文能力超过GPT 4 Turbo。支持图文问答，识图能力极强，支持function call。", 10, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 20, priceOut: 60)]
public class ApiAliQwen:ApiBase
{
    protected AliQwenClient _client;
    public ApiAliQwen(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<AliQwenClient>();
    }
    
    /// <summary>
    /// 使用通义千问来回答
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
    /// 使用通义千问来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 可输入数组，每个字符串长度2000字以内，返回向量长度1536
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.通义小杯, "通义小杯", "阿里通义千问 2.5 Plus，号称中文能力超过GPT 4 Turbo。支持图文问答，识图能力极强，支持function call，低价格高速度。", 11,  canProcessImage: true,
    canProcessMultiImages: true, canUseFunction: true, priceIn: 0.8, priceOut: 2)]
public class ApiAliQwenPlus : ApiAliQwen
{
    public ApiAliQwenPlus(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModel("qwen-plus-latest");
    }
}

[ApiClass(M.Ali_DeepSeekR1, "DS R1阿里版", "DeepSeek R1阿里云备用接口。", 118, type: ApiClassTypeEnum.推理模型, canProcessImage: false, priceIn: 4, priceOut: 16)]
public class ApiAliDeepSeekR1 : ApiAliQwen
{
    public ApiAliDeepSeekR1(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModel("deepseek-r1");
    }
}

/// <summary>
/// 通义千问大模型接口
/// 文档地址 https://help.aliyun.com/zh/dashscope/developer-reference/api-details
/// </summary>
public class AliQwenClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    private IServiceProvider _serviceProvider;
    public AliQwenClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper, IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        _serviceProvider = serviceProvider;
        APIKEY = configHelper.GetConfig<string>("Service:Qwen:Key");
    }

    private static String hostUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"; //openai兼容接口
    private static String embedUrl = "https://dashscope.aliyuncs.com/api/v1/services/embeddings/text-embedding/text-embedding";
    private static String imgEmbedUrl = "https://dashscope.aliyuncs.com/api/v1/services/embeddings/multimodal-embedding/multimodal-embedding";
    private string APIKEY;//从开放平台控制台中获取
    private string modelName = "qwen-max-latest";

    public void SetModel(string name)
    {
        modelName = name;
    }
    
    #region 通义千问
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream"></param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? "qwen-vl-max-latest" : modelName;
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
            max_tokens =  isImageMsg ? 2000: 4096,
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
        var url = hostUrl;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
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
        var url = hostUrl;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
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
            model = "text-embedding-v2",
            input = new { texts = embeddings },
            parameters = new
            {
                text_type = embedForQuery ? "query" : "document"
            }
        });
    }
    private class EmbeddingsResponse
    {
        public EmbeddingsOutput Output { get; set; }
    } 
    private class EmbeddingsOutput
    {
        public EmbeddingObject[] Embeddings { get; set; }
    }
    private class EmbeddingObject
    {
        public double[] Embedding { get; set; }
        public int text_index { get; set; }
    }
    
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = embedUrl;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.Output.Embeddings.Select(t => t.Embedding).ToArray(), string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
    
    
    private string GetImageEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        //传进来的上下文第一条的Q必须是图片的URL
        return JsonConvert.SerializeObject(new
        {
            model = "multimodal-embedding-one-peace-v1",
            input = new { contents=new[]{new{image=qc.First().Content}} },
            parameters = new
            {
                auto_truncation = true
            }
        });
    } 
    private class ImageEmbeddingsResponse
    {
        public EmbeddingObject Output { get; set; }
    } 
    public async Task<(ResultType resultType, double[][]? result, string error)> ImageEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = imgEmbedUrl;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var msg = GetImageEmbeddingsMsgBody(qc,  embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<ImageEmbeddingsResponse>(content);
            return (ResultType.Answer, new[] { result.Output.Embedding }, string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
    
    #endregion

    #region 通义万相

    //通义万相画图接口
    private static String wanxiangHostUrl =
        "https://dashscope.aliyuncs.com/api/v1/services/aigc/text2image/image-synthesis"; //万相画图
    private static String wanxiangVideoHostUrl =
        "https://dashscope.aliyuncs.com/api/v1/services/aigc/video-generation/video-synthesis"; //万相文生视频
    
    //万相查询任务状态接口
    private static String wanxiangTaskUrl = "https://dashscope.aliyuncs.com/api/v1/tasks/";
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1440*1440"),
                    new KeyValuePair<string, string>("横屏", "1440*1024"),
                    new KeyValuePair<string, string>("竖屏", "1024*1440")
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

    public List<ExtraOption> GetPosterExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "风格", Contents = new []
                {
                    new KeyValuePair<string, string>("剪纸工艺", "剪纸工艺"),
                    new KeyValuePair<string, string>("折纸工艺", "折纸工艺"),
                    new KeyValuePair<string, string>("中国水墨", "中国水墨"),
                    new KeyValuePair<string, string>("中国刺绣", "中国刺绣"),
                    new KeyValuePair<string, string>("浩瀚星云", "浩瀚星云"),
                    new KeyValuePair<string, string>("浓郁色彩", "浓郁色彩"),
                    new KeyValuePair<string, string>("光线粒子", "光线粒子"),
                    new KeyValuePair<string, string>("透明玻璃", "透明玻璃"),
                    new KeyValuePair<string, string>("真实场景", "真实场景"),
                    new KeyValuePair<string, string>("2D插画", "2D插画2"),
                    new KeyValuePair<string, string>("2D卡通", "2D卡通"),
                    new KeyValuePair<string, string>("儿童水彩", "儿童水彩"),
                    new KeyValuePair<string, string>("赛博背景", "赛博背景"),
                    new KeyValuePair<string, string>("浅蓝抽象", "浅蓝抽象"),
                    new KeyValuePair<string, string>("深蓝抽象", "深蓝抽象"),
                    new KeyValuePair<string, string>("抽象点线", "抽象点线"),
                    new KeyValuePair<string, string>("童话油画", "童话油画")
                }
            },
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("竖版", "竖版"),
                    new KeyValuePair<string, string>("横版", "横版")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_WanxPoster_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetPosterExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_WanxPoster_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
    
    
    public List<ExtraOption> GetT2VExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("竖版", "720*1280"),
                    new KeyValuePair<string, string>("横版", "1280*720")
                }
            },
            new ExtraOption()
            {
                Type = "质量", Contents = new []
                {
                    new KeyValuePair<string, string>("高(3.5元/次)", "wanx2.1-t2v-plus"),
                    new KeyValuePair<string, string>("中(1.2元/次)", "wanx2.1-t2v-turbo")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_WanxT2V_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetT2VExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_WanxT2V_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
    
    public string GetWanXiangMsgBody(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC;
        var body = new
        {
            model = "wanx2.1-t2i-plus",
            input = new
            {
                prompt = qc.FirstOrDefault(t=>t.Type== ChatType.文本)?.Content
            },
            parameters = new
            {
                size = input.ImageSize, n = 1
            }
        };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(body, jSetting);
    }
    
    public string GetPosterMsgBody(ApiChatInputIntern input)
    {
        var content = input.ChatContexts.Contexts.Last().QC.Last(t => t.Type == ChatType.文本).Content;
        var ps = GetPosterExtraOptions(input.External_UserId);
        var ss = content.Split("\n");
        var body = new
        {
            model = "wanx-poster-generation-v1",
            input = new
            {
                prompt_text_zh = ss[0],
                title = ss[1],
                sub_title = ss.Length>2 ? ss[2] : null,
                body_text = ss.Length>3 ? ss[3] : null,
                wh_ratios = ps[1].CurrentValue,
                lora_name = ps[0].CurrentValue,
                generate_mode = "generate",
                generate_num = 1
            },
            parameters = new
            {
            }
        };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(body, jSetting);
    }
    
    public string GetPosterEnlargeMsgBody(ApiChatInputIntern input)
    {
        var content = input.ChatContexts.Contexts.Last().QC.Last(t => t.Type == ChatType.阿里万相扩展参数).Content;
        var body = new
        {
            model = "wanx-poster-generation-v1",
            input = new
            {
                auxiliary_parameters =content,
                title = "A", //假的，没用
                generate_mode = "sr"
            },
            parameters = new
            {
            }
        };
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(body, jSetting);
    }

    public string GetWanXiangT2VMsgBody(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC;
        var jSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        var ps = GetT2VExtraOptions(input.External_UserId);
        if (qc.Any(t => t.Type == ChatType.图片Base64))
        {
            var q = qc.First(t => t.Type == ChatType.图片Base64);
            var fileService = _serviceProvider.GetRequiredService<IOssFileService>();
            var file = fileService.UploadFile("images.jpg", new MemoryStream(Convert.FromBase64String(q.Content)), input.UserId);
            var ossPath = fileService.GetFileFullUrl(file.FilePath);
            q.Content = ossPath;
            q.Type = ChatType.图片Url;
        }
        if (qc.FirstOrDefault(t => t.Type == ChatType.图片Url) != null)
        {
            var body = new
            {
                model = "wanx2.1-i2v-plus",
                input = new
                {
                    prompt = qc.FirstOrDefault(t => t.Type == ChatType.文本)?.Content,
                    img_url = qc.FirstOrDefault(t => t.Type == ChatType.图片Url)?.Content
                },
                parameters = new
                {
                    size = ps[0].CurrentValue, duration = 5
                }
            };
            return JsonConvert.SerializeObject(body, jSetting);
        }
        else
        {
            var body = new
            {
                model = ps[1].CurrentValue,
                input = new
                {
                    prompt = qc.FirstOrDefault(t => t.Type == ChatType.文本)?.Content
                },
                parameters = new
                {
                    size = ps[0].CurrentValue, duration = 5
                }
            };

            return JsonConvert.SerializeObject(body, jSetting);
        }
    }

    public enum WanXiangDrawImageType
    {
        Default, //默认文生图
        Poster, //海报
        PosterEnlarge, //海报放大
        T2V, //文生视频
    }
    
    public async IAsyncEnumerable<Result> CreateWanXiangImage(ApiChatInputIntern input, WanXiangDrawImageType type)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        client.DefaultRequestHeaders.Add("X-DashScope-Async", "enable");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var url = wanxiangHostUrl;
        var msg = String.Empty;
        switch (type)
        {
            case WanXiangDrawImageType.Poster:
                msg = GetPosterMsgBody(input);
                break;
            case WanXiangDrawImageType.PosterEnlarge:
                msg = GetPosterEnlargeMsgBody(input);
                break;
            case WanXiangDrawImageType.T2V:
                msg = GetWanXiangT2VMsgBody(input);
                url = wanxiangHostUrl;
                break;
            default:
                msg = GetWanXiangMsgBody(input);
                break;
        }
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["output"] != null && json["output"]["task_id"] != null)
        {
            var taskid = json["output"]["task_id"].Value<string>();
            await foreach (var res in CheckWanXiangTask(taskid, type))
                yield return res;
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    private async IAsyncEnumerable<Result> CheckWanXiangTask(string taskid, WanXiangDrawImageType type)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = wanxiangTaskUrl+taskid;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["output"] != null && json["output"]["task_id"] != null)
            {
                var state = json["output"]["task_status"].Value<string>();
                if (state == "RUNNING"||state == "PENDING")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "SUCCEEDED")
                {
                    yield return Result.Waiting("画图完成，下载图片...");
                    if (type== WanXiangDrawImageType.Poster|| type== WanXiangDrawImageType.PosterEnlarge)
                    {
                        var urls = json["output"]["render_urls"].Values<string>();
                        var bg_urls = json["output"]["bg_urls"].Values<string>();
                        var auxs = json["output"]["auxiliary_parameters"].Values<string>().ToArray();
                        var index = 0;
                        foreach (var t in bg_urls)
                        {
                            var bytes = await client.GetByteArrayAsync(t);
                            yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                        }
                        foreach (var t in urls)
                        {
                            var bytes = await client.GetByteArrayAsync(t);
                            yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                            if(type== WanXiangDrawImageType.Poster)
                                yield return Result.New(ResultType.AliWanXiangAuxiliary, auxs[index]);
                            index++;
                        }
                    }
                    else if (type == WanXiangDrawImageType.T2V)
                    {
                        var videoUrl = json["output"]["video_url"].Value<string>();
                        var bytes = await client.GetByteArrayAsync(videoUrl);
                        yield return VideoFileResult.Answer(bytes, "mp4", duration: 5000);
                    }
                    else
                    {
                        var arr = json["output"]["results"] as JArray;
                        foreach (var t in arr)
                        {
                            if (t["url"] != null)
                            {
                                var imageUrl = t["url"].Value<string>();
                                var bytes = await client.GetByteArrayAsync(imageUrl);
                                yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                            }
                            else
                                yield return Result.Error(t["code"].Value<string>());
                        }
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
    
    #endregion
}
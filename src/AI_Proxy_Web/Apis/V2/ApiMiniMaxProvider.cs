using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Apis.V2.Extra;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("MiniMax")]
public class ApiMiniMaxProvider : ApiOpenAIProvider
{
    public ApiMiniMaxProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    private string _groupId = string.Empty;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "text/chatcompletion_v2";
        _groupId = configHelper.GetProviderConfig<string>(attr.Provider, "GroupId");
    }
    
    
    private string MiniMaxGetTTSMsg(string text, string voiceName, string audioFormat, bool stream)
    {
        var voice = "male-qn-jingying-jingpin";
        if (!string.IsNullOrEmpty(voiceName) && voiceName.StartsWith("minimax_"))
            voice = voiceName.Replace("minimax_", "");
        var formats = new[] { "mp3", "wav", "pcm", "flac" };
        return JsonConvert.SerializeObject(new
        {
            model = "speech-2.5-hd-preview",
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
        var url = ttsHostUrl + _groupId;
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
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
                            bytes = ApiAudioServiceProvider.ConvertAudioFormat(bytes, format, audioFormat, random);
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
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        var msg = MiniMaxGetTTSMsg(input.ChatContexts.Contexts.Last().QC.Last().Content, input.AudioVoice, input.AudioFormat, true);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, ttsHostUrl+_groupId)
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
    public override async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, embedUrl+_groupId)
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

[ApiProvider("MiniMaxImage")]
public class ApiMiniMaxImageProvider : ApiMiniMaxProvider
{
    public ApiMiniMaxImageProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
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
                    new KeyValuePair<string, string>("方形", "1:1"),
                    new KeyValuePair<string, string>("横屏", "4:3"),
                    new KeyValuePair<string, string>("竖屏", "3:4"),
                    new KeyValuePair<string, string>("长横屏", "16:9"),
                    new KeyValuePair<string, string>("长竖屏", "9:16"),
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
    public async IAsyncEnumerable<Result> TextToImage(ApiChatInputIntern input)
    {
        var url = "https://api.minimax.chat/v1/image_generation";
        var _client = _httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
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
            model = _modelName,
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
}
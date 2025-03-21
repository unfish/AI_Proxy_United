using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Web;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using FFMpegCore;
using Hangfire;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
#pragma warning disable 8604, 1998, 8602
namespace AI_Proxy_Web.Feishu;

public enum FeishuMessageType
{
    Text = 0,
    Interactive = 1,
    Image = 2,
    File = 3,
    Audio = 4,
    PlainText = 5,
    Media = 6, //视频
    System = 7, //系统类型，目前只支持分隔线
    Divider = 8,  //分隔线
    CardId = 9,  //分隔线
}

public interface IBaseFeishuService
{
    (bool success, string message) GetUserAccessTokenByCode(string code);
    Result VoiceToText(byte[]  bytes, string fileName);
    
    Task<JObject> ProcessEventCallback(string event_type, JObject obj);

    Task ExportContextToPdf(int chatModel, string user_id, string sessionId, int logId);
}
public abstract class BaseFeishuService: IBaseFeishuService
{
    protected string AppId = "";
    protected string AppSecret = "";
    protected string TokenCacheKey = "";
    public bool UsePartialMessage = true;

    private const string DomainUrl = "https://open.feishu.cn/";
    private const string TokenUrl = "open-apis/auth/v3/tenant_access_token/internal/";
    private const string SendMessageUrl = "open-apis/im/v1/messages?receive_id_type=user_id";
    private const string UpdateMessageUrl = "open-apis/im/v1/messages/";
    private const string FileRecognizeUrl = "open-apis/speech_to_text/v1/speech/file_recognize";

    protected string contextCachePrefix = "fs"; //子类需要修改这个值，隔离同一个用户在多个飞书机器人中的上下文
    private IFeishuRestClient _restClient;
    private ILogRepository _logRepository;
    private IHttpClientFactory _httpClientFactory;
    private ConfigHelper _configHelper;
    protected string SiteHost;
    private static object lockObj = new object();
    public BaseFeishuService(IFeishuRestClient restClient, ILogRepository logRepository, ConfigHelper configHelper,
        IHttpClientFactory httpClientFactory)
    {
        _restClient = restClient;
        _logRepository = logRepository;
        _httpClientFactory = httpClientFactory;
        _configHelper = configHelper;
        SiteHost = configHelper.GetConfig<string>("Site:Host");
    }
    
    /// <summary>
    /// 用来保存本次对象中使用的token，避免多次调用接口时重复取redis
    /// </summary>
    private string currentToken;
    
    /// <summary>
    /// 获取飞书请求token
    /// </summary>
    protected string GetToken()
    {
        if (!string.IsNullOrEmpty(currentToken))
            return currentToken;
        var cacheKey =  TokenCacheKey;
        var token = CacheService.Get<string>(cacheKey);
        if (!string.IsNullOrWhiteSpace(token)) return token;
            
        var request = new RestRequest(TokenUrl, Method.Post);
            
        var appId =  AppId;
        var appSecret =  AppSecret;
        request.AddJsonBody(new { app_id = appId, app_secret = appSecret });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o.Value<int>("code") == 0)
        {
            token = o.Value<string>("tenant_access_token");
            var expire = o.Value<int>("expire");
            CacheService.Save(cacheKey, token, Math.Max(expire - 100, 10));
        }
        else
        {
            Console.WriteLine($"FeiShuGptApiToken Error: {response.Content}");
        }

        currentToken = token;
        return token;
    }
    
    /// <summary>
    /// 通用方法，发送文本消息
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="textOrContent"></param>
    /// <param name="type"></param>
    /// <param name="roolUp"></param>
    /// <returns></returns>
    public string SendMessage(string user_id, string textOrContent, FeishuMessageType type= FeishuMessageType.Text, bool roolUp = true)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return String.Empty;
        var request = new RestRequest(SendMessageUrl, Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        string content = textOrContent;
        switch (type)
        {
            case FeishuMessageType.Text:
                content = GetTextCardMessage(textOrContent);
                type = FeishuMessageType.Interactive;
                break;
            case FeishuMessageType.CardId:
                content = JsonConvert.SerializeObject(new {type = "card", data = new {card_id = textOrContent}});
                type = FeishuMessageType.Interactive;
                break;
            case FeishuMessageType.Image:
                content = JsonConvert.SerializeObject(new { image_key = textOrContent });
                break;
            case FeishuMessageType.File:
            case FeishuMessageType.Audio:
                content = JsonConvert.SerializeObject(new { file_key = textOrContent });
                break;
            case FeishuMessageType.PlainText:
                type = FeishuMessageType.Text;
                content = JsonConvert.SerializeObject(new { text = textOrContent });
                break;
            case FeishuMessageType.System:
            case FeishuMessageType.Divider:
                type = FeishuMessageType.System;
                content = JsonConvert.SerializeObject(new
                {
                    type = "divider", __params = new { divider_text = new { text = textOrContent } },
                    options = new { need_rollup = roolUp } //自动滚动清屏
                }).Replace("__params", "params");
                break;
        }
        request.AddJsonBody(new
        {
            content,
            msg_type = type.ToString().ToLower(),
            receive_id = user_id
        });

        try
        {
            var response = _restClient.GetClient().Execute(request, Method.Post);
            var responseContent = response.Content;
            var o = JObject.Parse(responseContent);
            if (o.Value<int>("code") == 0)
            {
                var msg_id = o["data"]["message_id"].Value<string>();
                return msg_id;
            }
            else
            {
                Console.WriteLine(responseContent);
                if (type == FeishuMessageType.Interactive) //如果是卡片消息发送错误，把错误信息发给用户
                {
                    SendMessage(user_id, responseContent, FeishuMessageType.PlainText);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        return String.Empty;
    }

    /// <summary>
    /// 发送消息跟随气泡消息。用户收到的是指定的消息后面的几个提示气泡，点击其中一个会将其内容作为用户输入发送过来。
    /// 如果用户点击气泡或收到新的消息，这些气泡会自动消失。主要用于自动提示用户可以提问的问题。
    /// </summary>
    /// <param name="message_id"></param>
    /// <param name="texts"></param>
    /// <returns></returns>
    public bool SendFollowUpMessage(string message_id, string[] texts)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return false;
        var request = new RestRequest(DomainUrl+$"open-apis/im/v1/messages/{message_id}/push_follow_up", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            follow_ups = texts.Select(t => new { content = t }).ToArray()
        });

        try
        {
            var response = _restClient.GetClient().Execute(request, Method.Post);
            var responseContent = response.Content;
            var o = JObject.Parse(responseContent);
            if (o.Value<int>("code") == 0)
            {
                return true;
            }
            else
            {
                Console.WriteLine(responseContent);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        return false;
    }
    
    private string GetTextCardMessage(string text)
    {
        return JsonConvert.SerializeObject(new
        {
            schema = "2.0",
            config = new {width_mode = "fill"},
            body = new{
                elements = new[]
                {
                    new {tag = "markdown", content = text}
                }
            }
        });
    }

    /// <summary>
    /// 下载消息中的文件和图片
    /// </summary>
    /// <param name="msg_id"></param>
    /// <param name="file_key"></param>
    /// <param name="file_type"></param>
    /// <returns></returns>
    public async Task<byte[]> DownloadFile(string msg_id, string file_key, string file_type="file")
    {
        var token = GetToken();
        var fileUrl = $"{DomainUrl}open-apis/im/v1/messages/{msg_id}/resources/{file_key}?type={file_type}";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        return await client.GetByteArrayAsync(fileUrl);
    }
    
    public async Task<string> GetMessageContent(string msg_id)
    {
        var token = GetToken();
        var fileUrl = $"{DomainUrl}open-apis/im/v1/messages/{msg_id}";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        var result = await client.GetStringAsync(fileUrl);
        var o = JObject.Parse(result);
        var sb = new StringBuilder();
        if (o["code"].Value<int>() == 0)
        {
            var items = o["data"]["items"] as JArray;
            foreach (var item in items)
            {
                var type = item["msg_type"].Value<string>();
                if (type == "text")
                {
                    var con = JObject.Parse(item["body"]["content"].Value<string>());
                    sb.Append(con["text"].Value<string>() + "\n");
                }else if (type == "interactive")
                {
                    var con = JObject.Parse(item["body"]["content"].Value<string>());
                    JArray elements = null;
                    if(con["elements"]!=null)
                        elements = con["elements"] as JArray;
                    if(elements==null)
                        elements = con["i18n_elements"]["zh_cn"] as JArray;
                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            if (element is JArray)
                            {
                                foreach (var tk in element as JArray)
                                {
                                    sb.Append(GetStringFromToken(tk));
                                }
                            }
                            else
                            {
                                sb.Append(GetStringFromToken(element));
                            }
                        }
                    }
                }
            }
        }

        return sb.ToString();
    }

    private string GetStringFromToken(JToken tk)
    {
        var tag = tk["tag"].Value<string>();
        if (tag == "text" || tag == "a")
            return tk["text"].Value<string>();
        if (tag == "markdown")
            return tk["content"].Value<string>();
        return string.Empty;
    }
    
    /// <summary>
    /// 通用方法，更新文本消息
    /// </summary>
    /// <param name="message_id"></param>
    /// <param name="text"></param>
    public void UpdateText(string message_id, string text, FeishuMessageType type= FeishuMessageType.Text)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return;
        var request = new RestRequest(UpdateMessageUrl + message_id, Method.Patch);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        //var content = JsonConvert.SerializeObject(new { text = text });
        request.AddJsonBody(new
        {
            content = type == FeishuMessageType.Text ? GetTextCardMessage(text) : text
        });
        var response = _restClient.GetClient().Execute(request, Method.Patch);
        var o = JObject.Parse(response.Content);
        if (o.Value<int>("code") != 0)
        {
            Console.WriteLine(response.Content);
        }
    }
    
    /// <summary>
    /// 如果已经有send_msg_id则更新，否则发送一条新消息并返回send_msg_id
    /// </summary>
    /// <param name="send_msg_id"></param>
    /// <param name="user_id"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    public string SendOrUpdateMessage(string send_msg_id, string user_id, string text, FeishuMessageType type= FeishuMessageType.Text)
    {
        if (string.IsNullOrEmpty(send_msg_id))
            send_msg_id = SendMessage(user_id, text, type);
        else
            UpdateText(send_msg_id, text, type);
        return send_msg_id;
    }
    
    /// <summary>
    /// 推送完整的消息，并决定是否清空sb内容
    /// </summary>
    /// <param name="send_msg_id"></param>
    /// <param name="user_id"></param>
    /// <param name="sb"></param>
    /// <param name="clearContent"></param>
    /// <returns>清空msg_id</returns>
    protected string FlushMessage(string send_msg_id, string user_id, StringBuilder sb, bool clearContent = true)
    {
        if (sb.Length > 0)
        {
            SendOrUpdateMessage(send_msg_id, user_id, sb.ToString());
            if(clearContent)
                sb.Clear();
        }
        return string.Empty;
    }
    
    public string CreatePartialCardMessage()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return String.Empty;
        var request = new RestRequest($"{DomainUrl}open-apis/cardkit/v1/cards", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            type="card_json",
            data = @"{
  ""schema"": ""2.0"",
  ""config"": {
    ""streaming_mode"": true,
    ""width_mode"": ""fill"",
    ""summary"": {
      ""content"": ""[思考中]""
    },
    ""streaming_config"": {
      ""print_frequency_ms"": {
        ""default"": 25
      },
      ""print_step"": {
        ""default"": 1
      },
      ""print_strategy"": ""fast""
    }
  },
  ""body"": {
    ""elements"": [
      {
        ""tag"": ""markdown"",
        ""content"": """",
        ""element_id"": ""markdown_1""
      },
      {
        ""tag"": ""markdown"",
        ""content"": ""<text_tag color='green'>思考中</text_tag>"",
        ""element_id"": ""markdown_2""
      }
    ]
  }
}"
        });

        try
        {
            var response = _restClient.GetClient().Execute(request, Method.Post);
            var responseContent = response.Content;
            var o = JObject.Parse(responseContent);
            if (o.Value<int>("code") == 0)
            {
                var card_id = o["data"]["card_id"].Value<string>();
                return card_id;
            }
            else
            {
                Console.WriteLine(responseContent);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        return String.Empty;
    }

    public void UpdatePartialCardText(string card_id, string text, int sequence)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return;
        var request = new RestRequest($"{DomainUrl}open-apis/cardkit/v1/cards/{card_id}/elements/markdown_1/content", Method.Put);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            content = text,
            sequence = sequence
        });
        var response = _restClient.GetClient().Execute(request, Method.Put);
        var o = JObject.Parse(response.Content);
        if (o.Value<int>("code") != 0)
        {
            Console.WriteLine(response.Content);
        }
    }
    public void FinishPartialCardText(string card_id, string text, int sequence)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return;
        var request = new RestRequest($"{DomainUrl}open-apis/cardkit/v1/cards/{card_id}", Method.Put);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            card = new {type = "card_json", data = GetTextCardMessage(text)},
            sequence = sequence
        });
        var response = _restClient.GetClient().Execute(request, Method.Put);
        var o = JObject.Parse(response.Content);
        if (o.Value<int>("code") != 0)
        {
            Console.WriteLine(response.Content);
        }
    }
    
    /// <summary>
    /// 保存上下文断点，发送一张卡片，点击按钮可以继续上次的聊天
    /// </summary>
    /// <param name="cacheKey"></param>
    /// <returns></returns>
    public string GetChatContextSavedActionCardMessage(string cacheKey)
    {
        var actionSizes = new List<object>();
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "恢复上下文继续聊天"},
            type = "primary",
            value = new { action = cacheKey, type = "recover_chatcontext" }
        });
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "删除该断点"},
            type = "default",
            value = new { action = cacheKey, type = "clear_chatcontext" }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""default""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""上下文断点已保存，你可以开始新会话。\n7天内点击该按钮可以继续本次聊天。点击删除断点会立即删除，无法再恢复。"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionSizes) + @"
                  }
                ]
              }
            }";
        return msg;
    }
    public string GetChatContextClearedActionCardMessage()
    {
        var msg = @"{
              ""config"": {
                ""width_mode"": ""default""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""上下文断点已删除。"",
                      ""tag"": ""lark_md""
                    }
                  }
                ]
              }
            }";
        return msg;
    }
    
    /// <summary>
    /// 飞书的语音识别，对文件格式要求比较高
    /// 转换为pcm：  ffmpeg -i sun.opus -f s16le  -ar 48000 -acodec pcm_s16le output.raw
    /// 降采样 为16k：   ffmpeg -ar 48000 -channels 1 -f s16le -i output.raw -ar 16000 -channels 1 -f output2.raw
    /// 试听： ffplay -ar 16000 -channels 1 -f s16le -i filepath
    /// </summary>
    /// <param name="file_key"></param>
    /// <param name="file"></param>
    /// <returns></returns>
    public Result VoiceToText(byte[]  bytes, string fileName)
    {
        var ext = "wav";
        if (fileName.EndsWith(".mp3"))
            ext = "mp3";
        else if (fileName.EndsWith(".m4a"))
            ext = "m4a";
        else if (fileName.EndsWith(".pcm"))
            ext = "pcm";
        else if (fileName.EndsWith(".opus")||fileName.EndsWith(".ogg"))
            ext = "opus";
        var random =  new Random().Next(100000, 999999).ToString();
        bytes = ConvertAudioToFeishuPcm(bytes, ext, "pcm", random);
        
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return Result.Error(string.Empty);
        var request = new RestRequest(FileRecognizeUrl, Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            speech = new
            {
                speech = Convert.ToBase64String(bytes)
            },
            config = new
            {
                file_id = HashHelper.GetMd5_16Str(bytes),
                format = "pcm",
                engine_type = "16k_auto"
            }
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var obj = JObject.Parse(response.Content);
        if (obj["code"].Value<int>() == 0)
        {
            return Result.Answer(obj["data"]["recognition_text"].Value<string>());
        }
        else
        {
            Console.WriteLine(response.Content);
            return Result.Error(obj["msg"].Value<string>());
        }
    }
    
    public byte[] ConvertAudioToFeishuPcm(byte[] mp3File, string sourceFormat, string targetFormat, string user_id)
    {
        lock (lockObj)
        {
            var fileWav = $"./{user_id}_audio." + sourceFormat;
            var fileOpus = $"./{user_id}_audio." + targetFormat;
            File.WriteAllBytes(fileWav, mp3File);
            FFMpegArguments.FromFileInput(fileWav)
                .OutputToFile(fileOpus, true, options => options.WithCustomArgument("-f s16le -ar 16000 -ac 1 -acodec pcm_s16le"))
                .ProcessSynchronously();
            var file = File.ReadAllBytes(fileOpus);
            File.Delete(fileWav);
            File.Delete(fileOpus);
            return file;
        }
    }
    
    public byte[] GetThumbFromVideoFile(byte[] videoFile, string user_id)
    {
        lock (lockObj)
        {
            var fileInput = $"./{user_id}_video.mp4";
            var fileOutput = $"./{user_id}_thumb.jpg";
            File.WriteAllBytes(fileInput, videoFile);
            FFMpegArguments.FromFileInput(fileInput)
                .OutputToFile(fileOutput, true, options => options.WithCustomArgument("-ss 00:00:00 -vframes 1"))
                .ProcessSynchronously();
            var file = File.ReadAllBytes(fileOutput);
            File.Delete(fileInput);
            File.Delete(fileOutput);
            return file;
        }
    }

    /// <summary>
    /// 上传图片到飞书获取image_key用来发消息
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    protected string UploadImageToFeishu(byte[] file)
    {
        var token = GetToken();
        var hc = _httpClientFactory.CreateClient();
        hc.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("message"), "image_type");
        content.Add(new ByteArrayContent(file), "image");
        var url = "https://open.feishu.cn/open-apis/im/v1/images";
        var result = hc.PostAsync(url, content).Result.Content.ReadAsStringAsync().Result;
        var obj = JObject.Parse(result);
        if (obj["code"].Value<int>() == 0)
        {
            return obj["data"]["image_key"].Value<string>();
        }
        else
        {
            Console.WriteLine(result);
        }
        return string.Empty;
    }
    
    /// <summary>
    /// 上传图片接口图片会被压缩，使用文件格式上传源图
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    public string UploadFileToFeishu(byte[] file, string fileName, int duration = 0)
    {
        var token = GetToken();
        var hc = _httpClientFactory.CreateClient();
        hc.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        var content = new MultipartFormDataContent();
        var fileType = "stream";
        var dotIndex = fileName.LastIndexOf(".") + 1;
        if (dotIndex > 0)
        {
            var ext = fileName.ToLower().Substring(dotIndex);
            if (ext == "docx" || ext == "xlsx" || ext == "pptx")
                ext = ext.Substring(0, 3);
            if (ext == "opus" || ext == "pdf" || ext == "mp4" || ext == "xls" || ext == "doc" || ext == "ppt")
                fileType = ext;
        }

        content.Add(new StringContent(fileType), "file_type");
        content.Add(new StringContent(fileName), "file_name");
        if (fileType == "opus" || fileType == "mp4")
            content.Add(new StringContent(duration.ToString()), "duration");
        content.Add(new ByteArrayContent(file), "file");
        var url = "https://open.feishu.cn/open-apis/im/v1/files";
        var result = hc.PostAsync(url, content).Result.Content.ReadAsStringAsync().Result;
        var obj = JObject.Parse(result);
        if (obj["code"].Value<int>() == 0)
        {
            return obj["data"]["file_key"].Value<string>();
        }
        else
        {
            Console.WriteLine(result);
        }
        return string.Empty;
    }
    
    //用户点击选择模型，选择画图样式，和尺寸的卡片按钮的时候，走同步逻辑
    public abstract bool IsSyncAction(string type);
    
    #region 基本接口，发消息接口
    protected const string GetUserAccessTokenUrl = "open-apis/authen/v1/access_token";
    protected const string RefreshUserAccessTokenUrl = "open-apis/authen/v1/refresh_access_token";
    
    /// <summary>
    /// 获取用户的User Access Token
    /// </summary>
    /// <param name="code">用户授权登录后返回的Code</param>
    public (bool success, string message) GetUserAccessTokenByCode(string code)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return (false, "获取应用Token失败");
        var request = new RestRequest(GetUserAccessTokenUrl, Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            grant_type = "authorization_code",
            code = code,
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            var d = o["data"];
            _logRepository.AddOrUpdateUserAccessToken(AppId, d["user_id"].Value<string>(), d["access_token"].Value<string>(),
                d["refresh_token"].Value<string>(), d["expires_in"].Value<int>(), d["refresh_expires_in"].Value<int>());
            return (true, string.Empty);
        }
        else
        {
            return (false, o["msg"].Value<string>());
        }
    }
    
    /// <summary>
    /// 刷新用户的User Access Token
    /// </summary>
    /// <param name="code">用户授权登录后返回的Code</param>
    public (bool success, string message) RefreshUserAccessToken(string refresh_token)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token)) return (false, "获取应用Token失败");
        var request = new RestRequest(RefreshUserAccessTokenUrl, Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            grant_type = "refresh_token",
            refresh_token = refresh_token,
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            var d = o["data"];
            _logRepository.AddOrUpdateUserAccessToken(AppId, d["user_id"].Value<string>(), d["access_token"].Value<string>(),
                d["refresh_token"].Value<string>(), d["expires_in"].Value<int>(), d["refresh_expires_in"].Value<int>());
            return (true, string.Empty);
        }
        else
        {
            return (false, o["msg"].Value<string>());
        }
    }
    
    /// <summary>
    /// 内部获取用户AccessToken，用来执行其它操作
    /// </summary>
    /// <param name="user_id"></param>
    /// <returns></returns>
    public string GetUserAccessToken(string user_id)
    {
        var res = _logRepository.GetUserAccessToken(AppId, user_id);
        if (!string.IsNullOrEmpty(res.access_token))
            return res.access_token;
        if (!string.IsNullOrEmpty(res.refresh_token))
        {
            var result = RefreshUserAccessToken(res.refresh_token);
            if (result.success)
            {
                var res2 = _logRepository.GetUserAccessToken(AppId, user_id);
                return res2.access_token;
            }
        }

        var text =
            $"该功能需要使用您的个人授权，[请点击此处进行授权](https://open.feishu.cn/open-apis/authen/v1/index?redirect_uri={SiteHost}api/ai/{GetControllerPath()}/login&app_id={AppId}&state=0)，授权完成后重新调用该功能。";
        SendMessage(user_id, text, FeishuMessageType.PlainText);
        return string.Empty;
    }
    
    public abstract string GetControllerPath();
    #endregion
    
    
    #region 个人资源接口

    /// <summary>
    /// 创建个人待办任务
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="summary"></param>
    /// <param name="endTime"></param>
    /// <returns></returns>
    public bool CreateUserTask(string user_id, string summary, DateTime endTime)
    {
        var token = GetUserAccessToken(user_id);
        if (string.IsNullOrEmpty(token)) return false;
        
        var request = new RestRequest("open-apis/task/v2/tasks?user_id_type=user_id", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            summary = summary,
            due = new
            {
                timestamp = new DateTimeOffset(endTime).ToUnixTimeMilliseconds()
            },
            members= new[]
            {
                new{id=user_id, type="user", role = "assignee"}
            }
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            SendMessage(user_id, $"待办任务已添加：{summary} {endTime.ToString("yyyy-MM-dd HH:mm")}");
            return true;
        }
        else
        {
            SendMessage(user_id, "待办任务创建失败，" + o["msg"].Value<string>());
            return false;
        }
    }

    /// <summary>
    /// 创建个人云文档
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="title"></param>
    /// <param name="blocks"></param>
    /// <returns></returns>
    public string CreateUserDocument(string user_id, string title, List<DocumentBlock> blocks)
    {
        var token = GetUserAccessToken(user_id);
        if (string.IsNullOrEmpty(token)) return string.Empty;
        
        var request = new RestRequest("open-apis/docx/v1/documents", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            title = title
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            var doc_id = o["data"]["document"]["document_id"].Value<string>();
            var subResp = AddBlocksToDocument(doc_id, blocks, token);
            if (subResp.success)
            {
                return doc_id;
            }
            else
            {
                SendMessage(user_id, $"文档已创建，但内容插入失败了，{subResp.message}。https://yesmro101.feishu.cn/docx/{doc_id}", FeishuMessageType.PlainText);
                return string.Empty;
            }
        }
        else
        {
            SendMessage(user_id, "待办任务创建失败，" + o["msg"].Value<string>());
            return string.Empty;
        }
    }

    private (bool success, string message) AddBlocksToDocument(string doc_id, List<DocumentBlock> blocks, string token)
    {
        var elements = new List<object>();
        foreach (var block in blocks)
        {
            if (block.Type == DocumentBlock.BlockType.Text)
            {
                if (!string.IsNullOrEmpty(block.Content))
                {
                    var ss = block.Content.Split("**");
                    var list = new List<object>();
                    for (var i = 0; i < ss.Length; i++)
                    {
                        if(ss[i].Length>0)
                            list.Add(new
                                { text_run = new { content = ss[i], text_element_style = new { bold = i % 2 == 1 } } });
                    }
                    elements.Add(new
                    {
                        block_type = 2, text = new
                        {
                            elements = list
                        }
                    });
                }
            }else if (block.Type == DocumentBlock.BlockType.H2)
            {
                elements.Add(new
                {
                    block_type = 4, heading2 = new
                    {
                        elements = new[] {new {text_run = new {content = block.Content}}}
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.H3)
            {
                elements.Add(new
                {
                    block_type = 5, heading3 = new
                    {
                        elements = new[] {new {text_run = new {content = block.Content}}}
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.H4)
            {
                elements.Add(new
                {
                    block_type = 6, heading4 = new
                    {
                        elements = new[] {new {text_run = new {content = block.Content}}}
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.H5)
            {
                elements.Add(new
                {
                    block_type = 7, heading5 = new
                    {
                        elements = new[] {new {text_run = new {content = block.Content}}}
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.Code)
            {
                elements.Add(new
                {
                    block_type = 14, code = new
                    {
                        elements = new[] {new {text_run = new {content = block.Content}}},
                        style = new
                        {
                            language = DocumentBlock.LanguageCode.TryGetValue(block.Language, out var lan) ? lan : 1,
                            wrap = true
                        }
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.Image || block.Type == DocumentBlock.BlockType.ImageB64)
            {
                elements.Add(new
                {
                    block_type = 27, image = new
                    {
                        align = 1, token=""
                    }
                });
            }else if (block.Type == DocumentBlock.BlockType.File)
            {
                elements.Add(new
                {
                    block_type = 23, view_type = new
                    {
                        view_type = 1
                    }
                });
            }
            else if (block.Type == DocumentBlock.BlockType.OL)
            {
                var jar = block.Content.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(
                        x =>
                        {
                            var ss = x.Split("**");
                            var list = new List<object>();
                            for (var i = 0; i < ss.Length; i++)
                            {
                                if(ss[i].Length>0)
                                    list.Add(new
                                        { text_run = new { content = ss[i], text_element_style = new { bold = i % 2 == 1 } } });
                            }
                            return new
                            {
                                block_type = 13, ordered = new {elements = list}
                            };
                        })
                    .ToArray();
                elements.AddRange(jar);
            }else if (block.Type == DocumentBlock.BlockType.UL)
            {
                var jar = block.Content.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(
                        x =>
                        {
                            var ss = x.Split("**");
                            var list = new List<object>();
                            for (var i = 0; i < ss.Length; i++)
                            {
                                if(ss[i].Length>0)
                                    list.Add(new
                                        { text_run = new { content = ss[i], text_element_style = new { bold = i % 2 == 1 } } });
                            }
                            return new
                            {
                                block_type = 12, bullet = new
                                {
                                    elements = list
                                }
                            };
                        })
                    .ToArray();
                elements.AddRange(jar);
            }
        }

        var pageIndex = 1;
        var pageSize = 40;
        while (true)
        {
            var subElements = elements.Skip((pageIndex - 1) * pageSize).Take(pageSize);
            if (subElements.Count() == 0)
                break;
            pageIndex++;

            var body = JsonConvert.SerializeObject(new
            {
                children = subElements
            });
            var request = new RestRequest($"open-apis/docx/v1/documents/{doc_id}/blocks/{doc_id}/children", Method.Post);
            request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
            request.AddParameter("application/json", body, ParameterType.RequestBody);
            var response = _restClient.GetClient().Execute(request, Method.Post);
            var o = JObject.Parse(response.Content);
            if (o["code"].Value<int>() == 0)
            {
                var arr = o["data"]["children"] as JArray;
                var imgIndex = 0;
                var hc = _httpClientFactory.CreateClient();
                var needUpdateBlocks = new List<JObject>();
                foreach (var tk in arr)
                {
                    var block_type = tk["block_type"].Value<int>();
                    if (block_type == 27 || block_type == 23) //文件或图片
                    {
                        try
                        {
                            var block_id = tk["block_id"].Value<string>();
                            var source = blocks.Where(t =>
                                    t.Type == DocumentBlock.BlockType.Image ||
                                    t.Type == DocumentBlock.BlockType.ImageB64 ||
                                    t.Type == DocumentBlock.BlockType.File)
                                .ToList()[imgIndex];
                            if (source.Content.StartsWith("https://") || source.Content.StartsWith("http://")) //处理文件或图片类型的附件的上传
                            {
                                var file = hc.GetByteArrayAsync(source.Content).Result;
                                if (block_type == 27)
                                {
                                    file = ImageHelper.Compress(file);
                                }
                                var file_token = UploadDocumentImageOrFileToFeishu(token, block_id,
                                    block_type == 27 ? "image" : "file", $"image_{imgIndex}.jpg", file);
                                if (!string.IsNullOrEmpty(file_token))
                                {
                                    var objStr = JsonConvert.SerializeObject(new
                                    {
                                        block_id = block_id, replace_image = new { token = file_token }
                                    });
                                    if (block_type == 23)
                                        objStr = objStr.Replace("replace_image", "replace_file");
                                    needUpdateBlocks.Add(JObject.Parse(objStr));
                                }
                            }else if (source.Type == DocumentBlock.BlockType.ImageB64)
                            {
                                var file = Convert.FromBase64String(source.Content);
                                if (block_type == 27)
                                {
                                    file = ImageHelper.Compress(file);
                                }
                                var file_token = UploadDocumentImageOrFileToFeishu(token, block_id,
                                    block_type == 27 ? "image" : "file", $"image_{imgIndex}.jpg", file);
                                if (!string.IsNullOrEmpty(file_token))
                                {
                                    var objStr = JsonConvert.SerializeObject(new
                                    {
                                        block_id = block_id, replace_image = new { token = file_token }
                                    });
                                    if (block_type == 23)
                                        objStr = objStr.Replace("replace_image", "replace_file");
                                    needUpdateBlocks.Add(JObject.Parse(objStr));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("上传文档图片：" + ex.ToString());
                        }

                        imgIndex++;
                    }
                }

                if (needUpdateBlocks.Count > 0)
                {
                    request = new RestRequest($"open-apis/docx/v1/documents/{doc_id}/blocks/batch_update",
                        Method.Patch);
                    request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);

                    body = JsonConvert.SerializeObject(new
                    {
                        requests = needUpdateBlocks
                    });
                    request.AddParameter("application/json", body, ParameterType.RequestBody);
                    response = _restClient.GetClient().Execute(request, Method.Patch);
                    o = JObject.Parse(response.Content);
                    if (o["code"].Value<int>() != 0)
                    {
                        return (false, o["msg"].Value<string>());
                    }
                }
            }
            else
                return (false, o["msg"].Value<string>());
        }

        return (true, string.Empty);
    }
    
    /// <summary>
    /// 上传文档中的图片和文件块对应的文件
    /// </summary>
    /// <param name="user_token"></param>
    /// <param name="block_id"></param>
    /// <param name="type">image 或 file</param>
    /// <param name="fileName"></param>
    /// <param name="file"></param>
    /// <returns></returns>
    private string UploadDocumentImageOrFileToFeishu(string user_token, string block_id, string type, string fileName, byte[] file)
    {
        var hc = _httpClientFactory.CreateClient();
        hc.DefaultRequestHeaders.Add("Authorization", "Bearer " + user_token);
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(fileName), "file_name");
        content.Add(new StringContent(type=="image"?"docx_image":"docx_file"), "parent_type");
        content.Add(new StringContent(block_id), "parent_node");
        content.Add(new StringContent(file.Length.ToString()), "size");
        content.Add(new ByteArrayContent(file), "file");
        var url = "https://open.feishu.cn/open-apis/drive/v1/medias/upload_all";
        var result = hc.PostAsync(url, content).Result.Content.ReadAsStringAsync().Result;
        var obj = JObject.Parse(result);
        if (obj["code"].Value<int>() == 0)
        {
            return obj["data"]["file_token"].Value<string>();
        }
        Console.WriteLine(result);
        return string.Empty;
    }
    
    /// <summary>
    /// 获取用户日历ID，用于创建日程
    /// </summary>
    /// <param name="user_id"></param>
    /// <returns></returns>
    private string GetUserPrimaryCalendarId(string user_id, string token)
    {
        var request = new RestRequest("open-apis/calendar/v4/calendars/primary", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            var cals = o["data"]["calendars"] as JArray;
            if (cals.Count > 0)
                return cals[0]["calendar"]["calendar_id"].Value<string>();
            return string.Empty;
        }
        else
        {
            SendMessage(user_id, "获取主日历ID失败，" + o["msg"].Value<string>());
            return string.Empty;
        }
    }

    /// <summary>
    /// 在用户的日历中创建一条日程，指定开始时间，默认为1小时
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="summary"></param>
    /// <param name="startTime"></param>
    /// <returns></returns>
    public bool CreateUserCalendarEvent(string user_id, string summary, DateTime startTime)
    {
        var token = GetUserAccessToken(user_id);
        if (string.IsNullOrEmpty(token)) return false;
        var calId = GetUserPrimaryCalendarId(user_id, token);
        var request = new RestRequest($"open-apis/calendar/v4/calendars/{calId}/events", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            summary = summary,
            start_time = new
            {
                timestamp = (new DateTimeOffset(startTime).ToUnixTimeMilliseconds()/1000).ToString()
            },
            end_time = new
            {
                timestamp = (new DateTimeOffset(startTime.AddHours(1)).ToUnixTimeMilliseconds()/1000).ToString()
            }
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            SendMessage(user_id, $"日程已添加：{summary} {startTime.ToString("yyyy-MM-dd HH:mm")}");
            return true;
        }
        else
        {
            SendMessage(user_id, "日程创建失败，" + o["msg"].Value<string>());
            return false;
        }
    }
    
    public async Task<(bool success, string title, List<DocumentBlock>? blocks)> GetMergeForwardMessageContent(string msg_id, bool withImages = false)
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
            return (false, string.Empty, null);
        
        var request = new RestRequest($"open-apis/im/v1/messages/{msg_id}?user_id_type=user_id", Method.Get);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        try
        {
            var response = _restClient.GetClient().Execute(request, Method.Get);
            var responseContent = response.Content;
            var o = JObject.Parse(responseContent);
            if (o.Value<int>("code") == 0)
            {
                var items = o["data"]["items"] as JArray;
                var blocks = new List<DocumentBlock>();
                foreach (var item in items)
                {
                    var msg_type = item["msg_type"].Value<string>();
                    if (msg_type == "merge_forward")
                    {
                        //第一条消息，没有内容，忽略
                    }
                    else if (msg_type == "text")
                    {
                        var user = "";
                        if (item["sender"]["sender_type"].Value<string>() == "user")
                        {
                            var userid = item["sender"]["id"].Value<string>();
                            user = userid;
                        }
                        var o1 = JObject.Parse(item["body"]["content"].Value<string>());
                        blocks.Add(new DocumentBlock()
                        {
                            Type = DocumentBlock.BlockType.Text, Content = user + ":\n" + o1["text"].Value<string>()
                        });
                    }
                    else if (msg_type == "post")
                    {
                        var o1 = JObject.Parse(item["body"]["content"].Value<string>());
                        var cons = o1["content"] as JArray;
                        var sb = new StringBuilder();
                        List<string> images = new List<string>();
                        foreach (var con in cons)
                        {
                            var arr = con as JArray;
                            foreach (var t in arr)
                            {
                                if (t["tag"].Value<string>() == "text")
                                    sb.Append(t["text"].Value<string>());
                                else if (t["tag"].Value<string>() == "img")
                                    images.Add(t["image_key"].Value<string>());
                            }
                        }

                        var user = "";
                        if (item["sender"]["sender_type"].Value<string>() == "user")
                        {
                            var userid = item["sender"]["id"].Value<string>();
                            user = userid;
                        }
                        if (sb.Length > 0)
                        {
                            blocks.Add(new DocumentBlock()
                                { Type = DocumentBlock.BlockType.Text, Content = user+":\n"+ sb.ToString() });
                        }
                    }else if (msg_type == "image" && withImages)
                    {
                        var o1 = JObject.Parse(item["body"]["content"].Value<string>());
                        var mid = item["message_id"].Value<string>();
                        var file_key = o1["image_key"].Value<string>();
                        var image = await DownloadFile(mid, file_key, "image");
                        
                        var user = "";
                        if (item["sender"]["sender_type"].Value<string>() == "user")
                        {
                            var userid = item["sender"]["id"].Value<string>();
                            user = userid;
                        }
                        blocks.Add(new DocumentBlock()
                            { Type = DocumentBlock.BlockType.Text, Content = user+":" });
                        blocks.Add(new DocumentBlock()
                            { Type = DocumentBlock.BlockType.ImageB64, Content = Convert.ToBase64String(image) });
                    }
                }

                return (true, "", blocks);
            }
            else
            {
                Console.WriteLine(responseContent);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }

        return (false, "获取消息数据错误", null);
    }
    
    public async Task<(bool success, string message)> GetMergeForwardMessageCreateDocument(string user_id, string msg_id, string type="doc")
    {
        SendMessage(user_id, "处理中，请稍候...");
        var (success, title, blocks) = await GetMergeForwardMessageContent(msg_id, true);
        if (success)
        {
            title = (blocks.FirstOrDefault(t => t.Type == DocumentBlock.BlockType.Text)?.Content ?? "合并转发聊天记录");
            var doc_id = CreateUserDocument(user_id, title.Substring(0, Math.Min(title.Length, 20)), blocks);
            if (!string.IsNullOrEmpty(doc_id))
            {
                SendMessage(user_id, $"文档已创建成功。https://yesmro101.feishu.cn/docx/{doc_id}", FeishuMessageType.PlainText);
            }
            return (!string.IsNullOrEmpty(doc_id), string.Empty);
        }
        else
        {
            return (success, title);
        }
    }
    

    public class DocumentBlock
    {
        public enum BlockType
        {
            Text, Code, Image, File, OL, UL, H2, H3, H4, H5, ImageB64
        }

        public static Dictionary<string, int> LanguageCode = new Dictionary<string, int>()
        {
            {"csharp", 8}, {"css", 12}, {"go", 22}, {"html", 24}, {"json", 28}, {"java", 29}, {"javascript", 30},
            {"python", 49}, {"sql", 56}, {"shell", 60}, {"typescript", 63},
        };
        public BlockType Type { get; set; }
        public string Content { get; set; }
        public string Language { get; set; } //代码语言
    }
    
    
    
    /// <summary>
    /// 导出聊天记录到文本
    /// </summary>
    /// <param name="chatModel"></param>
    /// <param name="contexts"></param>
    /// <param name="user_id"></param>
    public void ExportContextToTxt(int chatModel, ChatContexts contexts, string user_id)
    {
        if (!ChatModel.IsTextModel(chatModel))
        {
            SendMessage(user_id, "只能导出语言模型的聊天记录。");
            return;
        }
        if (contexts.IsEmpty())
        {
            SendMessage(user_id, "当前上下文中没有聊天记录。");
            return;
        }
        
        var m = ChatModel.GetModel(chatModel);
        var label = m?.Name ?? "AI助手";
        var sb = new StringBuilder();
        foreach (var ctx in contexts.Contexts)
        {
            foreach (var qc in ctx.QC)
            {
                if(qc.Type == ChatType.文本)
                    sb.Append($"我:\n{qc.Content}\n\n");
            }
            foreach (var ac in ctx.AC)
            {
                if(ac.Type == ChatType.文本)
                    sb.Append($"{label}:\n{ac.Content}\n\n");
            }
        }
        using (var ms = new MemoryStream())
        {
            using(TextWriter tw = new StreamWriter(ms)){
                tw.Write(sb.ToString());
                tw.Flush();
                ms.Position = 0;
                var bytes = ms.ToArray();

                var fileKey = UploadFileToFeishu(bytes,
                    DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt");
                SendMessage(user_id, fileKey, FeishuMessageType.File);
            }
        }
    }
    
    /// <summary>
    /// 导出聊天记录到PDF，本地原生导出
    /// </summary>
    /// <param name="contexts"></param>
    /// <param name="user_id"></param>
    public async Task ExportContextToPdf(ChatContexts contexts, string user_id)
    {
        var bytes = PdfHelper.GeneratePdf(contexts);
        var fileKey = UploadFileToFeishu(bytes, "对话.pdf");
        SendMessage(user_id, fileKey, FeishuMessageType.File);
    }
    
    /// <summary>
    /// 导出聊天记录到PDF，生成URL并由远程服务导出
    /// </summary>
    /// <param name="chatModel"></param>
    /// <param name="user_id"></param>
    /// <param name="sessionId"></param>
    public async Task ExportContextToPdf(int chatModel, string user_id, string sessionId, int logId)
    {
        if (!ChatModel.IsTextModel(chatModel))
        {
            SendMessage(user_id, "只能导出语言模型的聊天记录。");
            return;
        }

        var url = SiteHost + "api/ai/session/" + sessionId+"?id="+logId;
        var bytes = PdfHelper.GeneratePdfByUrl(url);
        var pdfFileName = "AI聊天记录"+ DateTime.Now.ToString("yyyyMMddHHmmss")+".pdf";
        var fileKey = UploadFileToFeishu(bytes,  pdfFileName);
        SendMessage(user_id, fileKey, FeishuMessageType.File);
    }
    
    public async Task ExportContextToPdf(string user_id, string url, bool toImage = false)
    {
        var bytes = PdfHelper.GeneratePdfByUrl(url, toImage);
        if (toImage)
        {
            var imageKey = UploadImageToFeishu(bytes);
            SendMessage(user_id, imageKey, FeishuMessageType.Image);
        }
        else
        {
            var pdfFileName = DateTime.Now.ToString("yyyyMMddHHmmss");
            var fileKey = UploadFileToFeishu(bytes, pdfFileName + ".pdf");
            SendMessage(user_id, fileKey, FeishuMessageType.File);
        }
    }
    
    public async Task ExportAndSendPdf(string user_id, string content)
    {
        var context = ChatContexts.New("这是一个提问");
        context.AddAnswer(content);
        var bytes = PdfHelper.GeneratePdf(context);
        var fileKey = UploadFileToFeishu(bytes, "对话.pdf");
        SendMessage(user_id, fileKey, FeishuMessageType.File);
    }
    
    /// <summary>
    /// 导出聊天记录到云文档
    /// </summary>
    /// <param name="chatModel"></param>
    /// <param name="contexts"></param>
    /// <param name="user_id"></param>
    public void ExportContextToDocument(int chatModel, ChatContexts contexts, string user_id)
    {
        if (!ChatModel.IsTextModel(chatModel))
        {
            SendMessage(user_id, "只能导出语言模型的聊天记录。");
            return;
        }
        if (contexts.IsEmpty())
        {
            SendMessage(user_id, "当前上下文中没有聊天记录。");
            return;
        }
        var blocks = new List<DocumentBlock>();
        foreach (var ctx in contexts.Contexts)
        {
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.文本)
                {
                    if (!qc.Content.StartsWith("[Q]"))
                    {
                        if (qc.Content.StartsWith("{"))
                        {
                            try
                            {
                                var art = JsonConvert.DeserializeObject<JinaAiClient.Article>(qc.Content);
                                qc.FileName = art.Title;
                                blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.Text, Content = "原文链接："+ art.Url });
                                blocks.AddRange(GetBlocksFromMarkdown(art.Content, ChatType.文本));
                            }
                            catch
                            {
                                blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.Text, Content = qc.Content });
                            }
                        }
                        else
                        {
                            blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.Text, Content = $"**我: {qc.Content}**" });
                        }
                    }
                }else if (qc.Type == ChatType.图片Base64)
                {
                    blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.ImageB64, Content = qc.Content });
                }
            }

            foreach (var ac in ctx.AC)
            {
                if(!ac.Content.StartsWith("```json"))
                    blocks.AddRange(GetBlocksFromMarkdown(ac.Content, ac.Type));
            }
        }

        var first = contexts.Contexts.First().QC.First();
        var title = string.IsNullOrEmpty(first.FileName)
            ? first.Content
            : first.FileName;
        if (title.Length > 20)
            title = title.Substring(0, 20) + "...";
        var doc_id = CreateUserDocument(user_id, title, blocks);
        if(!string.IsNullOrEmpty(doc_id))
            SendMessage(user_id, $"文档已创建成功。https://yesmro101.feishu.cn/docx/{doc_id}", FeishuMessageType.PlainText);
    }
    
    public List<DocumentBlock> GetBlocksFromMarkdown(string answer, ChatType at)
    {
        var blocks = new List<DocumentBlock>();
        if (at == ChatType.图片Base64)
        {
            blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.ImageB64, Content = answer });
            return blocks;
        } 
        //标题
        Regex regHeader = new Regex(@"^(\#{2,5})[ ]*(.+)", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
        Regex regOL = new Regex(@"^(\s*)([\d]+\.)[ ]*(.+)", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
        Regex regUL = new Regex(@"^(\s*)([-]{1})[ ]*(.+)", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
        Regex regImg = new Regex(@"!\[(.*?)\]\((.*)\)", RegexOptions.Compiled);
        var ss = answer.Split("\n");
        var sb = new StringBuilder();
        bool inCodeBlock = false;
        string codeLanguage = "";
        foreach (var s in ss)
        {
            var m = regHeader.Match(s);
            if (m.Success)
            {
                AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
                sb.Clear();
                var level = m.Groups[1].Value.Length;
                var type = DocumentBlock.BlockType.H5;
                switch (level)
                {
                    case 2:
                        type = DocumentBlock.BlockType.H2;
                        break;
                    case 3:
                        type = DocumentBlock.BlockType.H3;
                        break;
                    case 4:
                        type = DocumentBlock.BlockType.H4;
                        break;
                    case 5:
                        type = DocumentBlock.BlockType.H5;
                        break;
                }
                blocks.Add(new DocumentBlock() {Type = type, Content = m.Groups[2].Value});
                continue;
            }
            m = regOL.Match(s);
            if (m.Success)
            {
                AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
                sb.Clear();
                blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.OL, Content = m.Groups[3].Value});
                continue;
            }
            m = regUL.Match(s);
            if (m.Success)
            {
                AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
                sb.Clear();
                blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.UL, Content = m.Groups[3].Value});
                continue;
            }

            m = regImg.Match(s);
            if (m.Success)
            {
                AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
                sb.Clear();
                try
                {
                    if (m.Groups[2].Value.StartsWith("https://"))
                    {
                        var bytes = _httpClientFactory.CreateClient().GetByteArrayAsync(m.Groups[2].Value).Result;
                        blocks.Add(new DocumentBlock() { Type = DocumentBlock.BlockType.ImageB64, Content = Convert.ToBase64String(bytes) });
                    }
                }catch{}
                continue;
            }

            if (s.Trim().StartsWith("```"))
            {
                AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
                sb.Clear();
                var s1 = s.Trim().Remove(0, 3);
                inCodeBlock = s1.Length>0;
                if (inCodeBlock)
                    codeLanguage = s1;
                continue;
            }

            sb.AppendLine(s);
        }
        AddTextBlock(blocks, sb, inCodeBlock, codeLanguage);
        return blocks;
    }

    private void AddTextBlock(List<DocumentBlock> blocks, StringBuilder sb, bool inCodeBlock, string codeLanguage)
    {
        if (!string.IsNullOrEmpty(sb.ToString().Trim()))
        {
            if(inCodeBlock)
                blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Code, Content = sb.ToString(), Language = codeLanguage});
            else
               blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = sb.ToString().Trim()});
        }
    }
    
    public void SaveContextToContinue(int chatModel, ChatContexts contexts, string user_id)
    {
        if (contexts.IsEmpty())
        {
            SendMessage(user_id, "当前上下文中没有聊天记录。");
            return;
        }

        var guid = Guid.NewGuid().ToString("N");
        var input = new ApiChatInputIntern()
        {
            ChatContexts = contexts, External_UserId = user_id, ChatModel = chatModel
        };
        CacheService.BSave(guid, input, DateTime.Now.AddDays(10));
        SendMessage(user_id, GetChatContextSavedActionCardMessage(guid), FeishuMessageType.Interactive);
    }

    /// <summary>
    /// 保存上下文断点
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="guid"></param>
    /// <returns></returns>
    public bool SetContextToContinue(string user_id, string guid)
    {
        var input = CacheService.BGet<ApiChatInputIntern>(guid);
        if (input == null)
            return false;
        
        if (input.External_UserId != user_id)
            return false;

        ChatModel.SetDefaultModel(user_id, contextCachePrefix, input.ChatModel);
        ChatContexts.SaveChatContexts(user_id, input.ContextCachePrefix, input.ChatContexts);
        SendMessage(user_id, "聊天上下文已恢复，你可以发送新的消息继续聊天。");
        return true;
    }
    /// <summary>
    /// 删除已保存的上下文断点
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="guid"></param>
    /// <returns></returns>
    public bool SetContextToCleared(string user_id, string guid)
    {
        return CacheService.Delete(guid);
    }
    
    /// <summary>
    /// 导出Mermaid流程图到PDF
    /// </summary>
    /// <param name="logId"></param>
    /// <param name="sessionId"></param>
    /// <param name="user_id"></param>
    public void SendMermaidLink(int logId, string sessionId, string user_id)
    {
        var url = SiteHost + "api/ai/log/" + logId+"?sessionId="+sessionId;
        SendMessage(user_id, $"回复内容似乎包含流程图，<a href=\"{url}\">点击查看</a>。");
    }
    
    public void SendMarkmapLink(int logId, string sessionId, string user_id)
    {
        var url = SiteHost + "api/ai/log/" + logId+"?sessionId="+sessionId;
        SendMessage(user_id, $"回复内容似乎包含思维导图，<a href=\"{url}\">点击查看</a>。");
    }
    
    public void SendHtmlPageLink(int logId, string sessionId, string user_id)
    {
        var url = SiteHost + "api/ai/log/" + logId+"/html/?sessionId="+sessionId;
        SendMessage(user_id, $"回复内容似乎包含HTML网页代码，<a href=\"{url}\">点击查看页面预览</a>。");
    }
    
    public void SendLatexLink(int logId, string sessionId, string user_id)
    {
        var url = SiteHost + "api/ai/log/" + logId+"?sessionId="+sessionId;
        SendMessage(user_id, $"回复内容似乎包含公式，<a href=\"{url}\">点击查看</a>。");
    }
    
    public async Task<byte[]> PdfFileToImage(string fileUrl)
    {
        var client = _httpClientFactory.CreateClient();
        HttpContent content = new StringContent(JsonConvert.SerializeObject(new
        {
            url = fileUrl,
            format = "pdf2png",
            density = 300,
            options = new { resize = "50%", trim = "+repage", bordercolor = "white", border = "30x30" }
        }));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var res = await client.PostAsync(new Uri("http://pdf.yesmro.cn/"), content);
        if (res.IsSuccessStatusCode)
        {
            return await res.Content.ReadAsByteArrayAsync();
        }
        return null;
    }
    
    #endregion

    #region 公用回调事件处理
    public virtual async Task<JObject> ProcessEventCallback(string event_type, JObject obj)
    {
        if (event_type == "im.message.receive_v1")
        {
            BackgroundJob.Enqueue(() => ProcessMessageEvent(obj));
        }
        else if (event_type == "card.action.trigger")
        {
            var user_id = obj["event"]["operator"]["user_id"].Value<string>(); //注意，跟收到消息的不一样
            var value = obj["event"]["action"]["value"];
            var type = value!=null && value["type"] != null ? value["type"].Value<string>() : "";
            if (IsSyncAction(type))
            {
                return await ProcessCardActionMessage(user_id, obj);
            }
            else
            {
                BackgroundJob.Enqueue(() => ProcessCardActionMessage(user_id, obj));
            }
        }
        else if (event_type == "application.bot.menu_v6")
        {
            var user_id = obj["event"]["operator"]["operator_id"]["user_id"].Value<string>();
            BackgroundJob.Enqueue(() => ProcessMenuClickMessage(user_id, obj));
        }
        else if (event_type == "im.chat.access_event.bot_p2p_chat_entered_v1")
        {
            var user_id = obj["event"]["operator_id"]["user_id"].Value<string>();
            BackgroundJob.Enqueue(() => ProcessChatEnteredEvent(user_id));
        }

        return new JObject();
    }

    public virtual async Task ProcessMessageEvent(JObject obj)
    {
        if (obj["event"]["sender"] != null)
        {
            var user_id = obj["event"]["sender"]["sender_id"]["user_id"].Value<string>();
            var msg_id = obj["event"]["message"]["message_id"].Value<string>();
            var msg_type = obj["event"]["message"]["message_type"].Value<string>();
            var msg_content = obj["event"]["message"]["content"].Value<string>();
            var qc = new List<ChatContext.ChatContextContent>();
            if (msg_type == "text") //纯文本消息
            {
                var o = JObject.Parse(msg_content);
                var text = o["text"].Value<string>();
                if (text == "RELOAD CONFIG")
                {
                    _configHelper.ReloadConfig();
                    SendMessage(user_id, "配置文件重新加载成功");
                    return;
                }
                var parent_id = obj["event"]["message"]["parent_id"]==null?"": obj["event"]["message"]["parent_id"].Value<string>();
                if (!string.IsNullOrEmpty(parent_id) && msg_id != parent_id) //引用的消息，把被引用的内容加进来
                {
                    var parent = await GetMessageContent(parent_id);
                    if(!string.IsNullOrEmpty(parent))
                        text = "'''\n" + parent + "\n'''" + text;
                }

                qc.Add(ChatContext.NewContent(text));
            }
            else if (msg_type == "post") //富文本消息
            {
                var o = JObject.Parse(msg_content);
                var cons = o["content"] as JArray;
                var sb = new StringBuilder();
                foreach (var con in cons)
                {
                    var arr = con as JArray;
                    foreach (var t in arr)
                    {
                        var tag = t["tag"].Value<string>();
                        if (tag == "text")
                        {
                            sb.Append(t["text"].Value<string>());
                        }
                        else if (tag == "a")
                        {
                            sb.Append(t["href"].Value<string>());
                        }else if (tag == "img")
                        {
                            if (sb.Length > 0)
                            {
                                qc.Add(ChatContext.NewContent(sb.ToString()));
                                sb.Clear();
                            }
                            var file = await DownloadFile(msg_id, t["image_key"].Value<string>(), "image");
                            if (file != null)
                            {
                                file = ImageHelper.Compress(file);
                                qc.Add(ChatContext.NewContent(Convert.ToBase64String(file), ChatType.图片Base64,
                                    "image/jpeg"));
                            }
                        }else if (tag == "media")
                        {
                            if (sb.Length > 0)
                            {
                                qc.Add(ChatContext.NewContent(sb.ToString()));
                                sb.Clear();
                            }
                            var file = await DownloadFile(msg_id, t["file_key"].Value<string>(), "file");
                            if (file != null)
                            {
                                qc.Add(ChatContext.NewContent("", ChatType.文件Bytes,
                                    "video/mp4", "video.mp4", file));
                            }
                        }
                    }

                    if (sb.Length > 0)
                        sb.Append("\n");
                }
                if (sb.Length > 0)
                {
                    qc.Add(ChatContext.NewContent(sb.ToString()));
                }
            }
            else if (msg_type == "image")
            {
                var o = JObject.Parse(msg_content);
                var file = await DownloadFile(msg_id, o["image_key"].Value<string>(), "image");
                if (file != null)
                {
                    file = ImageHelper.Compress(file);
                    qc.Add(ChatContext.NewContent(Convert.ToBase64String(file), ChatType.图片Base64,
                        "image/jpeg"));
                }
            }
            else if (msg_type == "file"||msg_type == "media")
            {
                var o = JObject.Parse(msg_content);
                var file_key = o["file_key"].Value<string>();
                var file_name = o["file_name"].Value<string>();
                var file = await DownloadFile(msg_id, file_key);
                if (file != null)
                {
                    qc.Add(ChatContext.NewContent("", ChatType.文件Bytes,
                        file_name.EndsWith(".mp4") ? "video/mp4" : "file", file_name, file));
                }
            }
            else if (msg_type == "audio")
            {
                var o = JObject.Parse(msg_content);
                var file_key = o["file_key"].Value<string>();
                var file = await DownloadFile(msg_id, file_key);
                if (file != null)
                {
                    qc.Add(ChatContext.NewContent("", ChatType.文件Bytes, "audio/opus", "audio.opus", file));
                }
            }
            else if (msg_type == "merge_forward") //转发合并的消息
            {
                await ProcessMergeForwardMessage(user_id, msg_id);
            }
            else
            {
                SendMessage(user_id, $"对不起，我无法处理{msg_type}类型消息的内容。");
            }

            if (qc.Count > 0)
            {
                await AskGpt(qc, user_id);
            }
        }
    }

    public virtual int GetUserDefaultModel(string user_id) //子类可以覆盖这个方法，实现固定的默认模型
    {
        return ChatModel.GetUserDefaultModel(user_id, contextCachePrefix);
    }
    /// <summary>
    /// 聊天接口
    /// </summary>
    public virtual async Task AskGpt(List<ChatContext.ChatContextContent> qc,
        string user_id, bool no_function = false, int specialModel = -1)
    {
        SendMessage(user_id, "需要基类实现该方法");
    }
    
    /// <summary>
    /// 不同的子类处理自己的卡片事件
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="obj"></param>
    /// <returns></returns>
    public virtual async Task<JObject> ProcessCardActionMessage(string user_id, JObject obj)
    {
        SendMessage(user_id, "暂时不支持此类消息的处理");
        return new JObject();
    }

    /// <summary>
    /// 不同的子类处理自己的菜单事件
    /// </summary>
    /// <param name="user_id"></param>
    /// <param name="obj"></param>
    public virtual async Task ProcessMenuClickMessage(string user_id, JObject obj)
    {
        SendMessage(user_id, "暂时不支持此类消息的处理");
    }
    
    public async Task ProcessMergeForwardMessage(string user_id, string msg_id)
    {
        SendMessage(user_id, GetMergeForwardActionCardMessage(msg_id),
            FeishuMessageType.Interactive);
    }

    /// <summary>
    /// 用户进入会话事件，每次进入都会触发
    /// </summary>
    /// <param name="user_id"></param>
    public virtual async Task ProcessChatEnteredEvent(string user_id)
    {
        //默认什么都不做
    }
    
    protected JObject GetCardActionSuccessMessage(string msg)
    {
        return JObject.Parse(@"{""toast"":{
            ""type"":""success"",
            ""content"":""切换成功""
        },""card"": {""type"": ""raw"", ""data"": " + msg + "}}");
    } 
    protected JObject GetCardActionSuccessMessage()
    {
        return JObject.Parse(@"{""toast"":{
            ""type"":""success"",
            ""content"":""操作成功""
        }}");
    }
    
    
    /// <summary>
    /// 生成转发合并消息操作选择卡片
    /// </summary>
    /// <param name="msg_id"></param>
    /// <returns></returns>
    public string GetMergeForwardActionCardMessage(string msg_id)
    {
        var actionSizes = new List<object>();
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "创建云文档"},
            type = "primary",
            value = new { action = msg_id, type = "mergemsg_copytodoc" }
        });
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "总结对话内容"},
            type = "primary",
            value = new { action = msg_id, type = "mergemsg_summarize" }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""选择要执行的操作：(总结后可以对对话内容继续提问)"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionSizes) + @"
                  }
                ]
              }
            }";
        return msg;
    }
    #endregion
}
#pragma warning restore 8604, 1998, 8602
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("Google")]
public class ApiGoogleProvider : ApiProviderBase
{
    protected IFunctionRepository _functionRepository;
    protected IHttpClientFactory _httpClientFactory;
    public ApiGoogleProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _functionRepository = functionRepository;
        _httpClientFactory = httpClientFactory;
    }

    private string fileUploadUrl = String.Empty;
    public bool useSystem = true;
    public bool useFunctions = true;
    public bool canGenImage = false;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "v1beta/models/";
        fileUploadUrl = _host + "upload/v1beta/files";
        canGenImage = _extraTools.Contains("GenImage");
        if (canGenImage)
        {
            useSystem = false;
            useFunctions = false;
        }
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input)
    {
        var msgs = new List<Message>();
        Message? sys = null;
        if (!string.IsNullOrEmpty(input.ChatContexts.SystemPrompt))
            sys = new Message()
                {role = "user", parts = new[] {new {text = input.ChatContexts.SystemPrompt}}};
        var resultImageIndex = 0;
        foreach (var ctx in input.ChatContexts.Contexts)
        {
            List<object> contents = new List<object>();
            var role = "user";
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.图片Base64||qc.Type == ChatType.语音Base64)
                {
                    var mimeType = string.IsNullOrEmpty(qc.MimeType) ? (qc.Type == ChatType.语音Base64?"audio/mpeg":"image/jpeg") : qc.MimeType;
                    contents.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = mimeType, data = qc.Content
                        }
                    });
                    useFunctions = false;
                } 
                else if (qc.Type == ChatType.文件Bytes)
                {
                    if (qc.FileName.ToLower().EndsWith(".pdf"))
                    {
                        contents.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = "application/pdf",
                                data = qc.Bytes != null ? Convert.ToBase64String(qc.Bytes) : qc.Content
                            }
                        });
                    }
                    useFunctions = false;
                }
                else if (qc.Type == ChatType.图片Url || qc.Type== ChatType.语音Url || qc.Type== ChatType.视频Url || qc.Type== ChatType.文件Url)
                {
                    contents.Add(new
                    {
                        file_data = new
                        {
                            mime_type = qc.MimeType, file_uri = qc.Content
                        }
                    });
                    useFunctions = false;
                }
                else if (qc.Type == ChatType.文本 || qc.Type == ChatType.提示模板|| qc.Type== ChatType.图书全文)
                {
                    if (qc.Content.StartsWith("https://www.youtube.com/"))
                    {
                        contents.Add(new
                        {
                            file_data = new
                            {
                                file_uri = qc.Content
                            }
                        });
                        contents.Add(new { text = "详细总结一下这条视频的内容。" });
                        useFunctions = false;
                    }else
                       contents.Add(new { text = qc.Content });
                }
                else if (qc.Type == ChatType.缓存ID)
                {
                    input.CachedContentId = qc.Content;
                }
            }
            if(contents.Count > 0)
                msgs.Add(new Message() { role = role, parts = contents.ToArray() });

            foreach (var ac in ctx.AC)
            {
                if (ac.Type== ChatType.文本 && !string.IsNullOrEmpty(ac.Content))
                {
                    msgs.Add(new Message() { role = "model", parts = new[] { new { text = ac.Content } } });
                }
                else if (ac.Type == ChatType.MultiResult)
                {
                    contents.Clear();
                    var results = JArray.Parse(ac.Content);
                    foreach (var tk in results)
                    {
                        if (tk["resultType"].Value<int>() == (int)ResultType.ImageBytes)
                        {
                            resultImageIndex++;
                            if (resultImageIndex >= input.ChatContexts.ResultImagesCount - 2)//带上最近3张图片
                            {
                                contents.Add(new
                                {
                                    inline_data = new
                                    {
                                        mime_type =  "image/png", data = tk["result"].Value<string>()
                                    }
                                });
                            }
                        }else if (tk["resultType"].Value<int>() == (int)ResultType.Answer)
                        {
                            contents.Add(new { text = tk["result"].Value<string>()});
                        }
                    }
                    msgs.Add(new Message() { role = "model", parts = contents.ToArray() });
                }
                else if (ac.Type == ChatType.FunctionCall)
                {
                    var acalls = JsonConvert.DeserializeObject<List<FunctionCall>>(ac.Content);
                    contents.Clear();
                    foreach (var call in acalls)
                    {
                        contents.Add(new
                        {
                            functionCall = new
                            {
                                name = call.Name, args = JObject.Parse(call.Arguments)
                            }
                        });
                    }
                    msgs.Add(new Message() { role = "model", parts = contents.ToArray() });
                    contents.Clear();
                    foreach (var call in acalls)
                    {
                        contents.Add(new
                        {
                            functionResponse = new
                            {
                                name = call.Name, response = new
                                {
                                    name = call.Name, content = call.Result.ToString()
                                }
                            }
                        });
                    }
                    msgs.Add(new Message() { role = "function", parts = contents.ToArray() });
                }
            }
        }

        var functions = _functionRepository.GetFunctionList(input.WithFunctions);
        object tools = functions == null || functions.Count == 0
            ? new object[]
            {
                new { google_search = new { } }
            }
            : new object[]
            {
                new { function_declarations = functions }
            };
            
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            contents = msgs,
            tools = useFunctions && string.IsNullOrEmpty(input.CachedContentId) ? tools : null, //使用缓存的时候不能使用tools和系统提示，如果需要的话，tools也要放到缓存里去, 002版本暂不支持
            systemInstruction = useSystem && string.IsNullOrEmpty(input.CachedContentId) ? sys : null,
            cachedContent = input.CachedContentId,
            generationConfig = new
            {
                temperature = input.Temprature,
                maxOutputTokens = _maxTokens,
                response_modalities = canGenImage? new[] { "Text", "Image" } : null,
            }
        }, jSetting);
    }
    
    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var msg = GetMsgBody(input);
        var url = _chatUrl + _modelName + ":streamGenerateContent?alt=sse&key=" + _key;
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        using (var stream = await response.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                //Console.WriteLine(line);
                yield return Result.Error(line);
                yield break;
            }

            List<FunctionCall> functionCalls = new List<FunctionCall>();
            List<Result> results = new List<Result>();
            var sb = new StringBuilder();
            var hasImage = false;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                //Console.WriteLine(line);
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);
                else
                {
                    continue;
                }
                line = line.TrimStart();

                if (line == "[DONE]")
                {
                    break;
                }
                else if (line.StartsWith(":"))
                {
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var o = JObject.Parse(line);
                    if (o["candidates"] != null && o["candidates"][0]["content"]!=null && o["candidates"][0]["content"]["parts"]!=null)
                    {
                        var arr = o["candidates"][0]["content"]["parts"] as JArray;
                        bool textSent = false; //同一次返回的数组里面包含两个text段的时候，说明前面一个是thinking过程，后面一个是正式回答，中间加两个回车
                        foreach (var tk in arr)
                        {
                            if (tk["text"] != null)
                            {
                                if (textSent)
                                {
                                    yield return Result.New(ResultType.AnswerFinished);
                                    results.Add(Result.Answer(sb.ToString()));
                                    sb.Clear();
                                }
                                yield return Result.Answer(tk["text"].Value<string>());
                                sb.Append(tk["text"].Value<string>());
                                textSent = true;
                            }else if (tk["functionCall"] != null)
                            {
                                functionCalls.Add(new FunctionCall(){Name = tk["functionCall"]["name"].Value<string>(), Arguments = tk["functionCall"]["args"].ToString()});
                            }else if (tk["inlineData"] != null)
                            {
                                if (sb.Length > 0)
                                {
                                    yield return Result.New(ResultType.AnswerFinished);
                                    results.Add(Result.Answer(sb.ToString()));
                                    sb.Clear();
                                }
                                var mimeType =  tk["inlineData"]["mimeType"].Value<string>();
                                if (mimeType.Contains("image"))
                                {
                                    var fr = FileResult.Answer(Convert.FromBase64String(tk["inlineData"]["data"].Value<string>()), "png", ResultType.ImageBytes);
                                    yield return fr;
                                    results.Add(fr);
                                    hasImage = true;
                                }
                            }
                        }
                    }
                }
            }

            if (functionCalls.Count>0)
            {
                yield return FunctionsResult.Answer(functionCalls);
            }
            if (sb.Length > 0)
            {
                results.Add(Result.Answer(sb.ToString()));
            }

            if (hasImage || results.Count > 1)
            {
                yield return MultiMediaResult.Answer(results);
            }
        }
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var msg = GetMsgBody(input);
        var url = _chatUrl + _modelName + ":generateContent?key=" + _key;
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        JObject json;
        if (content.StartsWith("["))
        {
            var jarr = JArray.Parse(content);
            json = jarr[0] as JObject;
        }
        else
        {
            json = JObject.Parse(content);
        }

        if (json["candidates"] != null)
        {
            var parts = json["candidates"][0]["content"]["parts"][0];
            if (parts["text"] != null)
            {
                return Result.Answer(parts["text"].Value<string>());
            }
            else if (parts["functionCall"] != null)
            {
                return FunctionsResult.Answer(new List<FunctionCall>()
                {
                    new FunctionCall() {Name = parts["functionCall"]["name"].Value<string>(), Arguments = parts["functionCall"]["args"].ToString()}
                });
            }
            else
            {
                return Result.Error(content);
            }
        }
        else
            return Result.Error(content);
    }

    public async Task<(string mimeType, string uri)> UploadMediaFile(byte[] file, string fileName)
    {
        var bin = new ByteArrayContent(file);
        bin.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",  // 注意这里的引号，有些服务器对这个很敏感
            FileName = "\"" + fileName + "\""
        };
        if(fileName.EndsWith(".txt"))
            bin.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        else if(fileName.EndsWith(".pdf"))
            bin.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var boundary = DateTime.Now.Ticks.ToString();
        var formData = new MultipartFormDataContent(boundary);
        formData.Headers.Remove("Content-Type");
        formData.Headers.TryAddWithoutValidation("Content-Type", "multipart/related; boundary=" + boundary);
        formData.Add(bin, "file", fileName);
        
        HttpClient client = _httpClientFactory.CreateClient();
        var url = fileUploadUrl +"?key=" + _key;
        var resp = await client.PostAsync(url, formData);
        var content = await resp.Content.ReadAsStringAsync();
        try
        {
            var json = JObject.Parse(content);
            if (json["file"] != null)
                return (json["file"]["mimeType"].Value<string>(), json["file"]["uri"].Value<string>());
            else
                return ("", content);
        }
        catch (Exception e)
        {
            Console.WriteLine(content);
            return ("", content);
        }
    }

    public async Task<bool> WaitFileStatus(string fileId)
    {
        var url = fileId.Replace("https://generativelanguage.googleapis.com/", _host) +"?key=" + _key;
        HttpClient client = _httpClientFactory.CreateClient();
        int times = 0;
        while (times<120)
        {
            var resp = await client.GetAsync(url);
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["state"] != null)
            {
                var state = json["state"].Value<string>();
                if (state == "ACTIVE")
                    return true;
                else if (state == "FAILED")
                    return false;
                Thread.Sleep(1000);
            }
            else
            {
                Console.Write(content);
                return false;
            }

            times++;
        }

        return false;
    }
    
    
    /// <summary>
    /// 创建缓存
    /// </summary>
    /// <param name="text"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public async Task<Result> CreateCachedContent(string text, string fileUri, string mimeType)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = _host + "v1beta/cachedContents?key=" + _key;
        List<object> contents = new List<object>();
        if(string.IsNullOrEmpty(fileUri))
            contents.Add(new { text = text });
        else
            contents.Add(new
            {
                file_data = new
                {
                    mime_type = mimeType, file_uri = fileUri
                }
            });
        var msg = JsonConvert.SerializeObject(new
        {
            model="models/"+ _modelName,
            contents = new[]
            {
                new
                {
                    role="user", parts = contents.ToArray()
                }
            },
            ttl = "3600s"
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        JObject json = JObject.Parse(content);

        if (json["name"] != null)
        {
            return Result.Answer(json["name"].Value<string>());
        }
        else
            return Result.Error(content);
    }
    
    public async Task DeleteCachedContent(string cachedContentId)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = _host + $"v1beta/{cachedContentId}?key=" + _key;
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));
        var content = await resp.Content.ReadAsStringAsync();
        Console.WriteLine(content);
    }
    
    public class Message
    {
        public string role { get; set; } = string.Empty;
        public object[] parts { get; set; }
    }
}

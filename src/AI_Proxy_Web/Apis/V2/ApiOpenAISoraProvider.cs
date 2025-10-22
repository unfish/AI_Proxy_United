using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using FFMpegCore.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("OpenAI_Sora")]
public class ApiOpenAISoraProvider : ApiOpenAIProvider
{
    public ApiOpenAISoraProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "videos";
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = _modelName.Contains("pro") ? new[]
                {
                    new KeyValuePair<string, string>("横屏", "1280x720"),
                    new KeyValuePair<string, string>("竖屏", "720x1280"),
                    new KeyValuePair<string, string>("横屏HD", "1792x1024"),
                    new KeyValuePair<string, string>("竖屏HD", "1024x1792")
                }:
                new[]
                {
                    new KeyValuePair<string, string>("横屏", "1280x720"),
                    new KeyValuePair<string, string>("竖屏", "720x1280")
                }
            },
            new ExtraOption()
            {
                Type = "时长", Contents = new[]
                {
                    new KeyValuePair<string, string>("4秒", "4"),
                    new KeyValuePair<string, string>("8秒", "8"),
                    new KeyValuePair<string, string>("12秒", "12")
                }
            }
        };
    }

    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);

        HttpResponseMessage resp = null;
        var opt = GetExtraOptions(input.External_UserId);
        var url = _chatUrl;
        if (input.ChatContexts.Contexts.Any(t => t.AC.Any(x => x.Type == ChatType.文本)))
        {
            var lastAc = input.ChatContexts.Contexts.Last(t => t.AC.Any(x => x.Type == ChatType.文本));
            var lastVideo = lastAc.AC.Last(t => t.Type == ChatType.文本).Content;
            url += $"/{lastVideo}/remix";
            var msg = JsonConvert.SerializeObject(new
            {
                prompt = input.ChatContexts.Contexts.Last().QC.Last(t => t.Type == ChatType.文本).Content
            });
            resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(msg, Encoding.UTF8, "application/json")
            });
        }
        else
        {
            var boundary = DateTime.Now.Ticks.ToString("X");
            var content = new MultipartFormDataContent(boundary);
            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);
            var cnt = new StringContent(_modelName);
            content.Add(cnt, "model");
            cnt.Headers.Remove("Content-Disposition");
            cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"model\";");
            cnt = new StringContent(input.ChatContexts.Contexts.Last().QC.LastOrDefault(t => t.Type == ChatType.文本)
                ?.Content ?? "");
            content.Add(cnt, "prompt");
            cnt.Headers.Remove("Content-Disposition");
            cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"prompt\";");
            cnt = new StringContent(opt[0].CurrentValue);
            content.Add(cnt, "size");
            cnt.Headers.Remove("Content-Disposition");
            cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"size\";");
            cnt = new StringContent(opt[1].CurrentValue);
            content.Add(cnt, "seconds");
            cnt.Headers.Remove("Content-Disposition");
            cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"seconds\";");

            var image = input.ChatContexts.Contexts.Last().QC.LastOrDefault(t => t.Type == ChatType.图片Base64);
            if (image != null)
            {
                var bytes = Convert.FromBase64String(image.Content);
                var contentByte = new ByteArrayContent(bytes);
                content.Add(contentByte);
                contentByte.Headers.Remove("Content-Disposition");
                contentByte.Headers.TryAddWithoutValidation("Content-Disposition",
                    $"form-data; name=\"input_reference\";filename=\"{image.FileName}\"" + "");
                contentByte.Headers.Remove("Content-Type");
                contentByte.Headers.TryAddWithoutValidation("Content-Type",
                    (string.IsNullOrEmpty(image.MimeType) ? "image/jpeg" : image.MimeType));
            }

            resp = await client.PostAsync(url, content);
        }

        var response = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(response);
        if (json["id"] != null)
        {
            var videoId = json["id"].Value<string>();
            await foreach (var res in CheckTask(videoId, int.Parse(opt[1].CurrentValue)))
            {
                yield return res;
            }
        }
        else
        {
            yield return Result.Error(response);
        }
    }

    private async IAsyncEnumerable<Result> CheckTask(string videoId, int seconds)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = $"{_chatUrl}/{videoId}";
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["id"] != null)
            {
                var state = json["status"].Value<string>();
                if (state == "queued" || state == "in_progress")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "completed")
                {
                    yield return Result.Waiting("生成完成，正在下载...");
                    url = $"{_chatUrl}/{videoId}/content";
                    resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    url = $"{_chatUrl}/{videoId}/content?variant=thumbnail";
                    resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                    var imageBytes = await resp.Content.ReadAsByteArrayAsync();
                    var thumb = ImageHelper.Compress(imageBytes);
                    yield return VideoFileResult.Answer(bytes, "mp4", "video.mp4", thumb, seconds*1000);
                    yield return Result.Answer(videoId);
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

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("视频接口不支持Query调用");
    }
}
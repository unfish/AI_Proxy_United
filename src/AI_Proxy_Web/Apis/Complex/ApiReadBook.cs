using System.Collections.Concurrent;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.拆书助手, "拆书助手", "上传一本书，自动阅读理解，总结每一章节的摘要，以及全书的摘要，总结完成后你可以继续对书中的内容进行提问，还可以对全书内容自动生成思维导图。支持PDF/EPUB格式。", 195, type: ApiClassTypeEnum.辅助模型, canProcessFile:true, canProcessAudio:true, needLongProcessTime:true, priceIn: 0, priceOut: 0.1)]
public class ApiReadBook:ApiBase
{
    private IServiceProvider _serviceProvider;
    private ReadBookClient _client;
    public ApiReadBook(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ReadBookClient>();
        _serviceProvider = serviceProvider;
    }

    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach(var res in _client.SendMessageStream(input))
            yield return res;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
    }
}

public class ReadBookClient: IApiClient
{
    private IApiFactory _apiFactory;
    private IServiceProvider _serviceProvider;
    public ReadBookClient(IApiFactory apiFactory, IServiceProvider serviceProvider)
    {
        _apiFactory = apiFactory;
        _serviceProvider = serviceProvider;
    }
    private int modelId = (int)M.Gemini小杯;
    private bool useCache = false;
    
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        input.ChatModel = modelId;
        var api = _apiFactory.GetService(modelId);
        var gemini = _serviceProvider.GetRequiredService<GoogleGeminiClient>();
        bool isFirstBookChat = false;
        var q = input.ChatContexts.Contexts.FirstOrDefault()?.QC.FirstOrDefault(t => t.Type == ChatType.文件Bytes);
        if (q != null) //首次进入处理文件上传
        {
            var question = "该文件是一本书的完整内容，请按其中的章节顺序，总结出每个章节的核心内容，忽略序言后序等内容。" +
                           "\n请以JSON格式返回章节内容，您的响应必须是包含 3 个元素的 JSON 对象，对象具有以下架构：\n" +
                           "part: 第几部分，及该部分的标题，如果没有可以忽略该字段。\nchapter: 第几章，及该章的标题。\nsummary:该章的关键内容总结，总结应该尽量简短，控制在50个字以内。\n" +
                           "返回示例：[\n    {\"part\":\"第1部分：XXX\",\"chapter\":\"第1章：XXX\",\"summary\":\"XXX\"}\n]";
            var fileName = q.FileName.ToLower();
            if (fileName.EndsWith(".pdf"))
            {
                var resp = await gemini.UploadMediaFile(q.Bytes, q.FileName);
                var cacheResult = await gemini.CreateCachedContent("", resp.uri, resp.mimeType);
                if (useCache && cacheResult.resultType == ResultType.Answer)
                {
                    q.Content = cacheResult.ToString();
                    q.Type = ChatType.缓存ID;
                    q.Bytes = null;
                }
                else
                {
                    q.Content = resp.uri;
                    q.Type = ChatType.文件Url;
                    q.MimeType = resp.mimeType;
                    q.Bytes = null;
                }
                isFirstBookChat = true;
            }else if (fileName.EndsWith(".txt") || fileName.EndsWith(".epub"))
            {
                var fileContent = await api.ReadFileTextContent(q.Bytes, q.FileName);
                var cacheResult = await gemini.CreateCachedContent(fileContent, "", "");
                if (useCache && cacheResult.resultType == ResultType.Answer)
                {
                    q.Content = cacheResult.ToString();
                    q.Type = ChatType.缓存ID;
                    q.Bytes = null;
                }
                else
                {
                    var resp = await gemini.UploadMediaFile(q.Bytes, q.FileName);
                    q.Content = resp.uri;
                    q.Type = ChatType.文件Url;
                    q.MimeType = resp.mimeType;
                    q.Bytes = null;
                }
                isFirstBookChat = true;
            }
            else if (fileName.EndsWith(".mp4"))
            {
                var resp = await gemini.UploadMediaFile(q.Bytes, q.FileName);
                bool finish = await gemini.WaitFileStatus(resp.uri);
                if (!finish)
                {
                    yield return Result.Error("视频文件处理失败，可能是格式不兼容。");
                    yield break;
                } 
                q.Content = resp.uri;
                q.Type = ChatType.视频Url;
                q.MimeType = resp.mimeType;
                q.Bytes = null;
                question = "请详细分析一下该视频文件内容，描述、分析并总结该视频内容。";
            }
            else
            {
                yield return Result.Error("只支持PDF/EPUB/TXT文件格式");
            }
            input.ChatContexts.AddQuestion(question);
        }

        if (isFirstBookChat)
        {
            yield return Result.Waiting(
                "开始总结全书，接下来将先整理全书目录，然后自动根据目录详细总结整理每一章的内容，直到全部完成。\n在此过程中你可以发送stop或者\"停止\"，AI将在完成当前章节以后停止总结。\n注意，手动停止或出现异常中止后无法重新开始自动总结，但你仍然可以直接对本书内容进行提问。\n因为使用了服务器端缓存机制，会话有效期为60分钟，60分钟后该会话自动失效，不能继续提问。");
            var sb = new StringBuilder();
            await foreach (var res in api.ProcessChat(input))
            {
                if (res.resultType == ResultType.Answer)
                    sb.Append(res.ToString());
                yield return res;
            }

            if (sb.Length == 0)
                yield break;

            var summary = sb.ToString();
            var jsonIndex = summary.IndexOf("```json", StringComparison.Ordinal);
            if (jsonIndex >= 0)
            {
                summary = summary.Substring(jsonIndex + "```json".Length + 1);
                jsonIndex = summary.IndexOf("```", StringComparison.Ordinal);
                if (jsonIndex > 0)
                    summary = summary.Substring(0, jsonIndex);
            }

            JArray? arr = null;
            try
            {
                arr = JArray.Parse(summary);
            }
            catch
            {
                // ignored
            }

            if (arr == null)
            {
                yield return Result.Error("JSON解析错误。");
                input.IgnoreAutoContexts = true;
                yield break;
            }

            foreach (var tk in arr)
            {
                if (ApiBase.CheckStopSigns(input, false))
                {
                    yield return Result.Answer("收到停止指令，停止自动总结。");
                    break;
                }

                input.ChatContexts.AddQuestion(
                    $"[Q]现在请详细的总结{tk["chapter"].Value<string>()}的内容，需要尽量完整的包含该章原文中作者表达的主要观点、结论，以及得出这些结论的论据、证据、数字，和主要推理过程，必要时输出原文内容，输出原文时不超过5段。\n以普通Markdown文本格式返回内容，列表项内容使用数字序号或-横线开头，不要使用*星号格式。");

                await foreach (var res in api.ProcessChat(input))
                {
                    yield return res;
                    if (res.resultType == ResultType.Error)
                    {
                        input.IgnoreAutoContexts = true;
                        yield break;
                    }
                }
            }

            if (ApiBase.CheckStopSigns(input))
            {
                yield return Result.Answer("收到停止指令，停止自动总结。");
            }
            else
            {
                input.ChatContexts.AddQuestion(
                    "[Q]基于以上按章节总结的内容，请重新对全书进行一次完整的内容总结，以读书笔记的形式，尽量保留原书的逻辑与过程，突出作者重点表达的核心思想，并在最后加入作为读者自己对本书的理解与感悟，以及将来如何应用这些知识。\n注意：列表项内容使用数字序号或-横线开头，不要使用*星号。");
                await foreach (var res in api.ProcessChat(input))
                {
                    yield return res;
                }
                yield return Result.Answer("全书总结完成，您可以继续提问，对感兴趣的主题进行更深入的学习。");
            }
        }
        else //不是首次进入
        {
            await foreach (var res in api.ProcessChat(input))
            {
                yield return res;
            }
        }

        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
}

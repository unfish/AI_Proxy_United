using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("SearchAndSummarize")]
public class ApiSearchAndSummarizeProvider : ApiProviderBase
{
    protected IApiFactory _apiFactory;
    public ApiSearchAndSummarizeProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IApiFactory apiFactory):base(configHelper,serviceProvider)
    {
        _apiFactory = apiFactory;
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var ss = _modelName.Split(",");
        int _SearchModel = int.Parse(ss[0]);
        int _SummarizeModel = int.Parse(ss.Length > 1 ? ss[1] : ss[0]);

        var searchApi = _apiFactory.GetApiCommon(_SearchModel);
        var res = await searchApi.ProcessQuery(input);
        if (res.resultType == ResultType.SearchResult)
        {
            var results = ((SearchResult)res).result;
            if (results.Count > 0)
            {
                var sb = new StringBuilder();
                var waitMsgs = new StringBuilder();
                var q = input.ChatContexts.Contexts.Last().QC.First().Content;
                waitMsgs.AppendLine($"正在阅读关于{q}的网页资料：");
                sb.AppendLine("请根据以下参考资料，回答该问题：" +
                              input.ChatContexts.Contexts.Last().QC.Last().Content);
                sb.AppendLine("<refers>");
                foreach (var dto in results)
                {
                    sb.Append(
                        $"<refer><title>{dto.title}</title><url>{dto.url}></url><content>{dto.content}</content></refer>");
                    waitMsgs.AppendLine($"[{dto.title}]({dto.url})");
                }

                sb.AppendLine("</refers>");
                sb.AppendLine(
                    "字数控制在1000-2000字左右，详略得当，关键数据与结论需要保留。请严格遵循参考资料内容，参考资料中找不到对应的答案的直接返回'搜索结果中没有找到对应问题的答案'");
                waitMsgs.AppendLine("");
                yield return Result.Reasoning(waitMsgs.ToString());

                input.ChatContexts = ChatContexts.New(sb.ToString());
                input.Temprature = (decimal)0.2;
                var summarizeApi = _apiFactory.GetApiCommon(_SummarizeModel);
                sb.Clear();
                await foreach (var res2 in summarizeApi.ProcessChat(input))
                {
                    if (res2.resultType == ResultType.Answer)
                    {
                        sb.Append(res2.ToString());
                    }
                    else
                    {
                        yield return res2;
                    }
                }

                if (sb.Length > 0)
                    yield return Result.New(ResultType.FunctionResult, sb.ToString());
                else
                    yield return Result.New(ResultType.FunctionResult, "Error: 出现未知错误。");
            }
            else
            {
                yield return Result.New(ResultType.FunctionResult, "Error: 该关键词没有找到符合条件的搜索结果，请修改关键词并重试。");
            }
        }
        else
        {
            yield return res;
            yield return Result.New(ResultType.FunctionResult, "Error: " + res.ToString());
        }
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
}

using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.搜索摘要, "搜索摘要", "调用Google搜索接口进行搜索，然后对返回的结果通过另一个大模型进行总结摘要，只返回摘要内容。", 192, type: ApiClassTypeEnum.辅助模型, priceIn: 0, priceOut: 0.1)]
public class ApiSearchAndSummarize:ApiBase
{
    private IServiceProvider _serviceProvider;
    public ApiSearchAndSummarize(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private int _SearchModel = (int)M.Google搜索;
    private int _SummarizeModel = (int)M.MiniMax大杯;

    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var apiFactory = _serviceProvider.GetRequiredService<IApiFactory>();
        var searchApi = apiFactory.GetService(_SearchModel);
        var res = await searchApi.ProcessQuery(input);
        if (res.resultType != ResultType.SearchResult)
        {
            searchApi = apiFactory.GetService(M.博查Web搜索);
            res = await searchApi.ProcessQuery(input);
        }

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
                input.IgnoreSaveLogs = true;
                input.IgnoreAutoContexts = true;
                input.Temprature = (decimal)0.2;
                var summarizeApi = apiFactory.GetService(_SummarizeModel);
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

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该接口不支持Query调用");
    }
}

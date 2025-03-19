using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.阿里万相, "通义万相", "阿里通义万相V2 是阿里推出的文本生成图像模型，选定画图的尺寸之后直接输入要画的场景描述，中英文都可以，不超过75个字。", 200, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.16)]
public class ApiAliWanXiang:ApiBase
{
    protected AliQwenClient _client;
    public ApiAliWanXiang(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<AliQwenClient>();
    }
    
    /// <summary>
    /// 使用通义万相来画图
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach (var resp in _client.CreateWanXiangImage(input, AliQwenClient.WanXiangDrawImageType.Default))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 使用通义万相来画图
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.ImageSize))
        {
            var options = _client.GetExtraOptions(input.External_UserId);
            input.ImageSize = options[1].CurrentValue;
            input.ImageStyle = options[0].CurrentValue;
        }
    }
        
    /// <summary>
    /// 输入图片URL，返回图片的Embedding，1536维
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.ImageEmbeddings(qc, embedForQuery);
        return resp;
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

[ApiClass(M.万相海报, "万相海报", "阿里万相 生成海报专用模型。请一次输入四行文字，分别是画图描述词，大标题，副标题，正文，副标题和正文可以省略。", 205, type: ApiClassTypeEnum.画图模型)]
public class ApiAliWanXiangPoster : ApiAliWanXiang
{
    public ApiAliWanXiangPoster(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        var type = AliQwenClient.WanXiangDrawImageType.PosterEnlarge;
        if (input.ChatContexts.Contexts.Last().QC.All(t => t.Type != ChatType.阿里万相扩展参数))
        {
            type = AliQwenClient.WanXiangDrawImageType.Poster;
            var content = input.ChatContexts.Contexts.Last().QC.Last().Content;
            if (content.Split("\n", StringSplitOptions.RemoveEmptyEntries).Length < 2)
            {
                yield return Result.Error("至少需要输入两行文字，第一行画图提示词，第二行是大标题。");
                yield break;
            }
        }

        await foreach (var resp in _client.CreateWanXiangImage(input, type))
        {
            yield return resp;
        }
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetPosterExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetPosterExtraOptions(ext_userId, type, value);
    }
}


[ApiClass(M.万相视频, "万相视频", "阿里万相 生成视频专用模型。", 223, type: ApiClassTypeEnum.视频模型)]
public class ApiAliWanXiangVideo : ApiAliWanXiang
{
    public ApiAliWanXiangVideo(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.CreateWanXiangImage(input, AliQwenClient.WanXiangDrawImageType.Default))
        {
            yield return resp;
        }
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetT2VExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetT2VExtraOptions(ext_userId, type, value);
    }

}
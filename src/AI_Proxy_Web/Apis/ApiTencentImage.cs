using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Apis;


[ApiClass(M.混元画图, "混元画图", "腾讯混元文生图 是腾讯推出的文本生成图像模型，选定画图的风格和尺寸之后直接输入要画的场景描述，中英文都可以，不超过75个字，号称效果超过Stable Diffusion。", 201, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.4)]
public class ApiTencentImage:ApiBase
{
    private TencentClient _client;
    public ApiTencentImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<TencentClient>();
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

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.ImageSize))
        {
            var options = _client.GetExtraOptions(input.External_UserId);
            input.ImageSize = options[1].CurrentValue;
            input.ImageStyle = options[0].CurrentValue;
        }
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


[ApiClass(M.混元3D, "混元3D", "腾讯混元文生3D/图生3D 是腾讯推出的文本或图片生成3D模型，返回obj格式的3D文件。", 215, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.4)]
public class ApiTencent3D:ApiBase
{
    private TencentClient _client;
    public ApiTencent3D(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<TencentClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach (var resp in _client.TextTo3D(input))
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

}

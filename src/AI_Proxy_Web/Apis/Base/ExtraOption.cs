namespace AI_Proxy_Web.Apis.Base;

/// <summary>
/// 有些模型需要用户选择额外参数，比如尺寸，风格，思考模型的深度等等，使用统一类型来定义和返回
/// </summary>
public class ExtraOption
{
    public string Type { get; set; }
    public KeyValuePair<string,string>[] Contents { get; set; }
    public string CurrentValue { get; set; }
}
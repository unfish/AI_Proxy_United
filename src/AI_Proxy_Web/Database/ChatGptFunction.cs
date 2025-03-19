namespace AI_Proxy_Web.Database;

/// <summary>
/// 方法类型，是前端方法还是后端方法，Internal是GPT项目内部的方法，比如ChatGPT可以去调用画图的Api
/// </summary>
public enum FunctionType
{
    Frontend = 0, Backend = 1, Internal = 2
}
public enum CallMethod
{
    GET = 0, POST = 1
}

public class ChatGptFunction
{
    public int Id { get; set; }
    /// <summary>
    /// 对函数分组，可以按整组发起请求
    /// </summary>
    public string GroupName { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Parameters { get; set; }
    public FunctionType FunctionType { get; set; }
    /// <summary>
    /// 如果是后端方法，指定一个调用该方法的接口调用方式
    /// </summary>
    public CallMethod CallMethod { get; set; }
    /// <summary>
    /// 如果是后端方法，指定一个调用该方法的接口调用URL
    /// 如果是GET方法，URL里面可以用{参数名}，调用时替换为GPT解析的对应的参数值
    /// 比如http://www.baidu.com/file/download?file={file}
    /// 如果是POST方法，把GPT解析的参数直接作为Body Post过去
    /// </summary>
    public string? CallUrl { get; set; }
    public bool Disabled { get; set; }
    public DateTime CreatedOn { get; set; }
    /// <summary>
    /// 当有Function回调结果的时候，是否要指定Prompt替换用户输入的Prompt，仅限后端方法
    /// </summary>
    public string? FunctionPrompt { get; set; }
    
    /// <summary>
    /// 触发词，只有当用户输入的词里面包含了这里指定的词的时候才把函数体带上，减少提示词长度
    /// </summary>
    public string? TriggerWords { get; set; }
    
    /// <summary>
    /// 是否直接返回方法调用结果，不再经过AI，函数返回需要符合格式，直接使用body参数的值。仅适用于后端方法。
    /// </summary>
    public bool UseResultDirect { get; set; }
}
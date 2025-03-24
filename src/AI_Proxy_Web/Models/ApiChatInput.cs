namespace AI_Proxy_Web.Models;

/// <summary>
/// GPT处理文字类请求参数
/// </summary>
public record ApiChatInput
{
    /// <summary>
    /// 禁用构造函数，只能通过New的方式创建
    /// </summary>
    private protected ApiChatInput(){}

    public static ApiChatInput New()
    {
        return new ApiChatInputIntern();
    }
    
    public ChatFrom ChatFrom { get; set; }
    public int ChatModel { get; set; }
    
    /// <summary>
    /// 当前提问内容
    /// </summary>
    public List<ChatContext.ChatContextContent> QuestionContents { get; set; } =
        new List<ChatContext.ChatContextContent>();

    /// <summary>
    /// 指定需要附带的Function名称
    /// </summary>
    public string[]? WithFunctions { get; set; }

    /// <summary>
    /// 宪章userid，用来传递当前用户身份，有AccountId取AccountId，没有就取飞书User_Id
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 请求模型的温度参数
    /// </summary>
    private decimal temprature { get; set; }
    public decimal Temprature {
        get
        {
            return temprature <= 0 ? (decimal)0.6 : temprature;
        }
        set
        {
            temprature = value;
        } 
    }

    /// <summary>
    /// 宪章的用户Token，用于调用后端接口
    /// </summary>
    public string UserToken { get; set; } = string.Empty;
    
    /// <summary>
    /// 外部UserID，比如飞书userid或者企微userid, 网页端需要设成token，同一个External_UserId的上下文会相互覆盖，所以只允许一个并发
    /// </summary>
    public string External_UserId { get; set; } = string.Empty;

    /// <summary>
    /// 是否忽略自动加载和保存上下文，有些类需要掌控上下文保存过程
    /// </summary>
    public bool IgnoreAutoContexts { get; set; } = false;
    
    /// <summary>
    /// 是否忽略自动保存日志，需要并发异步调用的地方都需要忽略
    /// </summary>
    public bool IgnoreSaveLogs { get; set; } = false;
    
    /// <summary>
    /// 用来保存和读取Context的缓存Key，解决跨应用上下文冲突的问题
    /// </summary>
    public string ContextCachePrefix { get; set; } = string.Empty;
    
    /// <summary>
    /// 画图模型使用的样式
    /// </summary>
    public string ImageStyle { get; set; } = string.Empty;
    
    /// <summary>
    /// 画图模型使用的尺寸
    /// </summary>
    public string ImageSize { get; set; } = string.Empty;
    
    /// <summary>
    /// 文字转语音模型使用的语音格式
    /// </summary>
    public string AudioFormat { get; set; } = string.Empty;
    
    /// <summary>
    /// 文字转语音的声音选项
    /// </summary>
    public string AudioVoice { get; set; } = string.Empty;

    /// <summary>
    /// Midjourney的画图通道选项
    /// </summary>
    public string MidJourneyTunnel { get; set; } = "FAST"; //NORMAL或FAST

    /// <summary>
    /// 向量化的目的，是为了索引还是为了搜索，大部分接口不区分
    /// </summary>
    public bool EmbedForQuery { get; set; } = false;

    /// <summary>
    /// 缓存内容ID，用于支持缓存的模型接口(Gemini)，对大量重复的请求内容可以加快响应速度
    /// </summary>
    public string? CachedContentId { get; set; }

    /// <summary>
    /// 当前显示器的宽度
    /// </summary>
    public int? DisplayWidth { get; set; }
    /// <summary>
    /// 当前显示器的高度
    /// </summary>
    public int? DisplayHeight { get; set; }
    
    public string AgentSystem { get; set; } //当使用computer use功能时，面对的是什么系统，web, mac, windows
}

/// <summary>
/// API内部实际使用的参数，自行维护上下文对话，外部入口只负责传入最新的一条对话问题，如果外部需要自己控制上下文历史可以直接访问这个类
/// </summary>
public record ApiChatInputIntern : ApiChatInput
{
    /// <summary>
    /// 历史上下文对话内容，仅限Api内部使用，自动维护上下文
    /// </summary>
    public ChatContexts? ChatContexts { get; set; }

    /// <summary>
    /// Input在通过内部Function递归调用的时候，记录当前递归的层级
    /// </summary>
    public int RecursionLevel { get; set; } = 0;
    
    public List<KeyValuePair<string, string>>? AgentResults { get; set; } //向内部函数传递的跨Agent上下文信息
    public AgentInfo? Agent { get; set; } //当前Agent的信息

    public class AgentInfo
    {
        public string Skill { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Task { get; set; } = string.Empty;
    }
}
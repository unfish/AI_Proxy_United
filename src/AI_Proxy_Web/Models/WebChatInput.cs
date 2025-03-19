namespace AI_Proxy_Web.Models;

public class WebChatInput
{
    public ChatFrom ChatFrom { get; set; }
    
    public int ChatModel { get; set; }

    public string Question { get; set; } = string.Empty;
    
    //是否附带上下文对话内容，服务器端处理上下文
    public bool WithContext { get; set; } = false;
    
    //前台调用的时候指定需要附带的Function名称
    public string[]? WithFunctions { get; set; }
    
    public string? WithPromptKey { get; set; }

    /// <summary>
    /// 请求模型的温度参数
    /// </summary>
    public decimal? Temprature { get; set; } = null;
    
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// 用户指定的虚拟上下文，需要使用Q:xxx A:xxx的问答对的形式提供
    /// </summary>
    public string? VirtualContexts { get; set; }
    
    public bool WithVoice { get; set; } = false; //是否返回语音
    public bool WithFullVoice { get; set; } = false; //是否返回完整语音，流式接口默认为流式返回
    public string? WithVoiceId { get; set; }
    public string WithVoiceFormat { get; set; } = "mp3";
    
    /// <summary>
    /// 当前显示器的宽度
    /// </summary>
    public int? DisplayWidth { get; set; }
    /// <summary>
    /// 当前显示器的高度
    /// </summary>
    public int? DisplayHeight { get; set; }

    private static ChatFrom[] csTypes = new[] {ChatFrom.Api, ChatFrom.云文档, ChatFrom.小程序};

    public static bool IsFromSite(ChatFrom chatFrom)
    {
        return csTypes.Contains(chatFrom);
    }
}
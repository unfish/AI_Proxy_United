namespace AI_Proxy_Web.Models;

public class WebEmbeddingsInput
{
    public ChatFrom ChatFrom { get; set; }
    
    public int ChatModel { get; set; }

    public bool ForQuery { get; set; } = false;
    public string Question { get; set; } = string.Empty;
    public string[] Questions { get; set; } = Array.Empty<string>();
}
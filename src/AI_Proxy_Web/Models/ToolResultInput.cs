namespace AI_Proxy_Web.Models;

public class ToolResultInput
{
    public ChatFrom ChatFrom { get; set; }
    public int ChatModel { get; set; } = -1;
    public ToolResult[] ToolResults { get; set; }
    
    public class ToolResult
    {
        public string tool_id { get; set; }
        public ToolResultTypeEnum result_type { get; set; }
        public string content { get; set; }
        public string? mime_type { get; set; }
    }
    
    public enum ToolResultTypeEnum
    {
        Text = 0,
        Image = 1,
    }
}
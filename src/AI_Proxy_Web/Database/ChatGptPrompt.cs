using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Database;

public class ChatGptPrompt
{
    public int Id { get; set; }
    public string Key { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
    public string Prompt { get; set; }
    public PromptTemplate.PromptType Type { get; set; }
    public int SortFeed { get; set; }
    public bool Disabled { get; set; }
    public string GroupName { get; set; }
}
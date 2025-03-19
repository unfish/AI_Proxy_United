using Newtonsoft.Json.Converters;

namespace AI_Proxy_Web.Models;

public class PromptTemplate
{
    public string Name { get; set; }
    public string Label { get; set; }
    public string Content { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
    public PromptType Type { get; set; }
    public enum PromptType
    {
        Prompt, Tips
    }
    public string GroupName { get; set; }
}
namespace AI_Proxy_Web.Helpers;

public class SSEEvent
{
    public string Name { get; set; }
    public object Data { get; set; }

    public SSEEvent(string name, object data)
    {
        Name = name;
        Data = data;
    }
}
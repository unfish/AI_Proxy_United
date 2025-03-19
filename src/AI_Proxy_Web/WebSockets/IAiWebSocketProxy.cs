using System.Collections.Concurrent;
using AI_Proxy_Web.Apis.Base;

namespace AI_Proxy_Web.WebSockets;

/// <summary>
/// 定义后端处理websocket响应的接口，处理前端请求并返回消息，其它类来具体实现
/// </summary>
public interface IAiWebSocketProxy
{
    Task ConnectAsync(BlockingCollection<object> messageQueue, string extraParams="");
    void Wait();
    public event EventHandler<Result> OnMessageReceived;
    public event EventHandler OnProxyDisconnect;
}
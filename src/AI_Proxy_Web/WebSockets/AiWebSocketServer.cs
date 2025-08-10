using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Apis.V2;
using Newtonsoft.Json;
using WebSocket = System.Net.WebSockets.WebSocket;
using WebSocketState = System.Net.WebSockets.WebSocketState;

namespace AI_Proxy_Web.WebSockets;

/// <summary>
/// 接收前端进来的websocket请求并进行应答
/// </summary>
public class AiWebSocketServer
{
    private WebSocket _webSocket;
    private IAiWebSocketProxy _socketProxy;

    private BlockingCollection<object> _messageQueue;
    private CancellationTokenSource _cancellationTokenSource;

    private bool finishMessageSended = false;
    public const string finishMessage = "{\"type\": \"end\"}";
    public AiWebSocketServer(WebSocket socket, IServiceProvider serviceProvider, IApiFactory apiFactory, string server, string provider = "tencent")
    {
        _webSocket = socket;

        if (server == "asr")
        {
            if(provider=="doubao")
                _socketProxy = (ApiDoubaoSSRV3Client)apiFactory.GetApiCommon("DoubaoSSR").ApiProvider;
            else if(provider=="openai")
                _socketProxy = (ApiOpenAIStreamProvider)apiFactory.GetApiCommon("OpenAIStream").ApiProvider;
            else
                _socketProxy = (ApiTencentSSRProvider)apiFactory.GetApiCommon("TencentSSR").ApiProvider;
        }
        else
            throw new Exception("目前只支持asr服务");
        
        _messageQueue = new BlockingCollection<object>();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task ProcessAsync(string extraParams="")
    {
        //在异步任务中收到后端数据时转发给前端
        _socketProxy.OnMessageReceived += async (sender, result) =>
        {
            await SendToClientAsync(JsonConvert.SerializeObject(new
            {
                resultType = result.resultType.ToString(), result = result.ToString()
            }));
        };
        //后端连接断开的处理
        _socketProxy.OnProxyDisconnect += async (sender, args) =>
        {
            await DisconnectByProxy();
        };
        await _socketProxy.ConnectAsync(_messageQueue, extraParams); //启动后端socket连接的Task
        
        //SendTestMessage("hello, what day is today.");
        await ReceiveMessagesFromClientAsync(); //持续接收前端数据
        _socketProxy.Wait(); //等待内部Task全部结束
    }

    private async Task ReceiveMessagesFromClientAsync()
    {
        try
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 4]);
            while (_webSocket.State == WebSocketState.Open &&
                   !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                var bytes = new List<byte>();
                do
                {
                    result =
                        await _webSocket.ReceiveAsync(buffer, _cancellationTokenSource.Token);
                    if(result.Count>0)
                        bytes.AddRange(buffer.Array[..result.Count]);
                }while(!result.EndOfMessage);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await DisconnectByClient();
                }
                else
                {
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(bytes.ToArray());
                        _messageQueue.Add(message);
                        if (message == finishMessage)
                        {
                            finishMessageSended = true;
                            _messageQueue.CompleteAdding();
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        _messageQueue.Add(bytes.ToArray());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ex:"+ ex.Message);
        }
    }
    
    private async Task SendToClientAsync(string message)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(buffer);
        if(_webSocket.State== WebSocketState.Open || _webSocket.State== WebSocketState.CloseReceived)
            await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task DisconnectByProxy()
    {
        if(!_messageQueue.IsAddingCompleted)
            _messageQueue.CompleteAdding();
        if (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }catch{}
        }
        _webSocket.Dispose();
    }

    private async Task DisconnectByClient()
    {
        if (!_messageQueue.IsAddingCompleted)
        {
            //前端断开连接前如果没有收到结束指令，需要补上，让后端正常断开
            if(!finishMessageSended)
                _messageQueue.Add(finishMessage);
            _messageQueue.CompleteAdding();
        }
    }

    public void SendTestMessage(string msg)
    {
        _messageQueue.Add(msg);
    }
}
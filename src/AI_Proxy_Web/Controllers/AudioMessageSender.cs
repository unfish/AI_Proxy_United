using System.Collections.Concurrent;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Feishu;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;

namespace AI_Proxy_Web.Controllers;

public class AudioMessageSender
{
    private readonly BlockingCollection<string> _dataQueue = new BlockingCollection<string>();
    private readonly BlockingCollection<string> _audioQueue = new BlockingCollection<string>();
    private readonly HttpContext _httpContext;
    private readonly ApiBase _apiAudio;
    public AudioMessageSender(HttpContext httpContext, IApiFactory apiFactory)
    {
        _httpContext = httpContext;
        _apiAudio = apiFactory.GetApiCommon("AudioService");
    }

    private Task? taskA = null;
    private Task? taskS = null;
    public void Start(string withVoiceId, string withVoiceFormat, string exUserId)
    {
        taskA = Task.Run(async () =>
        {
            foreach (var data in _audioQueue.GetConsumingEnumerable())
            {
                if (!string.IsNullOrEmpty(data))
                {
                    await _httpContext.SSESendChatEventAsync(
                        JsonConvert.SerializeObject(new ResultDto()
                            {resultType = ResultType.AudioBytes.ToString(), result = data}));
                }
            }
        });
        taskS = Task.Run(async () =>
        {
            foreach (var data in _dataQueue.GetConsumingEnumerable())
            {
                if (!string.IsNullOrEmpty(data))
                {
                    var t2sInput = ApiChatInput.New() with
                    {
                        QuestionContents = ChatContext.NewContentList(data), AudioVoice = withVoiceId,
                        AudioFormat = withVoiceFormat, External_UserId = exUserId, IgnoreAutoContexts = true, IgnoreSaveLogs = true
                    };
                    await foreach (var res2 in _apiAudio.ProcessChat(t2sInput))
                    {
                        if (res2.resultType == ResultType.AudioBytes)
                        {
                            this.AddAudio(res2.ToString());
                        }
                    }
                }
            }
            _audioQueue.CompleteAdding();
        });
    }

    private void AddAudio(string data)
    {
        _audioQueue.Add(data);
    }
    
    public void AddAnswer(string data)
    {
        _dataQueue.Add(data);
    }

    public void Wait()
    {
        Task.WaitAll(new Task[] { taskA, taskS });
    }

    public void Finish()
    {
        _dataQueue.CompleteAdding();
    }
}
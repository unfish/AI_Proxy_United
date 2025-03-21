using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.语音服务, "语音服务", "提供输入文字转语音服务，和输入语音转文字服务。多种接口和音色可选。在飞书里只作为选择音色使用，不要直接对该服务发文字消息。", 300, type: ApiClassTypeEnum.辅助模型, canProcessAudio:true)]
public class ApiAudio:ApiBase
{
    private IAudioService _audioService;
    public ApiAudio(IAudioService audioService, IServiceProvider serviceProvider):base(serviceProvider)
    {
        _audioService = audioService;
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC.First();
        if (qc.Type == ChatType.文本)
        {
            await foreach(var res in _audioService.TextToVoiceStream(input))
                yield return res;
        }else if (qc.Type == ChatType.文件Bytes)
        {
            var resp = await _audioService.VoiceToText(qc.Bytes, qc.FileName); //发送语音转文字
            yield return resp;
        }
        else
        {
            yield return Result.Error("只能接受纯文本或语音输入实现双向互转");
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC.First();
        if (qc.Type == ChatType.文本)
        {
            var resp = await _audioService.TextToVoice(qc.Content, input.AudioVoice, audioFormat: input.AudioFormat, input.External_UserId); //发送文字转语音
            return resp;
        }
        else if (qc.Type == ChatType.文件Bytes)
        {
            var resp = await _audioService.VoiceToText(qc.Bytes, qc.FileName); //发送语音转文字
            return resp;
        }
        else
        {
            return Result.Error("只能接受纯文本或语音输入实现双向互转");
        }
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.AudioVoice))
            input.AudioVoice = ((AudioService)_audioService).GetExtraOptions(input.External_UserId)[0].CurrentValue;
        input.IgnoreAutoContexts = true;
        input.IgnoreSaveLogs = true;
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return ((AudioService)_audioService).GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        ((AudioService)_audioService).SetExtraOptions(ext_userId, type, value);
    }
}
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Feishu;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Concentus.Oggfile;
using Concentus.Structs;
using FFMpegCore;
using NAudio.Wave;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("AudioService")]
public class ApiAudioServiceProvider : ApiProviderBase
{
    protected IApiFactory _apiFactory;
    public ApiAudioServiceProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IApiFactory apiFactory):base(configHelper,serviceProvider)
    {
        _apiFactory = apiFactory;
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr); 
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "声音风格", Contents = new []
                {
                    new KeyValuePair<string, string>("女-成熟", "minimax_female-chengshu-jingpin"),
                    new KeyValuePair<string, string>("女-甜美", "minimax_female-tianmei-jingpin"),
                    new KeyValuePair<string, string>("女-新闻", "minimax_presenter_female"),
                    new KeyValuePair<string, string>("男-精英", "minimax_male-qn-jingying-jingpin"),
                    new KeyValuePair<string, string>("男-霸道", "minimax_male-qn-badao-jingpin"),
                    new KeyValuePair<string, string>("男-新闻", "minimax_presenter_male"),
                    new KeyValuePair<string, string>("女-智聆", "tencent_101002"),
                    new KeyValuePair<string, string>("女-智美", "tencent_101003"),
                    new KeyValuePair<string, string>("女-智芸", "tencent_101009"),
                    new KeyValuePair<string, string>("女-智丹", "tencent_101012"),
                    new KeyValuePair<string, string>("男-智华", "tencent_101010"),
                    new KeyValuePair<string, string>("男-智辉", "tencent_101013"),
                    new KeyValuePair<string, string>("男-智皓", "tencent_101024"),
                    new KeyValuePair<string, string>("男-智靖", "tencent_101018")
                }
            }
        };
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.AudioVoice))
            input.AudioVoice = GetExtraOptions(input.External_UserId)[0].CurrentValue;
        input.IgnoreAutoContexts = true;
        input.IgnoreSaveLogs = true;
    }

    
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC.First();
        if (qc.Type == ChatType.文本)
        {
            await foreach(var res in TextToVoiceStream(input))
                yield return res;
        }else if (qc.Type == ChatType.文件Bytes)
        {
            var resp = await VoiceToText(qc.Bytes, qc.FileName); //发送语音转文字
            yield return resp;
        }
        else
        {
            yield return Result.Error("只能接受纯文本或语音输入实现双向互转");
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC.First();
        if (qc.Type == ChatType.文本)
        {
            var resp = await TextToVoice(qc.Content, input.AudioVoice, audioFormat: input.AudioFormat, input.External_UserId); //发送文字转语音
            return resp;
        }
        else if (qc.Type == ChatType.文件Bytes)
        {
            var resp = await VoiceToText(qc.Bytes, qc.FileName); //发送语音转文字
            return resp;
        }
        else
        {
            return Result.Error("只能接受纯文本或语音输入实现双向互转");
        }
    }
    
    
    private static object lockObj = new object();

    public async Task<Result> VoiceToText(byte[]  bytes, string fileName)
    {
        switch (_modelName)
        {
            case "feishu":
            {
                var feishuService = serviceProvider.GetRequiredService<IFeishuService>();
                return feishuService.VoiceToText(bytes, fileName);
            }
            default:
            {
                var tencent = serviceProvider.GetRequiredService<ApiTencentProvider>();
                return await tencent.VoiceToText(bytes, fileName);
            }
        }
    }
    
    public async Task<Result> TextToVoice(string text, string voiceName, string audioFormat = "mp3", string user_id = "")
    {
        if (string.IsNullOrEmpty(voiceName))
            voiceName = GetExtraOptions(user_id)[0].CurrentValue;
        
        if (voiceName.StartsWith("minimax_"))
        {
            var mmax = (ApiMiniMaxProvider)_apiFactory.GetApiCommon("MiniMax").ApiProvider;
            return await mmax.TextToVoice(text, voiceName, audioFormat);
        }
        else if (voiceName.StartsWith("tencent_"))
        {
            var tencent = (ApiTencentProvider)_apiFactory.GetApiCommon("TencentHunYuan").ApiProvider;
            if (text.Length > 150)
                return await tencent.LongTextToVoice(text, voiceName, audioFormat);
            else
                return await tencent.TextToVoice(text, voiceName, audioFormat);
        }else
        {
            var mmax = (ApiDoubaoProvider)_apiFactory.GetApiCommon("Doubao").ApiProvider;
            return await mmax.TextToVoice(text, voiceName, audioFormat);
        }
    }

    public async IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.AudioVoice))
            input.AudioVoice = GetExtraOptions(input.External_UserId)[0].CurrentValue;
        if (input.AudioVoice.StartsWith("minimax_"))
        {
            var mmax = (ApiMiniMaxProvider)_apiFactory.GetApiCommon("MiniMax").ApiProvider;
            await foreach (var resp in mmax.TextToVoiceStream(input))
                yield return resp;
        }
        else if (input.AudioVoice.StartsWith("doubao_"))
        {
            var doubao = (ApiDoubaoProvider)_apiFactory.GetApiCommon("Doubao").ApiProvider;
            await foreach (var resp in doubao.TextToVoiceStream(input))
                yield return resp;
        }
    }
    
    public static byte[] ConvertOpusToWav(byte[] oggFile, string user_id)
    {
        var fileWav = $"./{user_id}_audio.wav";
        using (MemoryStream fileIn = new MemoryStream(oggFile))
        {
            using (MemoryStream pcmStream = new MemoryStream())
            {
                OpusDecoder decoder = OpusDecoder.Create(48000, 1);
                OpusOggReadStream oggIn = new OpusOggReadStream(decoder, fileIn);
                while (oggIn.HasNextPacket)
                {
                    short[] packet = oggIn.DecodeNextPacket();
                    if (packet != null)
                    {
                        for (int i = 0; i < packet.Length; i++)
                        {
                            var bytes = BitConverter.GetBytes(packet[i]);
                            pcmStream.Write(bytes, 0, bytes.Length);
                        }
                    }
                }

                pcmStream.Position = 0;
                var wavStream = new RawSourceWaveStream(pcmStream, new WaveFormat(48000, 1));
                var sampleProvider = wavStream.ToSampleProvider();
                WaveFileWriter.CreateWaveFile16(fileWav, sampleProvider);
                var file = File.ReadAllBytes(fileWav);
                File.Delete(fileWav);
                return file;
            }
        }
    }
    
    /// <summary>
    /// 调用容器里安装的ffmpeg，做任意语音格式转换
    /// </summary>
    /// <param name="mp3File"></param>
    /// <param name="sourceFormat"></param>
    /// <param name="targetFormat"></param>
    /// <param name="user_id"></param>
    /// <returns></returns>
    public static byte[] ConvertAudioFormat(byte[] mp3File, string sourceFormat, string targetFormat, string user_id)
    {
        lock (lockObj)
        {
            var fileWav = $"./{user_id}_audio." + sourceFormat;
            var fileOpus = $"./{user_id}_audio." + targetFormat;
            File.WriteAllBytes(fileWav, mp3File);
            FFMpegArguments.FromFileInput(fileWav)
                .OutputToFile(fileOpus, true, options => options.ForceFormat(targetFormat))
                .ProcessSynchronously();
            var file = File.ReadAllBytes(fileOpus);
            File.Delete(fileWav);
            File.Delete(fileOpus);
            return file;
        }
    }
}
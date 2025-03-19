using System.Text;
using AI_Proxy_Web.Feishu;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Concentus.Oggfile;
using Concentus.Structs;
using FFMpegCore;
using NAudio.Wave;

namespace AI_Proxy_Web.Apis.Base;

/// <summary>
/// 文本转语音公共服务，可以提供多家的服务可选
/// </summary>
public interface IAudioService
{
    Task<Result> VoiceToText(byte[]  bytes, string fileName);
    Task<Result> TextToVoice(string text, string voiceName, string audioFormat);

    IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input);
}
public class AudioService: IAudioService
{
    private IHttpClientFactory _httpClientFactory;
    private IServiceProvider _serviceProvider;
    public AudioService(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
    }

    private static object lockObj = new object();

    public async Task<Result> VoiceToText(byte[]  bytes, string fileName)
    {
        var defaultModel = "tencent";
        switch (defaultModel)
        {
            case "openai":
            {
                var configuration = _serviceProvider.GetRequiredService<ConfigHelper>();
                var openAIAPIKEY = configuration.GetConfig<string>("OpenAI:Key");
                var openAIHostUrl = configuration.GetConfig<string>("OpenAI:Host");
                var openai = _serviceProvider.GetRequiredService<OpenAIClient>();
                openai.Setup(openAIHostUrl, openAIAPIKEY);
                return await openai.VoiceToText(bytes, fileName);
            }
            case "feishu":
            {
                var feishuService = _serviceProvider.GetRequiredService<IFeishuService>();
                return feishuService.VoiceToText(bytes, fileName);
            }
            default:
            {
                var tencent = _serviceProvider.GetRequiredService<TencentClient>();
                return await tencent.VoiceToText(bytes, fileName);
            }
        }
    }
    
    public async Task<Result> TextToVoice(string text, string voiceName = "minimax_male-qn-jingying-jingpin", string audioFormat = "mp3")
    {
        if (voiceName.StartsWith("minimax_"))
        {
            var mmax = _serviceProvider.GetRequiredService<MiniMaxClient>();
            return await mmax.TextToVoice(text, voiceName, audioFormat);
        }
        else if (voiceName.StartsWith("tencent_"))
        {
            var tencent = _serviceProvider.GetRequiredService<TencentClient>();
            if (text.Length > 150)
                return await tencent.LongTextToVoice(text, voiceName, audioFormat);
            else
                return await tencent.TextToVoice(text, voiceName, audioFormat);
        }else if (voiceName.StartsWith("doubao_"))
        {
            var mmax = _serviceProvider.GetRequiredService<DoubaoClient>();
            return await mmax.TextToVoice(text, voiceName, audioFormat);
        }
        else
        {
            var configuration = _serviceProvider.GetRequiredService<ConfigHelper>();
            var openAIAPIKEY = configuration.GetConfig<string>("OpenAI:Key");
            var openAIHostUrl = configuration.GetConfig<string>("OpenAI:Host");
            var openai = _serviceProvider.GetRequiredService<OpenAIClient>();
            openai.Setup(openAIHostUrl, openAIAPIKEY);
            return await openai.TextToVoice(text, voiceName, audioFormat);
        }
    }

    public async IAsyncEnumerable<Result> TextToVoiceStream(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.AudioVoice))
            input.AudioVoice = "minimax_male-qn-jingying-jingpin";
        if (input.AudioVoice.StartsWith("minimax_"))
        {
            var mmax = _serviceProvider.GetRequiredService<MiniMaxClient>();
            await foreach (var resp in mmax.TextToVoiceStream(input))
                yield return resp;
        }
        else if (input.AudioVoice.StartsWith("spark_"))
        {
            var spark = _serviceProvider.GetRequiredService<XfSparkClient>();
            await foreach (var resp in spark.TextToVoiceStream(input))
                yield return resp;
        }
        else if (input.AudioVoice.StartsWith("doubao_"))
        {
            var doubao = _serviceProvider.GetRequiredService<DoubaoClient>();
            await foreach (var resp in doubao.TextToVoiceStream(input))
                yield return resp;
        }
    }
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
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
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
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
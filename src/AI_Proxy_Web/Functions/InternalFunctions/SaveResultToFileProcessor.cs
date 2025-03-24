using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("SaveResultToFile")]
public class SaveResultToFileProcessor: BaseProcessor
{
    public SaveResultToFileProcessor(IApiFactory factory) : base(factory)
    {
    }

    private string _funcArgs;

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        _funcArgs = funcArgs;
    }
    
    private void SaveFile(string filename, string content)
    {
        if (!Path.Exists(Path.GetDirectoryName(filename)))
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
        File.AppendAllText(filename, content);
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessResult(FunctionCall func, ApiChatInputIntern input, ApiChatInputIntern callerInput, bool reEnter = false)
    {
        var arg = JObject.Parse(_funcArgs);
        var role = arg["role"].Value<string>();
        var title = arg["title"].Value<string>();
        var file = arg["filename"].Value<string>();
        bool success = false;
        if (input.AgentResults!=null && input.AgentResults.Count > 0)
        {
            for (var i = input.AgentResults.Count - 1; i >= 0; i--)
            {
                if (input.AgentResults[i].Key == role)
                {
                    var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_files/"+input.External_UserId + "/",
                        file);
                    SaveFile(fullPath, title + "\n\n" + input.AgentResults[i].Value + "\n\n");
                    var bytes = File.ReadAllBytes(fullPath);
                    yield return FileResult.Answer(bytes, Path.GetExtension(fullPath), ResultType.FileBytes,
                        Path.GetFileName(fullPath));
                    success = true;
                    break;
                }
            }
        }

        yield return Result.New(ResultType.FunctionResult, success ? "文件写入完成，并且已经将文件发送给用户。" : "文件写入出错，没有找到对应的角色的报告结果。");
    }
}
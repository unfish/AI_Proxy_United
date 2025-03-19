namespace AI_Proxy_Web.Controllers;

public class ResultDto
{
    public string resultType { get; set; }
    public string result { get; set; }
    //用来放额外返回的消息，比如多function，或文本转语音的内容
    public List<ResultDto> extraResults { get; set; }
}

public class EmbeddingsDto
{
    public string resultType { get; set; }
    public string error { get; set; }
    public double[][] results { get; set; }
    public double[] result { get; set; }
}
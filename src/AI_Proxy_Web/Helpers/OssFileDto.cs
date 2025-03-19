namespace AI_Proxy_Web.Helpers;

public class OssFileDto
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public string ContentType { get; set; }
    public string ContentMD5 { get; set; }
}
namespace AI_Proxy_Web.Helpers;

public interface IOssFileService
{

    /// <summary>
    /// 上传文件到OSS
    /// </summary>
    /// <param name="originName"></param>
    /// <param name="inputStream"></param>
    /// <param name="userId"></param>
    /// <param name="useOriginName"></param>
    /// <returns></returns>
    OssFileDto UploadFile(string originName, Stream inputStream, int userId, bool useOriginName = false);
    
    /// <summary>
    /// 根据后缀名获取mime-type
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    string GetFileContentType(string filename);

    /// <summary>
    /// 读取指定的OSS文件并直接返回文件流
    /// </summary>
    /// <param name="file"></param>
    /// <param name="resize"></param>
    /// <param name="resizeTo"></param>
    /// <returns></returns>
    Stream GetFileStream(string file, bool resize = false, int resizeTo = 960);

    /// <summary>
    /// 文本文件直接返回文本内容
    /// </summary>
    /// <param name="file"></param>
    /// <returns></returns>
    string GetFileTextContent(string file);
    
    /// <summary>
    /// 返回指定的OSS文件的带token可直接访问的完整URL
    /// </summary>
    /// <param name="file"></param>
    /// <param name="resize"></param>
    /// <param name="resizeTo"></param>
    /// <param name="intern">是否是内网访问</param>
    /// <returns></returns>
    string GetFileFullUrl(string file, bool resize = false, int resizeTo = 960, bool intern = false);

    /// <summary>
    /// 获取指定文件路径的临时上传地址，对方可以直接调用该URL的PUT方法上传文件
    /// </summary>
    /// <param name="file"></param>
    /// <param name="intern"></param>
    /// <returns></returns>
    string GetUploadPutUrl(string file, bool intern = false);
}
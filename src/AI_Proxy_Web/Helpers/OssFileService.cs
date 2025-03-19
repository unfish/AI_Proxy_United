using System.Text.RegularExpressions;
using Aliyun.OSS;

namespace AI_Proxy_Web.Helpers
{
    public class OssFileService: IOssFileService
    {
        private readonly string ossAccessKeyId;
        private readonly string ossAccessSecret;
        private readonly string ossEndpoint;
        private readonly string bucket;
    
        public OssFileService(ConfigHelper configuration)
        {
            ossEndpoint = configuration.GetConfig<string>("AliOss:Endpoint");
            ossAccessKeyId = configuration.GetConfig<string>("AliOss:AccessKey");
            ossAccessSecret = configuration.GetConfig<string>("AliOss:AccessSecret");
            bucket = configuration.GetConfig<string>("AliOss:BucketName");
        }
    
        private OssClient GetOssClient()
        {
            return new OssClient(ossEndpoint, ossAccessKeyId, ossAccessSecret);
        }
    
        /// <summary>
        /// 上传文件到OSS
        /// </summary>
        /// <param name="originName"></param>
        /// <param name="inputStream"></param>
        /// <param name="userId"></param>
        /// <param name="useOriginName"></param>
        /// <returns></returns>
        public OssFileDto UploadFile(string originName, Stream inputStream, int userId, bool useOriginName = false)
        {
            //去除文件名中的特殊符号
            var specialCharReg = new Regex("[%$（）()^!@*#<>《》~{}!,&+\\s]");
            originName = specialCharReg.Replace(originName, string.Empty);

            var client = GetOssClient();
            var fullPath = $"file/{userId}/{DateTime.Now:yyyyMMdd}/{DateTime.Now:HHmmss}/{originName}";
            if (useOriginName)
                fullPath = originName;
            client.PutObject(bucket, fullPath, inputStream);
            var obj = client.GetObject(bucket, fullPath);
            return new OssFileDto()
            {
                FilePath = fullPath, FileName = fullPath.Split('/').Last(), FileSize = obj.Metadata.ContentLength, ContentType = obj.Metadata.ContentType, ContentMD5 = obj.Metadata.ContentMd5
            };
        }
    
        /// <summary>
        /// 根据后缀名获取mime-type
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public string GetFileContentType(string filename)
        {
            var ext = filename.Substring(filename.LastIndexOf(".") + 1).ToLower();
            switch (ext)
            {
                case "png":
                    return "image/png";
                case "jpg":
                case "jpeg":
                    return "image/jpeg";
                case "gif":
                    return "image/gif";
                case "doc":
                case "docx":
                    return "application/msword";
                case "pdf":
                    return "application/pdf";
                case "ppt":
                case "pptx":
                    return "application/vnd.ms-powerpoint";
                case "xls":
                case "xlsx":
                    return "application/vnd.ms-excel";
                case "mp4":
                    return "video/mp4";
                case "js":
                    return "application/javascript";
                case "scs":
                    return "application/scvp-cv-response";
                case "xml":
                    return "application/xml";
                default:
                    return "application/octet-stream";
            }
        }
    
        /// <summary>
        /// 读取指定的OSS文件并直接返回文件流
        /// </summary>
        /// <param name="file"></param>
        /// <param name="resize"></param>
        /// <param name="resizeTo"></param>
        /// <returns></returns>
        public Stream GetFileStream(string file, bool resize = false, int resizeTo = 960)
        {
            var client = GetOssClient();
            var mime = GetFileContentType(file);
            var req = new GetObjectRequest(bucket, file);
            if (resize && mime.StartsWith("image"))
                req.Process = "image/resize,w_" + resizeTo + "/format,jpg/quality,q_80";
            if (resize && mime.StartsWith("video"))
                req.Process = "video/snapshot,t_500,f_jpg,m_fast";

            var f = client.GetObject(req);
            return f.Content;
        }
        
        public string GetFileTextContent(string file)
        {
            var client = GetOssClient();
            var mime = GetFileContentType(file);
            var req = new GetObjectRequest(bucket, file);
            var f = client.GetObject(req);
            var sr = new StreamReader(f.Content);
            return sr.ReadToEnd();
        }

        /// <summary>
        /// 返回指定的OSS文件的带token可直接访问的完整URL
        /// </summary>
        /// <param name="file"></param>
        /// <param name="resize"></param>
        /// <param name="resizeTo"></param>
        /// <param name="intern">是否是内网访问</param>
        /// <returns></returns>
        public string GetFileFullUrl(string file, bool resize = false, int resizeTo = 960, bool intern = false)
        {
            var client = GetOssClient();
            var req = new GeneratePresignedUriRequest(bucket, file, SignHttpMethod.Get);
            req.Expiration = DateTime.Now.AddMinutes(60);
            var mime = GetFileContentType(file);
            if (resize && mime.StartsWith("image"))
                req.Process = "image/resize,w_" + resizeTo + "/format,png/quality,q_80";
            if (resize && mime.StartsWith("video"))
                req.Process = "video/snapshot,t_500,f_jpg,m_fast";
            var url = client.GeneratePresignedUri(req);
            var result = url.AbsoluteUri;
            if (!intern)
                result = result.Replace("-internal", "");
            return result;
        }

        /// <summary>
        /// 获取指定文件路径的临时上传地址，对方可以直接调用该URL的PUT方法上传文件
        /// </summary>
        /// <param name="file"></param>
        /// <param name="intern"></param>
        /// <returns></returns>
        public string GetUploadPutUrl(string file, bool intern = false)
        {
            var client = GetOssClient();
            var req = new GeneratePresignedUriRequest(bucket, file, SignHttpMethod.Put);
            req.Expiration = DateTime.Now.AddMinutes(60);
            var mime = GetFileContentType(file); 
            req.ContentType = mime;
            var url = client.GeneratePresignedUri(req);
            var result = url.AbsoluteUri;
            if (!intern)
                result = result.Replace("-internal", "");
            return result;
        }
    }
}
namespace AI_Proxy_Web.Helpers;

 public class MstApiResult<T>
    {
        public bool Success { get; set; }

        /// <summary>
        /// 0为正常，否则错误
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 为错误消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 正常返回值
        /// </summary>
        public T Body { get; set; }
    }


    /// <summary>
    /// 快速创建返回类型的帮助对象，将任意内容封装为返回格式
    /// </summary>
    public static class R
    {
        public static MstApiResult<T> New<T>(T body)
        {
            return new MstApiResult<T>() { Body = body, Success = true, Code = 0 };
        }

        public static MstApiResult<T> New<T>(int code, string msg, T body)
        {
            return new MstApiResult<T>() { Code = code, Message = msg, Body = body };
        }

        public static MstApiResult<T> Error<T>(string msg)
        {
            return new MstApiResult<T>() { Body = default(T), Success = false, Message = msg };
        }

        public static MstApiResult<string> ErrorPermission()
        {
            return new MstApiResult<string>() { Code = 401, Message = "Permission Error.", Body = null };
        }

        public static MstApiResult<string> NoPermission()
        {
            return new MstApiResult<string>() { Code = 403, Message = "您当前暂无权限！", Body = null };
        }

        public static MstApiResult<string> NoPermission(string permissionName)
        {
            return new MstApiResult<string>() { Code = 403, Message = $"您当前暂无【{permissionName}】权限！", Body = null };
        }
        public static MstApiResult<string> NoDownloadPermission()
        {
            return new MstApiResult<string>() { Code = 403, Message = "您没有下载权限.", Body = null };
        }
        public static MstApiResult<T> New<T>(bool success, string msg, T body)
        {
            return new MstApiResult<T>() { Success = success, Message = msg, Body = body };
        }

        public static MstApiResult<T> New<T>(bool success, int code, string msg, T body)
        {
            return new MstApiResult<T>() { Success = success, Code = code, Message = msg, Body = body };
        }
    }
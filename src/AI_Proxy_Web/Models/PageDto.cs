namespace AI_Proxy_Web.Models;

public class PageDto<T>
{
    public List<T>? List { get; set; }
    public int Total { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}
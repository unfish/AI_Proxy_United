using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AI_Proxy_Web.Helpers;

public static class SSEHttpContextExtensions 
{
    public static async Task SSEInitAsync(this HttpContext ctx)
    {
        ctx.Response.Headers.Append("Cache-Control", "no-cache");
        ctx.Response.Headers.Append("Content-Type", "text/event-stream");
        await ctx.Response.Body.FlushAsync();
    }
    
    public static async Task SSESendDataAsync(this HttpContext ctx, string data)
    {
        foreach(var line in data.Split('\n'))
            await ctx.Response.WriteAsync("data: " + line + "\n");
        
        await ctx.Response.WriteAsync("\n");
        await ctx.Response.Body.FlushAsync();
    }
    
    public static async Task SSESendEventAsync(this HttpContext ctx, SSEEvent e)
    {
        var lines = e.Data switch
        {
            null        => new [] { String.Empty },
            string s    => s.Split('\n').ToArray(),
            _           => new [] { JsonSerializer.Serialize(e.Data) }
        };

        foreach(var line in lines)
            await ctx.Response.WriteAsync("data: " + line + "\n");

        await ctx.Response.WriteAsync("\n");
        await ctx.Response.Body.FlushAsync();
    }

    public static async Task SSESendChatEventAsync(this HttpContext ctx, string msg)
    {
        try
        {
            await SSESendEventAsync(ctx, new SSEEvent("ai_chat", msg));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
    
    public static async Task SSESendCommentAsync(this HttpContext ctx, string comment)
    {
        foreach(var line in comment.Split('\n'))
            await ctx.Response.WriteAsync(": " + line + "\n");
        
        await ctx.Response.WriteAsync("\n");
        await ctx.Response.Body.FlushAsync();
    }
    
    public static async Task SendNonStreamAsync(this HttpContext ctx, object obj)
    {
        await ctx.Response.WriteAsJsonAsync(obj);
        await ctx.Response.Body.FlushAsync();
    }
    
    public static async Task SSESendLeTianEventAsync(this HttpContext ctx, int code, string msg, bool is_end)
    {
        await ctx.Response.WriteAsync("event: message\n");
        var resp = JsonConvert.SerializeObject(new
        {
            code, msg = code == 0 ? "success" : msg, data = new { content = msg, is_end }
        });
        await ctx.Response.WriteAsync("data: " + resp + "\n");
        await ctx.Response.WriteAsync("\n");
        await ctx.Response.Body.FlushAsync();
    }
}
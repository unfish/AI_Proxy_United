using System.Collections.Concurrent;
using AI_Proxy_Web.Helpers;
using Microsoft.Playwright;
using SkiaSharp;

namespace AI_Proxy_Web.Apis.V2.Extra;

public class AutomationHelper
{
    private AutomationHelper()
    {
    }
    private static ConcurrentDictionary<string, AutomationHelper> HelperCaches = new ConcurrentDictionary<string, AutomationHelper>();

    public static async Task<AutomationHelper> GetInstance(string sessionId, int width=1024, int height=768)
    {
        if (string.IsNullOrEmpty(sessionId))
            sessionId = "main"; //不传session时使用共享同一个实例
        if (HelperCaches.TryGetValue(sessionId, out var instance))
        {
            return instance;
        }
        else
        {
            var helper = new AutomationHelper() {_sessionId  = sessionId, _pageWidth = width, _pageHeight = height };
            await helper.StartBrowser();
            HelperCaches.TryAdd(sessionId, helper);
            return helper;
        }
    }

    public static readonly string[] AutomationFunctions = new[]
    {
        "computer", "str_replace_based_edit_tool", "bash", "OpenUrl", "GetPageHtml", "GoBack", "SendFile", "ClickElement", "InputElement", "Screenshot", "image_generation", "image_generation_call"
    };    
    
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private List<IPage> _pages = new List<IPage>();
    private string? _sessionId;
    private int _pageWidth = 0;
    private int _pageHeight = 0;
    private DateTime _lastActionTime = DateTime.Now;
    private async Task StartBrowser()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions(){Headless = true});
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions()
        {
            ViewportSize =  new ViewportSize(){Width = _pageWidth, Height = _pageHeight}, DeviceScaleFactor = 2f
        });
        _context.Page += (sender, page) => { _pages.Add(page); };
    }

    private async Task CloseAllPages()
    {
        if (_pages.Count > 0)
        {
            for(var i = _pages.Count - 1; i >= 0; i--)
            {
                await _pages[i].CloseAsync();
                _pages.RemoveAt(i);
            }
        }
    }

    public async Task<bool> OpenUrl(string url)
    {
        await CloseAllPages();
        var page = await _context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        _lastActionTime = DateTime.Now;
        try
        {
            await page.GotoAsync(url);
            // 等待页面完全加载
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }
        catch
        {
            return  false;
        }
    }

    private async Task Release()
    {
        await CloseAllPages();
        await _context.CloseAsync();
        await _browser.CloseAsync();
        HelperCaches.TryRemove(_sessionId, out _);
    }
    
    public async Task GoBack()
    {
        if (_pages.Count > 1)
        {
            _pages.RemoveAt(_pages.Count - 1);
        }else if (_pages.Count == 1)
        {
            await _pages[0].GoBackAsync();
            Thread.Sleep(_actionWaitTime);
        }
        _lastActionTime = DateTime.Now;
    }

    public async Task<byte[]?> Screenshot(bool fullscreen=false)
    {
        _lastActionTime = DateTime.Now;
        if (_pages.Count > 0)
        {
            var page = _pages.Last();
            var bytes = await page.ScreenshotAsync(new (){ FullPage = fullscreen});
            if (fullscreen)
                return bytes;
            return ImageHelper.Compress(bytes, new SKSize(_pageWidth, _pageHeight), SKEncodedImageFormat.Png);
        }
        else
        {
            return null;
        }
    }

    public async Task<string> GetHtml()
    {
        var page = _pages.Last();
        var html = await page.ContentAsync();
        html = HtmlHelper.ExtractCoreDom(html, false);
        _lastActionTime = DateTime.Now;
        return html;
    }
    
    public async Task<string> GetVisibleHtml()
    {
        var page = _pages.Last();
        // 在浏览器中执行JavaScript来获取可见DOM
        string visibleHtml = await page.EvaluateAsync<string>(@"
        () => {
            // 判断元素是否可见的函数
            function isVisible(element) {
                if (!element) return false;
                
                // 检查元素是否在DOM中
                if (!element.isConnected) return false;
                
                // 获取计算样式
                const style = window.getComputedStyle(element);
                
                // 检查基本可见性属性
                if (style.display === 'none') return false;
                if (style.visibility === 'hidden' || style.visibility === 'collapse') return false;
                if (parseFloat(style.opacity) === 0) return false;
                
                // 检查元素尺寸
                const rect = element.getBoundingClientRect();
                if (rect.width === 0 && rect.height === 0) return false;
                
                // 检查是否被裁剪到不可见
                if (style.overflow !== 'visible' && rect.width === 0 && rect.height === 0) return false;
                
                // 递归检查父元素
                if (element.parentElement) {
                    return isVisible(element.parentElement);
                }
                
                return true;
            }
            
            // 创建一个新的文档
            const visibleDoc = document.implementation.createHTMLDocument('');
            
            // 处理head元素
            const sourceHead = document.head;
            const targetHead = visibleDoc.head;
            
            // 清空目标head
            while (targetHead.firstChild) {
                targetHead.removeChild(targetHead.firstChild);
            }
            
            // 复制重要的head元素
            for (const child of sourceHead.children) {
                if (['TITLE', 'META'].includes(child.tagName)) {
                    const newNode = child.cloneNode(true);
                    targetHead.appendChild(newNode);
                }
            }
            
            // 处理body元素
            const sourceBody = document.body;
            const targetBody = visibleDoc.body;
            
            // 清空目标body
            while (targetBody.firstChild) {
                targetBody.removeChild(targetBody.firstChild);
            }
            
            // 处理body的子元素
            function processBodyChildren(sourceNode, targetParent) {
                for (const child of sourceNode.childNodes) {
                    // 处理元素节点
                    if (child.nodeType === Node.ELEMENT_NODE) {
                        // 跳过脚本和样式元素
                        if (['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(child.tagName)) {
                            continue;
                        }
                        
                        // 检查可见性
                        if (!isVisible(child)) {
                            continue;
                        }
                        
                        // 创建新元素
                        const newElement = visibleDoc.createElement(child.tagName);
                        
                        // 复制属性
                        for (const attr of child.attributes) {
                            newElement.setAttribute(attr.name, attr.value);
                        }
                        
                        // 添加到目标父节点
                        targetParent.appendChild(newElement);
                        
                        // 递归处理子节点
                        processBodyChildren(child, newElement);
                    } 
                    // 处理文本节点
                    else if (child.nodeType === Node.TEXT_NODE) {
                        const text = child.textContent.trim();
                        if (text) {
                            targetParent.appendChild(visibleDoc.createTextNode(child.textContent));
                        }
                    }
                }
            }
            
            // 处理body内容
            processBodyChildren(sourceBody, targetBody);
            
            // 获取DOCTYPE
            let doctype = '';
            if (document.doctype) {
                doctype = new XMLSerializer().serializeToString(document.doctype);
            }
            
            // 返回HTML字符串
            return doctype + visibleDoc.documentElement.outerHTML;
        }
        ");

        var html = HtmlHelper.ExtractCoreDom(visibleHtml, false);
        _lastActionTime = DateTime.Now;
        return html;
    }
    
    public async Task MoveMouse(int x, int y)
    {
        var page = _pages.Last();
        await page.Mouse.MoveAsync(x, y);
        _currentX = x;
        _currentY = y;
        _lastActionTime = DateTime.Now;
    }

    private int _actionWaitTime = 300; //下面这些动作做完以后自动等待一段时间
    public async Task Click(int x, int y)
    {
        var page = _pages.Last();
        _currentX = x;
        _currentY = y;
        await page.Mouse.ClickAsync(x, y);
        // 等待页面完全加载
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }

    private int _currentX = 0;
    private int _currentY = 0;
    public async Task Click()
    {
        var page = _pages.Last();
        await page.Mouse.ClickAsync(_currentX, _currentY);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    public async Task InputText(string text)
    {
        var page = _pages.Last();
        await page.Keyboard.InsertTextAsync(text);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public async Task PressKey(string text)
    {
        var page = _pages.Last();
        await page.Keyboard.PressAsync(text);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public async Task<bool> ClickElement(string selector)
    {
        var page = _pages.Last();
        _lastActionTime = DateTime.Now;
        if (await page.Locator(selector).CountAsync() == 1)
        {
            await page.Locator(selector).ClickAsync(new() { Force = true });
            // 等待页面完全加载
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Thread.Sleep(_actionWaitTime);
            return true;
        }
        return false;
    }
    
    public async Task<bool> InputElement(string selector, string text)
    {
        var page = _pages.Last();
        _lastActionTime = DateTime.Now;
        if (await page.Locator(selector).CountAsync() == 1)
        {
            await page.Locator(selector).FillAsync(text, new() { Force = true });
            Thread.Sleep(_actionWaitTime);
            return true;
        }
        return false;
    }
    
    public async Task ScrollPage(int amount, string direction)
    {
        var x = 0;
        var y = amount * 80;
        switch (direction)
        {
            case "left":
                x = -amount * 80;
                y = 0;
                break;
            case "right":
                x = amount * 80;
                y = 0;
                break;
            case "up":
                x = 0;
                y = -amount * 80;
                break;
        }
        var page = _pages.Last();
        await page.Mouse.WheelAsync(x, y);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public static async Task CleanCache() //因为用户不会主动关闭已打开的浏览器，使用定时任务来清除半小时没操作过的浏览器进程
    {
        foreach (var key in HelperCaches.Keys)
        {
            if (HelperCaches.TryGetValue(key, out var instance))
            {
                if (instance._lastActionTime < DateTime.Now.AddMinutes(-30))
                {
                    await instance.Release();
                }
            }
        }
    }
}
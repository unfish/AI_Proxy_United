using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace AI_Proxy_Web.Helpers;

public class CustomRateLimiter
{
    private readonly ConcurrentDictionary<string, (SlidingWindowRateLimiter slidingWindowLimiter, ConcurrencyLimiter concurrencyLimiter)> _limiters;
    private readonly SlidingWindowRateLimiterOptions _slidingWindowOptions;
    private readonly ConcurrencyLimiterOptions _concurrencyOptions;

    public CustomRateLimiter()
    {
        _limiters = new ConcurrentDictionary<string, (SlidingWindowRateLimiter, ConcurrencyLimiter)>();

        _slidingWindowOptions = new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20, // 每10分钟限制请求20次
            Window = TimeSpan.FromMinutes(10),
            SegmentsPerWindow = 1
        };

        _concurrencyOptions = new ConcurrencyLimiterOptions
        {
            PermitLimit = 1, // 限制并发请求数为1
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        };
    }

    public async Task<bool> IsRequestAllowedAsync(string userId)
    {
        var limiters = _limiters.GetOrAdd(userId, _ => CreateRateLimiters());

        var slidingWindowLimiter = limiters.slidingWindowLimiter;
        var concurrencyLimiter = limiters.concurrencyLimiter;

        using var slidingWindowLease = await slidingWindowLimiter.AcquireAsync(1);
        if (!slidingWindowLease.IsAcquired)
        {
            return false;
        }

        using var concurrencyLease = await concurrencyLimiter.AcquireAsync(1);
        return concurrencyLease.IsAcquired;
    }

    private (SlidingWindowRateLimiter, ConcurrencyLimiter) CreateRateLimiters()
    {
        var slidingWindowLimiter = new SlidingWindowRateLimiter(_slidingWindowOptions);
        var concurrencyLimiter = new ConcurrencyLimiter(_concurrencyOptions);

        return (slidingWindowLimiter, concurrencyLimiter);
    }
}
using Hangfire.Dashboard;

namespace AI_Proxy_Web.Helpers;

public class HangfireAuthorizationFilter: IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext dashboardContext)
    {
        var context = dashboardContext.GetHttpContext();
        var cookiesToken = context.Request.Cookies["admin-token"];
        return true;
    }
}
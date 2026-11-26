using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net;

namespace SISI;

public class HealthController : BaseController
{
    [HttpGet]
    [Route("sisi/health")]
    async public Task<ActionResult> Index(string run = null)
    {
        // string thay vì bool: "1" không bind được sang bool (chỉ "true"/"false") và lỗi đó bị nuốt
        if (run == "1" || string.Equals(run, "true", StringComparison.OrdinalIgnoreCase))
        {
            // mỗi lần chạy là N request ra site thật → chỉ cho gọi từ chính máy
            if (!IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback))
                return StatusCode(403, "chi chay duoc tu localhost");

            await HealthMonitor.Run();
        }

        Response.Headers.CacheControl = "no-cache";
        return ContentTo(HealthMonitor.Snapshot().ToString(Formatting.None), "application/json; charset=utf-8");
    }
}

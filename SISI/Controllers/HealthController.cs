using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace SISI;

public class HealthController : BaseController
{
    [HttpGet]
    [Route("sisi/health")]
    async public Task<ActionResult> Index(string run = null)
    {
        // string thay vì bool: "1" không bind được sang bool (chỉ "true"/"false") và lỗi đó bị nuốt
        if (run == "1" || string.Equals(run, "true", StringComparison.OrdinalIgnoreCase))
            await HealthMonitor.Run();

        Response.Headers.CacheControl = "no-cache";
        return ContentTo(HealthMonitor.Snapshot().ToString(Formatting.None), "application/json; charset=utf-8");
    }
}

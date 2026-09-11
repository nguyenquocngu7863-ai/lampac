using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RusTrio;

// Gop 3 nguon Nga Mirage/Spectre/Phantom vao 1 cho: hoi song song ca 3
// (qua localhost, giu nguyen code goc) roi gop the lai. Nguon nao khong
// co link thi bo qua, khoi mo tung cai thu tay.
public class RusTrioController : BaseOnlineController
{
    static readonly string[] Sources = ["mirage", "spectre", "phantom"];

    public RusTrioController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/rustrio")]
    async public Task<ActionResult> Index()
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string qs = HttpContext.Request.QueryString.Value ?? string.Empty;

        var tasks = Sources.Select(src => FetchSource(src, qs)).ToArray();
        var results = await Task.WhenAll(tasks);

        var sb = new StringBuilder();
        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.body))
                continue;

            sb.Append($"<div class=\"videos__line\"><div class=\"videos__line-title\">── {r.src} ──</div></div>");
            sb.Append(r.body);
        }

        if (sb.Length == 0)
            return OnError();

        return Content(sb.ToString(), "text/html; charset=utf-8");
    }

    async Task<(string src, string body)> FetchSource(string src, string qs)
    {
        try
        {
            string body = await Http.Get($"{host}/lite/{src}{qs}", timeoutSeconds: 40, statusCodeOK: false);
            if (string.IsNullOrWhiteSpace(body) || !body.Contains("videos__item"))
                return (src, null);

            return (src, body);
        }
        catch
        {
            return (src, null);
        }
    }
}

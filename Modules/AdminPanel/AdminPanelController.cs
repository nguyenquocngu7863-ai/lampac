using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Shared;
using Shared.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdminPanel;

[Authorization(redirectUri: "/adminpanel/auth")]
public class AdminPanelController : BaseController
{
    const string InitFile = "init.conf";
    const string CurrentFile = "current.conf";
    const string UsersFile = "users.json";

    [HttpGet]
    [AllowAnonymous]
    [Route("/adminpanel/auth")]
    public ActionResult Auth()
    {
        if (Request.Cookies.TryGetValue("accspasswd", out var passwd) &&
            passwd == CoreInit.rootPasswd)
        {
            return Redirect("/adminpanel");
        }

        var path = Path.Combine(ModInit.modpath, "auth.html");
        var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("/adminpanel/auth")]
    public ActionResult AuthLogin(string password, string remember)
    {
        if (!string.IsNullOrEmpty(password))
            password = password.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace(" ", "");

        if (string.IsNullOrEmpty(password) || password != CoreInit.rootPasswd)
            return Redirect("/adminpanel/auth?error=1");

        bool keep = remember is "1" or "on" or "true";
        SetRootCookie(password, keep);
        return Redirect("/adminpanel");
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/adminpanel/api/session")]
    public ActionResult ApiSession()
    {
        bool ok = Request.Cookies.TryGetValue("accspasswd", out var passwd) &&
                  passwd == CoreInit.rootPasswd;
        return new ContentResult
        {
            Content = ok ? "{\"ok\":true}" : "{\"ok\":false}",
            ContentType = "application/json; charset=utf-8",
            StatusCode = ok ? 200 : 401
        };
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("/adminpanel/api/login")]
    public async Task<IActionResult> ApiLogin()
    {
        string password = null;
        bool keep = true;

        string contentType = Request.ContentType ?? string.Empty;
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var parsed = JObject.Parse(body);
                    password = parsed.Value<string>("password");
                    keep = parsed.Value<bool?>("remember") ?? true;
                }
                catch (JsonException)
                {
                    return AdminJsonError(400, "invalid json");
                }
            }
        }
        else
        {
            password = Request.HasFormContentType ? Request.Form["password"].ToString() : null;
            string remember = Request.HasFormContentType ? Request.Form["remember"].ToString() : null;
            keep = remember is "1" or "on" or "true" || string.IsNullOrEmpty(remember);
        }

        if (!string.IsNullOrEmpty(password))
            password = password.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace(" ", "");

        if (string.IsNullOrEmpty(password) || password != CoreInit.rootPasswd)
            return AdminJsonError(401, "invalid password");

        SetRootCookie(password, keep);
        return AdminJsonOk();
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("/adminpanel/api/logout")]
    public ActionResult ApiLogout()
    {
        Response.Cookies.Delete("accspasswd", new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
        return AdminJsonOk();
    }

    void SetRootCookie(string password, bool keep)
    {
        var options = new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            HttpOnly = true,
            IsEssential = true
        };
        if (keep)
        {
            options.Expires = DateTimeOffset.UtcNow.AddYears(10);
            options.MaxAge = TimeSpan.FromDays(3650);
        }

        Response.Cookies.Append("accspasswd", password, options);
    }

    [HttpGet]
    [Route("/adminpanel")]
    public ActionResult Index()
    {
        var path = Path.Combine(ModInit.modpath, "index.html");
        var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet]
    [Route("/adminpanel/api/groups")]
    public ActionResult Groups()
    {
        var sites = DiscoverNextHubSites();
        var current = LoadCurrentRoot(sites);
        var built = ConfigSectionGroups.Build(current, sites.Keys);
        var json = JsonConvert.SerializeObject(built, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });
        return Content(json, "application/json; charset=utf-8");
    }

    [HttpGet]
    [Route("/adminpanel/api/groups/catalog")]
    public ActionResult GroupsCatalog()
    {
        var sites = DiscoverNextHubSites();
        var built = ConfigSectionGroups.BuildCatalog(sites.Keys);
        var current = LoadCurrentRoot(sites);
        var inCatalog = new System.Collections.Generic.HashSet<string>(ConfigSectionGroups.CatalogRootKeys, StringComparer.Ordinal);
        inCatalog.UnionWith(sites.Keys);
        var orphans = current.Properties()
            .Select(p => p.Name)
            .Where(k => !inCatalog.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (orphans.Length > 0)
            built.Add(new GroupDto("other", "Khác", "Các khóa trong current chưa có trong danh mục nhóm.", orphans));

        var json = JsonConvert.SerializeObject(built, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });
        return Content(json, "application/json; charset=utf-8");
    }

    static Dictionary<string, bool> DiscoverNextHubSites()
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            // AdminPanel itself may be loaded from mods/ while NextHUB still
            // lives in module/. Search both loader roots instead of assuming
            // both modules are siblings.
            var sitesDirs = new[]
            {
                Path.GetFullPath(Path.Combine(ModInit.modpath, "..", "NextHUB", "sites")),
                Path.GetFullPath(Path.Combine(ModInit.modpath, "..", "..", "module", "NextHUB", "sites")),
                Path.GetFullPath(Path.Combine(ModInit.modpath, "..", "..", "mods", "NextHUB", "sites"))
            }
            .Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists);

            foreach (var sitesDir in sitesDirs)
            foreach (var path in Directory.GetFiles(sitesDir, "*.yaml"))
            {
                var slug = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(slug) || slug.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
                    continue;

                var enabled = true;
                foreach (var line in System.IO.File.ReadLines(path))
                {
                    if (!line.StartsWith("enable:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (bool.TryParse(line.Substring(line.IndexOf(':') + 1).Trim(), out var parsed))
                        enabled = parsed;
                    break;
                }
                result[slug] = enabled;
            }
        }
        catch
        {
        }
        return result;
    }

    static JObject LoadCurrentRoot(Dictionary<string, bool> nextHubSites = null)
    {
        var root = new JObject();
        try
        {
            if (System.IO.File.Exists(CurrentFile))
                root = JObject.Parse(System.IO.File.ReadAllText(CurrentFile, Encoding.UTF8));
        }
        catch (JsonException)
        {
            root = new JObject();
        }

        // current.conf can be written before SISI enumerates its YAML sources.
        // Merge missing runtime sections so AdminPanel does not hide them.
        if (CoreInit.CurrentConf != null)
        {
            try
            {
                foreach (var property in CoreInit.CurrentConf.Properties())
                {
                    if (root.Property(property.Name) == null)
                        root[property.Name] = property.Value.DeepClone();
                }
            }
            catch
            {
            }
        }

        // Every NextHUB YAML is independently overrideable through a root key
        // with the same slug. Supply an enable template even before the source
        // has been opened, allowing AdminPanel to manage all installed sites.
        foreach (var site in nextHubSites ?? DiscoverNextHubSites())
        {
            if (root.Property(site.Key) == null)
                root[site.Key] = new JObject { ["enable"] = site.Value };
        }

        return root;
    }

    [HttpGet]
    [Route("/adminpanel/api/init")]
    public ActionResult GetInit()
    {
        if (!System.IO.File.Exists(InitFile))
            return Content("{}", "application/json; charset=utf-8");

        var text = System.IO.File.ReadAllText(InitFile, Encoding.UTF8);
        return Content(NormalizeJsonText(text), "application/json; charset=utf-8");
    }

    [HttpGet]
    [Route("/adminpanel/api/current")]
    public ActionResult GetCurrent()
    {
        var current = LoadCurrentRoot(DiscoverNextHubSites());
        return Content(current.ToString(Formatting.Indented), "application/json; charset=utf-8");
    }

    [HttpPost]
    [Route("/adminpanel/api/init")]
    public async Task<IActionResult> SaveInit()
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return AdminJsonError(400, "empty body");

        try
        {
            var parsed = JToken.Parse(body);
            if (parsed.Type != JTokenType.Object)
                return AdminJsonError(400, "root must be a JSON object");

            var formatted = ((JObject)parsed).ToString(Formatting.Indented);
            await WriteInitAtomicAsync(formatted).ConfigureAwait(false);
            return AdminJsonOk();
        }
        catch (JsonException ex)
        {
            return AdminJsonError(400, "invalid json", ex.Message);
        }
        catch (IOException ex)
        {
            return AdminJsonError(500, "failed to write init.conf", ex.Message);
        }
    }

    [HttpPost]
    [Route("/adminpanel/api/init/section/{key}")]
    public async Task<IActionResult> SaveInitSection(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\'))
            return AdminJsonError(400, "invalid section key");

        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return AdminJsonError(400, "empty body");

        JToken sectionToken;
        try
        {
            sectionToken = JToken.Parse(body);
        }
        catch (JsonException ex)
        {
            return AdminJsonError(400, "invalid json", ex.Message);
        }

        try
        {
            JObject root;
            if (System.IO.File.Exists(InitFile))
            {
                try
                {
                    root = JObject.Parse(System.IO.File.ReadAllText(InitFile, Encoding.UTF8));
                }
                catch (JsonException)
                {
                    root = new JObject();
                }
            }
            else
                root = new JObject();

            root[key] = sectionToken.DeepClone();
            var formatted = root.ToString(Formatting.Indented);
            await WriteInitAtomicAsync(formatted).ConfigureAwait(false);
            return AdminJsonOk();
        }
        catch (IOException ex)
        {
            return AdminJsonError(500, "failed to write init.conf", ex.Message);
        }
    }

    // tmp+Move — атомарно; на bind mount Move(..., overwrite) часто EBUSY → пишем в init.conf напрямую.
    static async Task WriteInitAtomicAsync(string formatted)
    {
        var tmp = InitFile + ".tmp";
        await System.IO.File.WriteAllTextAsync(tmp, formatted, Encoding.UTF8).ConfigureAwait(false);
        try
        {
            try
            {
                System.IO.File.Move(tmp, InitFile, overwrite: true);
            }
            catch (IOException ex) when (IsReplaceTargetBusy(ex))
            {
                await System.IO.File.WriteAllTextAsync(InitFile, formatted, Encoding.UTF8).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tmp))
                    System.IO.File.Delete(tmp);
            }
            catch
            {
            }
        }
    }

    static bool IsReplaceTargetBusy(IOException ex)
    {
        for (Exception e = ex; e != null; e = e.InnerException)
        {
            if (e.Message != null && e.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static ContentResult AdminJsonOk()
    {
        return new ContentResult
        {
            Content = "{\"ok\":true}",
            ContentType = "application/json; charset=utf-8",
            StatusCode = 200
        };
    }

    static ContentResult AdminJsonError(int status, string error, string detail = null)
    {
        var o = new JObject { ["error"] = error };
        if (!string.IsNullOrEmpty(detail))
            o["detail"] = detail;
        return new ContentResult
        {
            Content = o.ToString(Formatting.None),
            ContentType = "application/json; charset=utf-8",
            StatusCode = status
        };
    }

    static string NormalizeJsonText(string raw)
    {
        try
        {
            return JToken.Parse(raw).ToString(Formatting.Indented);
        }
        catch
        {
            return raw;
        }
    }

    [HttpGet]
    [Route("/adminpanel/api/users-json")]
    public ActionResult GetUsersJson()
    {
        if (!System.IO.File.Exists(UsersFile))
            return Content("[]", "application/json; charset=utf-8");

        var text = System.IO.File.ReadAllText(UsersFile, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
            return Content("[]", "application/json; charset=utf-8");

        return Content(NormalizeJsonText(text), "application/json; charset=utf-8");
    }

    [HttpPost]
    [Route("/adminpanel/api/users-json")]
    public async Task<IActionResult> SaveUsersJson()
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return AdminJsonError(400, "empty body");

        try
        {
            var parsed = JToken.Parse(body);
            if (parsed.Type != JTokenType.Array)
                return AdminJsonError(400, "root must be a JSON array", "users.json must be a list of AccsUser objects");

            foreach (var item in (JArray)parsed)
            {
                if (item.Type != JTokenType.Object)
                    return AdminJsonError(400, "invalid array item", "each element must be a JSON object");
            }

            var formatted = ((JArray)parsed).ToString(Formatting.Indented);
            await WriteUsersAtomicAsync(formatted).ConfigureAwait(false);
            return AdminJsonOk();
        }
        catch (JsonException ex)
        {
            return AdminJsonError(400, "invalid json", ex.Message);
        }
        catch (IOException ex)
        {
            return AdminJsonError(500, "failed to write users.json", ex.Message);
        }
    }

    static async Task WriteUsersAtomicAsync(string formatted)
    {
        var tmp = UsersFile + ".tmp";
        await System.IO.File.WriteAllTextAsync(tmp, formatted, Encoding.UTF8).ConfigureAwait(false);
        try
        {
            try
            {
                System.IO.File.Move(tmp, UsersFile, overwrite: true);
            }
            catch (IOException ex) when (IsReplaceTargetBusy(ex))
            {
                await System.IO.File.WriteAllTextAsync(UsersFile, formatted, Encoding.UTF8).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tmp))
                    System.IO.File.Delete(tmp);
            }
            catch
            {
            }
        }
    }
}

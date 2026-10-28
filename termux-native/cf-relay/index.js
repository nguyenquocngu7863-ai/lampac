/**
 * xn-relay — Worker relay cho media/XHR cần egress Cloudflare.
 * Dùng: https://<worker>/<host>/<path>?query  →  fetch https://<host>/<path>?query
 * - forward Range/Referer/Origin/UA (HLS segment + 206)
 * - cache GET ở edge (cacheEverything, TTL 1h) → xem lại không về origin
 * - chỉ cho host trong allowlist (env ALLOW, mặc định "xnhau.pub") để không thành open relay
 */
const UA =
  "Mozilla/5.0 (Linux; Android 14; SM-A525F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

function allowed(host, env) {
  const list = (env.ALLOW || "xnhau.pub").split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
  host = host.toLowerCase();
  return list.some((d) => host === d || host.endsWith("." + d));
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    const m = url.pathname.match(/^\/([^/]+)(\/.*)?$/);
    if (!m) return new Response("usage: /<host>/<path>", { status: 400 });

    const host = m[1];
    if (!allowed(host, env)) return new Response("host not allowed: " + host, { status: 403 });

    const target = `https://${host}${m[2] || "/"}${url.search}`;
    const h = new Headers();
    for (const k of ["range", "referer", "origin", "accept", "accept-language", "cookie"]) {
      const v = req.headers.get(k);
      if (v) h.set(k, v);
    }
    if (!h.has("referer")) h.set("referer", `https://${host}/`);
    h.set("user-agent", req.headers.get("user-agent") || UA);

    const up = await fetch(target, {
      method: req.method,
      headers: h,
      redirect: "follow",
      cf: { cacheEverything: true, cacheTtl: 3600 },
    });

    const out = new Headers(up.headers);
    out.set("access-control-allow-origin", "*");
    out.set("x-relay", host);
    out.delete("set-cookie");
    out.delete("content-security-policy");
    return new Response(up.body, { status: up.status, headers: out });
  },
};

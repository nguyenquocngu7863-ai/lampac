/**
 * xn-relay — Worker relay cho media/XHR cần egress Cloudflare.
 * Dùng: https://<worker>/<host>/<path>?query  →  fetch https://<host>/<path>?query
 * - forward Range/Referer/Origin/UA (HLS segment + 206)
 * - cache GET ở edge (cacheEverything, TTL 1h) → xem lại không về origin
 * - chỉ cho host trong allowlist (env ALLOW, mặc định "xnhau.pub,xnhau.pink") để không thành open relay
 */
const UA =
  "Mozilla/5.0 (Linux; Android 14; SM-A525F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

function allowed(host, env) {
  const list = (env.ALLOW || "xnhau.pub,xnhau.pink,jav.guru").split(",").map((s) => s.trim().toLowerCase()).filter(Boolean);
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

    // `__nf=1` (opt-in): tra 3xx NGUYEN VEN (redirect: manual) de client tu di
    // tiep. Can cho gateway jav.guru `/searcho/?hr=..` 302 sang host embed that
    // (DoodStream/VOE): follow o worker thi url_effective = URL worker nen mat
    // host embed -> module khong tinh duoc baseUrl. Tham so bi cat truoc khi
    // relay de khong lot sang origin.
    const noFollow = url.searchParams.get("__nf") === "1";
    if (noFollow) url.searchParams.delete("__nf");

    const target = `https://${host}${m[2] || "/"}${url.search}`;
    const h = new Headers();
    for (const k of ["range", "referer", "origin", "accept", "accept-language", "cookie", "content-type", "authorization", "x-requested-with"]) {
      const v = req.headers.get(k);
      if (v) h.set(k, v);
    }
    if (!h.has("referer")) h.set("referer", `https://${host}/`);
    h.set("user-agent", req.headers.get("user-agent") || UA);

    const hasBody = req.method !== "GET" && req.method !== "HEAD";
    const up = await fetch(target, {
      method: req.method,
      headers: h,
      body: hasBody ? req.body : undefined,
      duplex: hasBody ? "half" : undefined,
      redirect: noFollow ? "manual" : "follow",
      cf: { cacheEverything: true, cacheTtl: 60 },
    });

    const out = new Headers(up.headers);
    out.set("access-control-allow-origin", "*");
    out.set("x-relay", host);
    // set-cookie: worker fetch gop nhieu header thanh 1 chuoi -> phai append
    // tung cai de session (cookie phien cua trang detail) con nguyen.
    out.delete("set-cookie");
    const sc = up.headers.getSetCookie ? up.headers.getSetCookie() : [];
    for (const c of sc) out.append("set-cookie", c);
    out.delete("content-security-policy");
    return new Response(up.body, { status: up.status, headers: out });
  },
};

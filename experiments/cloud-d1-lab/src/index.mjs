// Isolated GGman cloud-storage lab. No production auth or CloudBase credentials.
const kinds = Object.freeze({
  app_settings: 32768,
  ui_text: 184320,
  esc_profile: 400000,
  game_profile: 510000
});
const response = (status, data, extra = {}) => new Response(JSON.stringify(data), {
  status, headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store",
    "x-content-type-options": "nosniff", ...extra }
});
const bytes = text => new TextEncoder().encode(text).length;
const encode = input => Uint8Array.from(atob(input.replace(/-/g, "+").replace(/_/g, "/")
  .padEnd(Math.ceil(input.length / 4) * 4, "=")), char => char.charCodeAt(0));
const decode = input => JSON.parse(new TextDecoder().decode(encode(input)));

let keyCache = null;
let keyCacheExpiry = 0;
async function jwtOwner(request, env) {
  const url = String(env.ACCESS_ISSUER || "");
  const audience = String(env.ACCESS_AUD || "");
  if (!/^https:\/\/[a-z0-9-]+\.cloudflareaccess\.com\/?$/.test(url) ||
      !/^[A-Za-z0-9_-]{15,256}$/.test(audience)) throw new Error("AUTH_NOT_CONFIGURED");
  const token = request.headers.get("Cf-Access-Jwt-Assertion") || "";
  if (token.length > 8192) return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  let head, payload;
  try { head = decode(parts[0]); payload = decode(parts[1]); } catch { return null; }
  if (head.alg !== "RS256" || typeof head.kid !== "string" || head.kid.length > 256) return null;
  const now = Math.floor(Date.now() / 1000);
  const issuer = url.replace(/\/$/, "");
  if (payload.iss !== issuer ||
      !(Array.isArray(payload.aud) ? payload.aud.includes(audience) : payload.aud === audience) ||
      !Number.isInteger(payload.exp) || payload.exp <= now ||
      (payload.nbf != null && (!Number.isInteger(payload.nbf) || payload.nbf > now)) ||
      typeof payload.sub !== "string" || payload.sub.length < 6 || payload.sub.length > 256)
    return null;
  if (!keyCache || keyCacheExpiry < Date.now() || keyCache.issuer !== issuer) {
    const reply = await fetch(issuer + "/cdn-cgi/access/certs", { redirect: "error" });
    if (!reply.ok) throw new Error("AUTH_JWKS_UNAVAILABLE");
    const jwks = await reply.json();
    if (!Array.isArray(jwks.keys) || jwks.keys.length > 20) throw new Error("AUTH_JWKS_INVALID");
    keyCache = { issuer, keys: jwks.keys };
    keyCacheExpiry = Date.now() + 5 * 60 * 1000;
  }
  const jwk = keyCache.keys.find(k => k.kid === head.kid && k.kty === "RSA");
  if (!jwk) return null;
  const key = await crypto.subtle.importKey("jwk", jwk, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false, ["verify"]);
  const signature = encode(parts[2]);
  const signed = new TextEncoder().encode(parts[0] + "." + parts[1]);
  if (!await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, signature, signed)) return null;
  return payload.sub;
}
async function singleOwnerAuthenticate(request, env) {
  const configured = String(env.OWNER_KEY_SHA256 || "").toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(configured)) throw new Error("OWNER_KEY_NOT_CONFIGURED");
  const header = request.headers.get("authorization") || "";
  // 32 cryptographically random bytes encoded base64url (43 characters).
  if (!/^Bearer [A-Za-z0-9_-]{43}$/.test(header)) return null;
  const supplied = header.slice(7);
  const digest = await sha(supplied);
  let difference = 0;
  for (let index = 0; index < 64; index++)
    difference |= configured.charCodeAt(index) ^ digest.charCodeAt(index);
  return difference === 0 ? "single-owner-primary" : null;
}
async function authenticateByMode(request, env) {
  if (env.ENVIRONMENT === "single_owner" && env.AUTH_MODE === "single-owner-key")
    return singleOwnerAuthenticate(request, env);
  return jwtOwner(request, env);
}
const forbidden = /^(access.?token|refresh.?token|password|client.?secret|api.?key|accountid|puuid|summonerid|verification.?code)$/i;
function containsSecrets(obj, depth = 0) {
  if (depth > 24) return true;
  if (!obj || typeof obj !== "object") return false;
  return Object.entries(obj).some(([key, value]) =>
    forbidden.test(key) || containsSecrets(value, depth + 1));
}
function validPayload(kind, payload) {
  if (!payload || Array.isArray(payload) || typeof payload !== "object" ||
      payload.SchemaVersion !== 1 && payload.SchemaVersion !== "1" ||
      containsSecrets(payload)) return false;
  if (kind === "app_settings") return !!payload.Settings && typeof payload.Settings === "object" &&
    !Array.isArray(payload.Settings);
  if (kind === "ui_text") return !!payload.Text && !Array.isArray(payload.Text) &&
    typeof payload.Text === "object" && !!payload.Replace &&
    !Array.isArray(payload.Replace) && typeof payload.Replace === "object" &&
    Object.keys(payload.Text).length <= 1000 && Object.keys(payload.Replace).length <= 80;
  if (kind === "game_profile") return !!payload.Esc && typeof payload.Esc === "object" &&
    !Array.isArray(payload.Esc) && Array.isArray(payload.Lobby) &&
    payload.Lobby.length >= 1 && payload.Lobby.length <= 9;
  if (kind === "esc_profile") return Array.isArray(payload.Files) &&
    payload.Files.length >= 1 && payload.Files.length <= 3;
  return false;
}
async function boundedJson(request, maxBytes) {
  const length = request.headers.get("content-length");
  if (length && (+length > maxBytes || !/^\d+$/.test(length))) throw new Error("PAYLOAD_TOO_LARGE");
  if (request.headers.get("content-type")?.split(";")[0].trim() !== "application/json")
    throw new Error("UNSUPPORTED_MEDIA_TYPE");
  const reader = request.body?.getReader();
  if (!reader) throw new Error("BAD_JSON");
  let size = 0;
  const chunks = [];
  try {
    for (;;) {
      const next = await reader.read();
      if (next.done) break;
      size += next.value.byteLength;
      if (size > maxBytes) throw new Error("PAYLOAD_TOO_LARGE");
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const all = new Uint8Array(size);
  let offset = 0;
  for (const part of chunks) { all.set(part, offset); offset += part.byteLength; }
  try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(all)); }
  catch { throw new Error("BAD_JSON"); }
}
const sha = async value => [...new Uint8Array(await crypto.subtle.digest("SHA-256",
  new TextEncoder().encode(value)))].map(v => v.toString(16).padStart(2, "0")).join("");
function checkEnv(env) {
  if (!env?.DB || (env.ENVIRONMENT !== "lab" &&
      !(env.ENVIRONMENT === "single_owner" &&
        env.AUTH_MODE === "single-owner-key" &&
        /^[0-9a-f]{64}$/i.test(String(env.OWNER_KEY_SHA256 || "")))))
    throw new Error("BACKEND_NOT_CONFIGURED");
}
export function createHandler(authenticate = authenticateByMode) {
  return {
    async fetch(request, env) {
      try {
        checkEnv(env);
        const url = new URL(request.url);
        const labRoute = url.pathname.startsWith("/api/lab/");
        const singleRoute = url.pathname.startsWith("/api/v1/");
        if (labRoute && env.ENVIRONMENT !== "lab" ||
            singleRoute && env.ENVIRONMENT !== "single_owner")
          return response(404, { error: "NOT_FOUND" });
        if (url.pathname === "/api/lab/health" && request.method === "GET")
          return response(200, { ok: true, lab: true, storage: "d1", auth: "cloudflare-access" });
        const identityRoute = url.pathname === "/api/v1/whoami";
        const documentRoute = /^\/api\/(?:lab|v1)\/documents\/[a-z_]+$/.test(url.pathname);
        if (!identityRoute && !documentRoute)
          return response(404, { error: "NOT_FOUND" });
        if (identityRoute && request.method !== "GET" ||
            documentRoute && request.method !== "GET" && request.method !== "PUT")
          return response(405, { error: "METHOD_NOT_ALLOWED" });
        const kind = documentRoute ? url.pathname.split("/").pop() : null;
        if (kind && !Object.hasOwn(kinds, kind)) return response(404, { error: "NOT_FOUND" });
        let owner;
        try { owner = await authenticate(request, env); }
        catch { return response(503, { error: "AUTH_UNAVAILABLE" }); }
        if (!owner) return response(401, { error: "UNAUTHORIZED" });
        if (typeof owner !== "string" || owner.length < 6 || owner.length > 256)
          return response(401, { error: "UNAUTHORIZED" });
        if (identityRoute)
          return response(200, { authenticated: true, subject: owner, backend: "d1" });
        if (request.method === "GET") {
          const row = await env.DB.prepare(
            "SELECT version, payload_json, content_hash, updated_at FROM sync_documents WHERE owner_id = ? AND kind = ?"
          ).bind(owner, kind).first();
          if (!row) return response(200, { exists: false, category: kind, remoteVersion: 0,
            nextAction: "explicit_bootstrap_required" });
          return response(200, { exists: true, category: kind, remoteVersion: row.version,
            payload: JSON.parse(row.payload_json), contentHash: row.content_hash, updatedAt: row.updated_at,
            nextAction: "restore_cloud_authoritative" });
        }
        let body;
        try { body = await boundedJson(request, kinds[kind] + 4096); }
        catch (e) { return response(e.message === "PAYLOAD_TOO_LARGE" ? 413 : 400, { error: e.message }); }
        if (!Number.isSafeInteger(body?.expectedVersion) || body.expectedVersion < 0 ||
            body.expectedVersion > 2147483646 || !validPayload(kind, body.payload))
          return response(422, { error: "INVALID_DOCUMENT" });
        const json = JSON.stringify(body.payload);
        if (bytes(json) > kinds[kind]) return response(413, { error: "DOCUMENT_TOO_LARGE" });
        if (body.expectedVersion === 0 && body.explicitBootstrap !== true)
          return response(409, { error: "EXPLICIT_BOOTSTRAP_REQUIRED" });
        const hash = await sha(json);
        const updatedAt = new Date().toISOString();
        let result;
        if (body.expectedVersion === 0) {
          result = await env.DB.prepare(
            "INSERT INTO sync_documents(owner_id, kind, version, payload_json, content_hash, updated_at) " +
            "VALUES (?, ?, 1, ?, ?, ?) ON CONFLICT(owner_id, kind) DO NOTHING"
          ).bind(owner, kind, json, hash, updatedAt).run();
        } else {
          result = await env.DB.prepare(
            "UPDATE sync_documents SET version=version+1, payload_json=?, content_hash=?, updated_at=? " +
            "WHERE owner_id=? AND kind=? AND version=?"
          ).bind(json, hash, updatedAt, owner, kind, body.expectedVersion).run();
        }
        if (result.meta?.changes !== 1)
          return response(409, { error: "REVISION_CONFLICT", nextAction: "fetch_cloud_before_retry" });
        return response(200, { saved: true, category: kind, remoteVersion: body.expectedVersion + 1,
          contentHash: hash, updatedAt });
      } catch {
        // Never echo DB errors, headers, tokens or payloads.
        return response(503, { error: "STORAGE_UNAVAILABLE" });
      }
    }
  };
}
export default createHandler();
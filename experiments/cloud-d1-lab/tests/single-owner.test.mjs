import test from "node:test";
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import handler from "../src/index.mjs";

function memoryDb() {
  const rows = new Map();
  return {
    rows,
    prepare(sql) {
      let args;
      return {
        bind(...params) { args = params; return this; },
        async first() { return rows.get(args.join(":")) || null; },
        async run() {
          if (sql.startsWith("INSERT")) {
            const [owner, kind, value, hash, time] = args;
            const key = owner + ":" + kind;
            if (rows.has(key)) return { meta: { changes: 0 } };
            rows.set(key, { version: 1, payload_json: value, content_hash: hash, updated_at: time });
            return { meta: { changes: 1 } };
          }
          const [value, hash, time, owner, kind, expected] = args;
          const key = owner + ":" + kind;
          const old = rows.get(key);
          if (!old || old.version !== expected) return { meta: { changes: 0 } };
          rows.set(key, { version: expected + 1, payload_json: value, content_hash: hash, updated_at: time });
          return { meta: { changes: 1 } };
        }
      };
    }
  };
}
test("single-user D1 is inaccessible without 256-bit owner key", async () => {
  const key = randomBytes(32).toString("base64url");
  const bad = randomBytes(32).toString("base64url");
  const db = memoryDb();
  const env = { ENVIRONMENT: "single_owner", AUTH_MODE: "single-owner-key", DB: db,
    OWNER_KEY_SHA256: createHash("sha256").update(key).digest("hex") };
  const call = (method, auth, path, data) => handler.fetch(
    new Request("https://example.com" + path, {
      method,
      headers: { ...(auth ? { authorization: "Bearer " + auth } : {}),
        ...(data ? { "content-type": "application/json" } : {}) },
      ...(data ? { body: JSON.stringify(data) } : {})
    }), env);
  const path = "/api/v1/documents/app_settings";
  assert.equal((await call("GET", "", path)).status, 401);
  assert.equal((await call("GET", bad, path)).status, 401);
  assert.equal((await call("GET", key, "/api/lab/documents/app_settings")).status, 404);
  const who = await (await call("GET", key, "/api/v1/whoami")).json();
  assert.equal(who.subject, "single-owner-primary");
  assert.equal((await call("PUT", key, path, { expectedVersion: 0,
    explicitBootstrap: true, payload: { SchemaVersion: 1, Settings: { theme: "dark" } } })).status, 200);
  const saved = await (await call("GET", key, path)).json();
  assert.equal(saved.remoteVersion, 1);
  assert.equal(saved.payload.Settings.theme, "dark");
  assert.equal((await call("PUT", key, path, { expectedVersion: 0,
    explicitBootstrap: true, payload: { SchemaVersion: 1, Settings: {} } })).status, 409);
  assert.equal(db.rows.size, 1);
});
test("single-user API rejects invalid service configuration", async () => {
  const req = new Request("https://example.com/api/v1/whoami");
  const bad = await handler.fetch(req, { ENVIRONMENT: "single_owner", AUTH_MODE: "single-owner-key",
    DB: memoryDb(), OWNER_KEY_SHA256: "NOT_SET" });
  assert.equal(bad.status, 503);
});

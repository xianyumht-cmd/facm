import test from "node:test";
import assert from "node:assert/strict";
import { createHandler } from "../src/index.mjs";

function database() {
  const store = new Map();
  return {
    store,
    prepare(sql) {
      let args;
      return {
        bind(...values) { args = values; return this; },
        async first() {
          const row = store.get(args[0] + ":" + args[1]);
          return row ? { ...row } : null;
        },
        async run() {
          if (sql.startsWith("INSERT")) {
            const [owner, kind, json, hash, stamp] = args;
            const key = owner + ":" + kind;
            if (store.has(key)) return { meta: { changes: 0, rows_written: 0 } };
            store.set(key, { version: 1, payload_json: json, content_hash: hash, updated_at: stamp });
            return { meta: { changes: 1, rows_written: 1 } };
          }
          if (sql.startsWith("UPDATE")) {
            const [json, hash, stamp, owner, kind, expected] = args;
            const key = owner + ":" + kind;
            const current = store.get(key);
            if (!current || current.version !== expected) return { meta: { changes: 0, rows_written: 0 } };
            store.set(key, { version: expected + 1, payload_json: json, content_hash: hash, updated_at: stamp });
            return { meta: { changes: 1, rows_written: 1 } };
          }
          throw new Error("unrecognized SQL: " + sql);
        }
      };
    }
  };
}
const subject = "provider-uid-alice";
const other = "provider-uid-bob";
const app = { SchemaVersion: 1, Settings: { theme: "light" } };
const second = { SchemaVersion: 1, Settings: { theme: "dark" } };
const handler = createHandler(async request => request.headers.get("x-test-owner"));
const ctx = db => ({ ENVIRONMENT: "lab", DB: db });
const call = (db, kind, owner, method = "GET", obj, type = "application/json") =>
  handler.fetch(new Request("http://127.0.0.1/api/lab/documents/" + kind, {
    method, headers: { "x-test-owner": owner, ...(method === "PUT" ? { "content-type": type } : {}) },
    ...(method === "PUT" ? { body: JSON.stringify(obj) } : {})
  }), ctx(db));
const data = async response => ({ status: response.status, body: await response.json() });

test("no user-supplied owner can read another account's data", async () => {
  const db = database();
  assert.equal((await data(await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: app }))).status, 200);
  const otherRead = await data(await call(db, "app_settings", other));
  assert.equal(otherRead.status, 200);
  assert.equal(otherRead.body.exists, false);
  const ownerRead = await data(await call(db, "app_settings", subject));
  assert.equal(ownerRead.body.payload.Settings.theme, "light");
  assert.equal(ownerRead.body.nextAction, "restore_cloud_authoritative");
  assert.equal(db.store.size, 1);
});

test("first use never seeds the server without explicit bootstrap", async () => {
  const db = database();
  const refused = await data(await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, payload: app }));
  assert.equal(refused.status, 409);
  assert.equal(refused.body.error, "EXPLICIT_BOOTSTRAP_REQUIRED");
  assert.equal(db.store.size, 0);
});

test("compare-and-swap rejects stale revisions and concurrent initial writes", async () => {
  const db = database();
  assert.equal((await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: app })).status, 200);
  const raced = await data(await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: second }));
  assert.equal(raced.status, 409);
  assert.equal((await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 1, payload: second })).status, 200);
  const stale = await data(await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 1, payload: app }));
  assert.equal(stale.status, 409);
  assert.equal((await data(await call(db, "app_settings", subject))).body.remoteVersion, 2);
});

test("untrusted owner input and missing Access identity do not access records", async () => {
  const db = database();
  assert.equal((await data(await call(db, "app_settings", "", "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: app }))).status, 401);
  const unknown = await handler.fetch(new Request("http://localhost/api/lab/documents/profile", {
    headers: { "x-test-owner": subject }
  }), ctx(db));
  assert.equal(unknown.status, 404);
  assert.equal(db.store.size, 0);
});

test("rejects oversized data, credential-shaped fields and malformed payloads", async () => {
  const db = database();
  const bad = [
    { SchemaVersion: 1, Settings: { refreshToken: "sample" } },
    { SchemaVersion: 1, Settings: { metadata: { accountId: "personal" } } },
    { SchemaVersion: 1, Settings: [] },
    { SchemaVersion: 2, Settings: {} }
  ];
  for (const payload of bad) {
    assert.equal((await call(db, "app_settings", subject, "PUT",
      { expectedVersion: 0, explicitBootstrap: true, payload })).status, 422);
  }
  const huge = { SchemaVersion: 1, Settings: { a: "x".repeat(34000) } };
  assert.equal((await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: huge })).status, 413);
  assert.equal(db.store.size, 0);
});

test("rejects invalid request methods, wrong content types and production bindings", async () => {
  const db = database();
  const env = ctx(db);
  const url = "http://localhost/api/lab/documents/app_settings";
  assert.equal((await handler.fetch(new Request(url, { method: "DELETE",
    headers: { "x-test-owner": subject } }), env)).status, 405);
  assert.equal((await call(db, "app_settings", subject, "PUT",
    { expectedVersion: 0, explicitBootstrap: true, payload: app }, "text/plain")).status, 400);
  assert.equal((await handler.fetch(new Request(url, {
    headers: { "x-test-owner": subject }
  }), { ...env, ENVIRONMENT: "production" })).status, 503);
});

test("all supported categories have independent per-owner revisions", async () => {
  const db = database();
  const documents = {
    ui_text: { SchemaVersion: 1, Text: {}, Replace: {} },
    esc_profile: { SchemaVersion: 1, Files: [{}] },
    game_profile: { SchemaVersion: 1, Esc: {}, Lobby: [{}] }
  };
  for (const [kind, payload] of Object.entries(documents)) {
    assert.equal((await call(db, kind, subject, "PUT",
      { expectedVersion: 0, explicitBootstrap: true, payload })).status, 200);
    assert.equal((await data(await call(db, kind, subject))).body.remoteVersion, 1);
  }
  assert.equal(db.store.size, 3);
});

test("an untrusted Access header is never used by the exported production handler without signature validation", async () => {
  const { default: productionHandler } = await import("../src/index.mjs");
  const db = database();
  const result = await data(await productionHandler.fetch(
    new Request("http://localhost/api/lab/documents/app_settings", {
      headers: { "x-test-owner": subject, "Cf-Access-Jwt-Assertion": "untrusted" }
    }), { ...ctx(db), ACCESS_ISSUER: "https://example.cloudflareaccess.com",
      ACCESS_AUD: "lab-only-audience-id" }));
  assert.equal(result.status, 401);
  assert.equal(db.store.size, 0);
});

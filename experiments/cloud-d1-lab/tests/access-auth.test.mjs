import test from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import production from "../src/index.mjs";

test("Cloudflare Access JWT signature, issuer, audience and expiry must all verify", async () => {
  const { publicKey, privateKey } = await webcrypto.subtle.generateKey(
    { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
    true, ["sign", "verify"]);
  const jwk = await webcrypto.subtle.exportKey("jwk", publicKey);
  const issuer = "https://ggmanlabtest.cloudflareaccess.com";
  const aud = "ggman-lab-audience-1234567";
  const encode = o => Buffer.from(JSON.stringify(o)).toString("base64url");
  const signedToken = async (payload, useKey = privateKey) => {
    const data = encode({ alg: "RS256", typ: "JWT", kid: "test-key" }) + "." + encode(payload);
    const sig = await webcrypto.subtle.sign("RSASSA-PKCS1-v1_5", useKey, Buffer.from(data));
    return data + "." + Buffer.from(sig).toString("base64url");
  };
  const valid = { sub: "owner-lab-001", iss: issuer, aud, exp: Math.floor(Date.now() / 1000) + 600 };
  const env = { ENVIRONMENT: "lab", ACCESS_ISSUER: issuer, ACCESS_AUD: aud,
    DB: { prepare() { throw new Error("DB must never be invoked on verification failures"); } } };
  const original = globalThis.fetch;
  let fetches = 0;
  globalThis.fetch = async url => {
    assert.equal(url, issuer + "/cdn-cgi/access/certs");
    fetches++;
    return new Response(JSON.stringify({ keys: [{ ...jwk, kid: "test-key" }] }),
      { status: 200, headers: { "content-type": "application/json" } });
  };
  try {
    const request = async token => production.fetch(new Request(
      "http://localhost/api/lab/documents/app_settings", {
        headers: { "Cf-Access-Jwt-Assertion": token }
      }), env);
    const validToken = await signedToken(valid);
    // Signature and claims verify: only then does the test-only mock DB throw (masked as 503).
    assert.equal((await request(validToken)).status, 503);
    assert.equal(fetches, 1);
    assert.equal((await request(validToken.slice(0, -1) + (validToken.endsWith("A") ? "B" : "A"))).status, 401);
    assert.equal((await request(await signedToken({ ...valid, aud: "wrong-aud" }))).status, 401);
    assert.equal((await request(await signedToken({ ...valid, iss: "https://other.cloudflareaccess.com" }))).status, 401);
    assert.equal((await request(await signedToken({ ...valid, exp: 100 }))).status, 401);
    assert.equal((await request("untrusted")).status, 401);
  } finally {
    globalThis.fetch = original;
  }
});

# FACM 4.0 BOOT3-B signed artifact pipeline

BOOT3-B separates deterministic build/package generation from possession of the
release private key. The normal build machine produces a complete unsigned
bundle and an external signing request. A controlled signer returns only
detached Base64 signatures; it never receives repository write access or a
request to copy a private key into the worktree.

## Pipeline order

`tools/release/Build-FacmBoot3BRelease.ps1` reuses the existing BOOT-2 publish,
component classification, CAB/FDI-compatible packaging and content-digest
logic. It then performs the following deterministic steps:

1. produce `facm-app-win-x64`;
2. produce `facm-dotnet-runtime-win-x64`;
3. produce `facm-windows-runtime-win-x64`;
4. write each schema-3 component manifest;
5. compute exact manifest bytes, package bytes, sizes, file counts and digests;
6. write the schema-3 application manifest with component-manifest byte hashes;
7. write `release-index.json` and a non-secret ownership report;
8. write `signing-request.json` containing the exact payload contract.

The default composition is exactly the three core components above. Desktop Pet
payloads are not included. The pipeline does not return to the rejected
monolithic single-file distribution route.

## Output layout

```text
<output>/
  bundle/
    manifest.json
    release-index.json
    ownership-report.json
    manifest.json.sig                 # returned by external signer
    components/<id>/<version>/
      <id>-<version>.cab
      component.manifest.json
      component.manifest.json.sig     # returned by external signer
  signing-request.json
  signer-responses/                   # external response staging, not release output
```

The application and component manifest files are written as UTF-8 without BOM
with one final newline. After writing, their bytes are never parsed and
reserialized by the signing/apply step. CAB bytes are copied unchanged from
the BOOT-2 package stage and their byte count/SHA-256 are recorded.

## External signer request contract

`signing-request.json` is schema 1 and contains no private-key path or secret.
It records:

- release version, architecture, immutable key ID and algorithm;
- exact relative bundle path of every application/component manifest;
- exact payload byte count and SHA-256 for every payload;
- exact expected detached signature path;
- release-index path, byte count and SHA-256;
- `requestStatus=unsigned-external-signer-required`.

The signer must independently re-read each payload, compare its size and
SHA-256 with the request, authorize the release/key ID, sign the exact bytes
with RSA PKCS#1/SHA-256, and return Base64 signatures at the requested relative
paths. `tools/release/Apply-FacmSigningResponses.ps1` verifies the request
inputs and response encoding before writing only the detached signature files;
it never opens a private key.

The local validation test may emulate the external signer with the non-formal
candidate validation key held outside the repository. That path is test
infrastructure, not release signing, and is never the default builder path.

## Offline release-bundle validation

`tools/release/Test-FacmReleaseBundle.ps1` is read-only with respect to
installed FACM state. It checks the release index, required files, exact
application/component relationships, fixed core ownership, HTTPS URLs, key IDs,
package bytes, sizes, file counts, extracted content digest, Base64 signature
shape, and obvious private-key/secret material. It then invokes the native
`--verify-trust-bundle` diagnostic for the authoritative CNG signature and CAB
round-trip validation.

The validator rejects unsigned-local bundles, unsigned manifests, unknown or
test-only key IDs, Desktop Pet in the default composition, changed signed
bytes, replayed component signatures, metadata mismatch, package hash/size
mismatch, unsafe ownership paths and secret-bearing release bundles.

## Determinism and evidence

The release index intentionally excludes wall-clock timestamps and absolute
machine paths. It records the repository commit, logical artifact paths,
exact byte sizes, hashes, content digests and signature paths. Rebuilding from
the same BOOT-2 package/source inputs must produce byte-identical metadata and
bundle files; the BOOT3-B test compares two such outputs before signatures are
applied.

The index and request are review evidence, not runtime trust roots. Runtime
trust still comes only from the compiled native key table and exact-byte
verification in BOOT3-A.

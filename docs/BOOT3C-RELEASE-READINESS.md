# FACM BOOT3-C Release Readiness Contract

Status date: 2026-08-31
Scope: local candidate and production-like distribution rehearsal only
Current production: FACM 3.5.15
Current source baseline: BOOT3-B documentation head `72972f69579e63804c29a3b51a2602b918324a7a`

## Decision summary

BOOT3-C hardens the BOOT3-B signed-artifact path with a real TLS origin, a deterministic mirror path, no automatic
redirects, resumable package delivery, disk-space preflight, safer state handling, local rollback coverage, and a
repeatable evidence harness. It does not publish FACM 4.0, move Formal P7, change production pointers, or run Gate13.

The candidate status vocabulary is deliberately split:

| Status | Meaning in this task |
| --- | --- |
| `PASS_LOCAL_AUTOMATED` | Repository/native deterministic checks pass on the current development host. |
| `PASS_PRODUCTION_LIKE_HTTPS` | The controlled local TLS origin/mirror rehearsal passes; this is not a production CDN claim. |
| `PASS_REAL_MACHINE` | Reviewed evidence exists from the named physical Windows target. |
| `BLOCKED_EXTERNAL_SIGNER` | A real production signing response/key-custody proof is absent. |
| `BLOCKED_RELEASE_OWNER_AUTHORIZATION` | Publication/landing authorization is absent. |
| `BLOCKED_PRODUCTION_INFRA` | Approved CDN, DNS, mirror, certificate, and publishing controls are absent. |
| `NOT_RUN_GATE13` | Formal P7/Gate13 is outside this BOOT3-C task. |

## Distribution and trust topology

```text
bootstrap.json
  primary manifest URL + fixed mirror seed
          |
          v
WinHTTP HTTPS GET (system certificate validation; redirects disabled)
          |
          v
exact application manifest bytes + detached signature
  embedded BOOT3-B key table / key ID
          |
          v
authenticated component-manifest URL, mirror list, package URL, package hash
          |
          v
exact component manifest bytes + detached signature
          |
          v
CAB size/SHA-256 -> FDI extraction -> size/fileCount/contentDigest
          |
          v
fresh composition staging -> atomic active.json commit
```

The origin certificate authenticates the TLS channel only. It does not create a FACM release trust root. Release
identity remains the embedded `facm-production-r1` public key and the exact detached signature over the bytes read by
the bootstrapper. An arbitrary HTTPS mirror cannot become trusted by changing `bootstrap.json`; it must serve bytes
that pass the signed application/component metadata and package checks.

The application manifest contains `manifestMirrors`. Each component contains authenticated package `mirrors` and
`componentManifestMirrors`. The bootstrapper attempts each configured address in declared order. HTTP errors, TLS
errors, malformed responses and 3xx responses do not get followed automatically. Package responses are promoted only
after exact size and SHA-256 verification; a corrupt primary package can therefore fall through to a valid mirror.

## Failure, interruption, and state policy

- A failed download leaves a bounded `.partial` file under `.facm/cache/downloads`; a later attempt can use HTTP
  Range from that prefix. A mismatching completed package is deleted before the next address is tried.
- Extraction occurs in a fresh component staging directory. Failed extraction staging is retained under the controlled
  `.facm/staging` root for diagnosis/retry; stale staging is never treated as an active version.
- Composition is built separately under `composition-<version>`. The previous active pointer is not removed while a
  package, extraction, composition, state write, or activation step is failing.
- `active.json` is atomically replaced only after composition and component state are ready. Its schema, version and
  relative version path are checked before use; component state rejects duplicate IDs, invalid digests, unsafe paths,
  and malformed records.
- Before downloading, the target volume is checked for package/partial space, extracted component staging, composed
  version space, and a 64 MiB safety margin. A low-space result fails before active state changes.
- Remote production downgrade is rejected by numeric release comparison. Same-version evaluation is a no-op when the
  active composition and all authenticated installed component digests match. Explicit local `--activate-version`
  remains the emergency/local rollback mechanism and never fetches a remote release.

The current integration harness covers unavailable primary, corrupt primary package, corrupt mirror rejection with old
active preservation, incomplete `.partial` recovery, stale staging cleanup, same-version no-op, redirect rejection,
local forward-and-rollback, and low-space diagnostics.

## Signing boundary and publication contract

The normal builder must not receive a production private key. It creates the exact-byte bundle and an external signing
request containing only relative payload paths, byte counts, digests, key ID, algorithm and expected signature paths.
The controlled signer must return detached Base64 signatures and an audit record containing, at minimum:

- release ID and release version;
- request/index digest and each signed payload digest;
- key ID and signer authorization record;
- signer identity/custody reference and dual-control approval;
- signature response timestamp and immutable audit/event ID.

No private key, HSM export, password, token, cookie or credential-bearing diagnostic may be placed in Git, a bundle,
the request, logs, CI artifacts, or command arguments. The current local validation key is only a test dependency held
outside the repository. It proves the local cryptographic path; it is not production key-custody evidence.

Approved production publication order, for a later authorized task:

1. Build CABs and manifests from the reviewed source commit.
2. Obtain the external signer response and run the offline bundle validator plus native trust-bundle verification.
3. Publish immutable package blobs to the approved primary and mirror version paths.
4. Verify exact bytes, sizes and hashes independently from both origins.
5. Publish signed component manifests and detached signatures.
6. Publish the signed application manifest and detached signature last among release payloads.
7. Publish release index/online pointers only after release-owner approval and the production change record exists.

Version paths are immutable and content-addressed by the recorded release metadata. Existing production 3.5.15 remains
the rollback baseline until a separately authorized cutover proves otherwise.

## Real-machine acceptance matrix

The wrapper `tools/release/Test-FacmBoot3CRealMachineHarness.ps1` calls the existing read-only collector and emits
`boot3c-acceptance.json`. Every row begins as `manual_required`; automatic observations are not a release decision.

| Area | Required evidence |
| --- | --- |
| Compatibility | Win10 22H2 and controlled Win11, standard-user launch, UAC allow/cancel, Defender and SmartScreen result. |
| Provisioning | Clean directory, fresh provisioning, signed candidate identity, first FACM/Orb launch, second launch with zero download/extraction. |
| Offline | Offline launch after a successful provision; app-only/runtime-only and data-root persistence. |
| Recovery | Failed/corrupt package, interrupted download and resume, failed forward update, local/emergency rollback. |
| Reliability | Shutdown/relaunch, locked destination, low disk, stale/corrupt staging, no active-pointer loss. |
| Product boundary | No Pet payload reintroduced, no tray/League/UI behavior regression, no unauthorized origin or redirect accepted. |

The repository has not performed `PASS_REAL_MACHINE` for BOOT3-C. The existing collector's automatic facts and any
current desktop observations must remain labeled as preparation or manual review until a human runs the matrix on the
intended physical targets.

## Explicit blockers and non-goals

- `BLOCKED_EXTERNAL_SIGNER`: no real controlled production signer response or custody/dual-control evidence is present.
- `BLOCKED_RELEASE_OWNER_AUTHORIZATION`: no authorization to publish, land, or retire 3.5.15 was given.
- `BLOCKED_PRODUCTION_INFRA`: no approved production CDN/DNS/mirror/certificate publication evidence is present.
- `NOT_RUN_GATE13`: Formal P7/Gate13, production cutover and destructive/online checks are intentionally not run.
- Self-update of the bootstrapper is design-only. A future implementation needs a separately signed bootstrapper chain,
  dual slots, recovery/rollback semantics and release-owner approval; BOOT3-C does not implement it.
- No production online/version pointer, GitHub release, merge, push, deploy, restart, HSM invention, TLS bypass,
  arbitrary trust root, Pet reintroduction, monolithic 421 MB package, tray/League/UI rewrite, or MS9 redo is included.

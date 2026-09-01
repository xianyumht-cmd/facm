# FACM 4.0 FREE-DIST-1 — GitHub Release and Free HTTPS Transport

Status as of 2026-09-01: FREE-DIST-2 toolchain revalidation and final non-production candidate preparation passed locally. No GitHub Release was published, no repository push or merge was performed, and production remains FACM 3.5.15.

## Distribution contract

The canonical artifact origin is a GitHub Release. Signed metadata contains only canonical GitHub Release URLs and never contains public proxy hostnames:

```text
https://github.com/xianyumht-cmd/facm/releases/download/<release-tag>/<relative-artifact-path>
```

GitHub Release assets are uploaded as unique flat filenames. The final local test candidate uses these stable names:

```text
manifest.json
manifest.json.sig
facm-app-win-x64-component-manifest.json
facm-app-win-x64-component-manifest.json.sig
facm-app-win-x64-4.0.0-free-dist-test.1.cab
facm-dotnet-runtime-win-x64-component-manifest.json
facm-dotnet-runtime-win-x64-component-manifest.json.sig
facm-dotnet-runtime-win-x64-4.0.0-free-dist-test.1.cab
facm-windows-runtime-win-x64-component-manifest.json
facm-windows-runtime-win-x64-component-manifest.json.sig
facm-windows-runtime-win-x64-4.0.0-free-dist-test.1.cab
release-index.json
ownership-report.json
```

The proposed non-production release identity is tag `v4.0.0-free-dist-test.1`, title `FACM 4.0.0 FREE-DIST test.1`,
and `prerelease=true`. The canonical application manifest URL is:

```text
https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0-free-dist-test.1/manifest.json
```

The launcher-only review directory is:

```text
D:\project2\facm4-free-dist-review-20260831
```

It contains exactly `FACM.exe` and `bootstrap.json`. The local release-compatible signed bundle is:

```text
D:\project2\facm-free-dist-release-20260831\bundle
```

The final flat release candidate bundle is:

```text
D:\project2\facm-free-dist-final-candidate-flat4-20260901\bundle
```

The reproducible preparation and focused test tools are:

```text
tools/release/Prepare-FacmFreeDistCandidate.ps1
tools/release/Test-FacmFreeDistProxyTransport.ps1
```

The candidate uses `facm-production-r1` and the existing BOOT3-A/BOOT3-B detached-signature model. The local validation key is read from outside the repository and is never copied into the launcher or bundle.

## Transport order and safety

For canonical GitHub Release URLs, the bootstrapper tries these fixed HTTPS candidates in order:

1. `ghfast.top`
2. `gh-proxy.com`
3. `gh.llkk.cc`
4. direct GitHub Release URL (`github-direct`)

Proxy endpoints are transport candidates only. They are not trust roots, are not written into signed manifests, and do not change key selection, detached-signature verification, downgrade policy, activation policy, rollback policy, or BOOT3-A trust-bundle rules.

WinHTTP automatic redirects remain disabled. The bootstrapper follows only a tightly controlled HTTPS redirect to the canonical GitHub release host or GitHub-owned release asset hosts (`release-assets.githubusercontent.com` and `objects.githubusercontent.com`). HTTP redirects, user-info URLs, arbitrary hosts, and redirect chains beyond the bounded limit are rejected.

For non-canonical URLs, including explicit local-development URLs, the bootstrapper uses the supplied URL directly and does not add GitHub proxy candidates.

## Resume and verification behavior

An existing partial component file is retained across transport failover. A `206 Partial Content` response is accepted only when its `Content-Range` starts at the requested local offset and its total equals the authenticated component `packageSize`. A `200` response when resuming causes a safe restart rather than unsafe concatenation. Every completed candidate download is checked against the authenticated package SHA-256 and component content metadata; failed or corrupt candidates are removed before the next candidate is attempted.

Application and component manifests are still verified as exact bytes against their detached signatures. A component manifest found through a proxy is verified using the canonical source URL's corresponding detached signature; the proxy response itself is never treated as authority.

## Compatibility evidence

The measurements below were made on 2026-08-31 against a small public GitHub Release asset and the public FACM 3.5.15 `FACM.exe` asset. The small probe was 1,024 bytes from a file of 1,856 bytes; the large probe was 65,536 bytes from a file of 78,624,152 bytes.

| Candidate | Small Range | Large Range | Decision |
|---|---:|---:|---|
| `ghfast.top` | `206`, `0-1023/1856` | `206`, `0-65535/78624152` | selected |
| `gh-proxy.com` | `206`, `0-1023/1856` | `206`, `0-65535/78624152` | selected |
| `gh.llkk.cc` | `206`, `0-1023/1856` | `206`, `0-65535/78624152` | selected |
| direct GitHub | `302` to GitHub release assets | `206`, `0-65535/78624152` after approved HTTPS redirect | mandatory final fallback |
| `ghproxy.net` | TLS handshake failure | TLS handshake failure | excluded |
| `ghproxy.cc` / `ui.ghproxy.cc` | expired TLS certificate | expired TLS certificate | excluded |
| `gh-proxy.net` | `302` to HTTP / error page | error page | excluded |
| `git.yylx.win` | `206` | `200` while ignoring Range | excluded for resume-safe distribution |
| `github.tbedu.top` | TLS handshake failure | TLS handshake failure | excluded |

These are availability observations, not an SLA. Public free proxies can change behavior or disappear; the direct GitHub path remains the final fallback. The prefix syntax was also cross-checked against current public GitHub Release proxy documentation, which warns that third-party proxy availability is not guaranteed: [OpenList GitHub Releases guide](https://doc.oplist.org/guide/drivers/github_releases.html).

## Local evidence

The generated evidence files are:

```text
D:\project2\facm-free-dist-release-20260831\free-dist-evidence.json
D:\project2\facm-free-dist-probe-20260831\free-dist-test-results.json
```

The final candidate evidence and fresh verification outputs are:

```text
D:\project2\facm-free-dist-final-candidate-flat4-20260901\free-dist-evidence.json
D:\project2\facm-free-dist-final-candidate-flat4-boot3c-20260901\results.json
D:\project2\facm-free-dist-final-candidate-flat4-probe-20260901\free-dist-test-results.json
```

Current candidate figures:

- release-compatible bundle: 103,774,544 bytes total;
- three CAB packages: 103,647,538 bytes total;
- launcher-only directory: 3,926,076 bytes total;
- launcher files: `FACM.exe` 3,925,844 bytes and `bootstrap.json` 232 bytes;
- four detached signatures present;
- overall live probe passed after attempting all four candidates; the retained JSON records passing responses from `gh-proxy.com` and `gh.llkk.cc`;
- invalid non-GitHub Release probe URL was rejected;
- existing BOOT3-C HTTPS distribution regression evidence remains 8/8 PASS.

The public GitHub repository currently exposes FACM 3.5.15, not the local `v4.0.0-free-dist-1` candidate. Therefore a clean-machine first-run against the public FREE-DIST-1 release and a second-launch zero-download proof are intentionally not claimed yet. They require a separately authorized GitHub Release publication followed by real-machine acceptance.

The final local candidate uses `v4.0.0-free-dist-test.1` only for a non-production test release. It is signed with the
external local validation key for test evidence; it is not a production signing result. The final launcher-only review
directory is `D:\project2\facm4-free-dist-final-review-flat3-20260901` and contains exactly `FACM.exe` and `bootstrap.json`.
The candidate evidence records the exact source commit used to prepare this bundle.

## Required next action before any release claim

The remaining human-controlled action is to review and explicitly authorize publication of the local flat bundle as a GitHub
Release asset set. After publication, run clean Windows-machine first-run, proxy failover, corrupted-content rejection,
resume, and second-launch zero-download acceptance; only then consider any production pointer or cutover request. Gate13,
Mac acceptance, and production cutover are out of scope for this task.

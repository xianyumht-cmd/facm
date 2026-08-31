# FACM 4.0 BOOT3-A trust boundary

BOOT3-A adds the cryptographic trust boundary for the existing BOOT-2 native component provisioner. It does not change the BOOT-2 component ownership, CAB/FDI extraction, resume, mirror fallback, composition, or installed fast-path design.

## Trust model

There are two intentionally separate manifest modes:

- `production` is schema 3. The bootstrapper requires HTTPS, a `keyId` present in its compiled-in production keyring, and a detached `.sig` beside the application manifest. It never uses `allowUnsignedLocal`, `allowInsecureLocal`, `bootstrap.json`, arbitrary certificate stores, or caller-provided roots to bypass signature verification.
- `unsigned-local` is schema 2. It is accepted only when both explicit local switches are present, the application manifest URL is loopback HTTP, and every component URL is also allowed by that local policy. It is a development boundary, not a fallback from production and not a release mode.

Any other trust mode, an unsigned production manifest, an HTTPS manifest downgraded to `unsigned-local`, or a production manifest served from loopback HTTP is rejected. The normal launch path still reads the active composition and does not fetch a manifest or rehash installed files. Network work occurs only for initial provisioning or an explicit update attempt.

Production component-manifest and package URLs are independently required to be HTTPS; the local HTTP switches never broaden that production rule.

## Signed-byte format

The native bootstrapper uses Windows CNG (`bcrypt.dll`) with RSA-2048 PKCS#1 v1.5 signatures over SHA-256. The signature file contains Base64 text and is detached from the payload. The signed payload is the exact UTF-8 byte sequence received from the server or read from the trust fixture; no JSON canonicalization is used.

The application manifest is signed by `facm-production-r1`. Each production component record in the application manifest contains:

- the HTTPS component-manifest URL;
- the SHA-256 of the exact component-manifest bytes;
- package size and package SHA-256;
- extracted installed size, file count, and `contentDigest`.

Each component manifest is separately signed by the same key identity. The bootstrapper checks its exact-byte digest and detached signature, then compares every authenticated component field with the application record. It verifies the CAB package SHA-256 before extraction and verifies extracted size, file count, and `contentDigest` after native FDI extraction. Thus package and extracted-content identities remain inside the authenticated metadata chain.

The production keyring is bootstrapper-local and currently contains only the public root identified as `facm-production-r1`. Only public modulus/exponent material is compiled into `ManifestTrust.cpp`. The corresponding release private key is controlled outside the repository and must never be committed, embedded, generated into source-controlled fixtures, or copied into review artifacts. Key rotation is an explicit release operation: add the new public identity in a reviewed bootstrapper, sign a transition manifest with the controlled release process, and retire the old identity only after the supported update window.

## Existing signing infrastructure audit

The repository already has:

- `scripts/sign-release.ps1` and `scripts/sign-ci.ps1` for Authenticode signing of PE executables;
- `docs/SIGNING.md` and `docs/SELF-SIGNED-CI-NOTE.md` describing release certificates, CI secrets, and self-signed development output;
- `WindowsUpdatePackageIdentityVerifier` using the embedded EXE signer certificate plus `WinVerifyTrust` for the managed updater.

That mechanism was audited and deliberately not reused for BOOT3-A manifest/component signatures. It authenticates PE file signatures and release identity; it does not provide a detached exact-byte signature format for JSON and CAB metadata, nor a bootstrapper-embedded manifest key identity chain. The existing Authenticode boundary remains the EXE release-signing boundary.

## Test material and evidence

`tools/boot1/Test-Boot3A.ps1` creates all CABs, manifests, detached signatures, and mutations under `D:\project2`. It accepts an externally held local validation key through `-ProductionPrivateKeyPath`; no private key is stored in the repository. The script generates a visibly test-only `facm-test-only-r1` key under its D: test root and proves that production trust rejects it as an unknown identity.

`--verify-trust-bundle` is a bounded bootstrapper verification diagnostic. It verifies a local signed application/component bundle, package SHA-256, native extraction, and extracted content digest without changing active state. The production network path uses the same signature verifier and additionally fetches only HTTPS manifests and their detached signatures.

The focused BOOT3-A tests cover a valid signed application/component path, altered application bytes, altered component bytes, invalid signatures, unknown/test-only key identity, unsigned production input, unsigned-local downgrade, altered authenticated component metadata, corrupted package hash, and failed update preservation of the previous launchable active composition. The existing BOOT-2 smoke remains the regression baseline for unsigned-local development provisioning, mirror fallback, Range resume, offline fast path, and app-only/runtime-only incremental updates.

BOOT3-A does not run Gate13, modify production pointers, publish or release FACM 4.0, merge or push PR #234, move Formal P7, or retire FACM 3.5.15. BOOT3-B must supply controlled release-key custody/rotation, production HTTPS hosting of signed application and component manifests, signed package publication, and fresh real-machine/update evidence before any production cutover decision.

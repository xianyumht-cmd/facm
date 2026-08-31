# FACM 4.0 BOOT3-B release-key governance

This document defines the release-key lifecycle around the BOOT3-A native trust
boundary. It is a policy and review contract; it does not claim that a formal
production secret-storage service already exists.

## Scope and current status

The candidate bootstrapper currently embeds the public identity
`facm-production-r1`. BOOT3-A used an externally held local validation private
key to exercise that public key. That key is not a formal production release
credential, and the candidate must not be promoted merely because the verifier
accepts it.

The runtime trust source is the fixed public-key table compiled into
`src/FACM.Bootstrapper/ManifestTrust.cpp`. The tooling policy at
`tools/release/facm-keyring-policy.json` is review metadata only; it cannot add
runtime trust roots.

The current public-key representation was independently checked on 2026-08-31:

- RSA key size: 2048 bits;
- public exponent: `010001`;
- modulus representation: 256 big-endian bytes in `BCRYPT_RSAKEY_BLOB` order;
- embedded modulus SHA-256: `f3086137e6b315b5b080fd10723b17f63f288d0e8c4a4dc4fce66ff14d3c9f20`;
- embedded modulus matches the external local validation key's public modulus.

The validation key was generated outside Git using the platform RSA provider
and exported as PKCS#8 PEM for local test use. No formal production key was
used, claimed, or placed in the repository, bootstrapper, fixtures, review
artifacts, logs, CI artifacts, or command-line output.

## Identity and algorithm

Every release identity uses the immutable format
`facm-production-r<positive integer>`. The key ID is metadata, not a secret,
and cannot be changed by a manifest, configuration file, environment variable,
remote server, or command-line flag.

The fixed algorithm contract is RSA-2048 or stronger only when explicitly
implemented and reviewed, RSA PKCS#1 v1.5 signatures, SHA-256, detached
Base64 signature files, and exact UTF-8 payload bytes. A key entry also records
its lifecycle status in the compiled table. Unknown, planned, retired, or
revoked identities are rejected by the native verifier.

## Custody and authorization boundary

Build and package generation may run on the normal repository/build machine.
The release private key must remain in a separate controlled signing boundary:
an approved HSM/KMS or an isolated signing service/machine with access control,
operator authentication, audit logging, and no checkout write access. The
available repository does not prove that such a service is currently deployed;
BOOT3-B therefore implements the external-signer request boundary without
fabricating one.

Signing authorization requires a reviewed release/version, exact artifact
digests and byte counts, the requested key ID, two-person or equivalent
approval, and an auditable signer response. The signer must refuse requests
for an unapproved key ID, unexpected path, changed digest, duplicate logical
artifact, or unsupported algorithm.

## Backup policy

Formal production private-key backups must be encrypted, access-controlled,
offline or separately isolated from the build checkout, and recoverable only
under documented dual authorization. Backup restore must be logged and
verified against the public-key fingerprint. Plaintext PEM/PFX files, command
line arguments, repository files, normal build output, fixtures, logs, and
review bundles are prohibited locations for a production private key.

## Public-key distribution and activation

The public key is distributed only through a reviewed bootstrapper build and
the corresponding release evidence. The application/component manifests carry
the immutable key ID; they do not carry an arbitrary public key. A new key is
not active merely because a manifest names it.

Activation requires:

1. public-key review and fingerprint verification;
2. a source change to the compiled trusted-key table;
3. native build and negative/positive rotation tests;
4. exact-byte signed-bundle validation;
5. release-owner approval recorded with the signing request and evidence.

## Rotation, overlap, retirement, and emergency replacement

Rotation is source-controlled and explicit. A future key is first `planned`;
the candidate bootstrapper rejects it. To activate a key, a reviewed
bootstrapper adds its public entry with `active` or `overlap` status. During a
bounded overlap window, both the old and new public identities may be accepted
by that specific bootstrapper, while newly signed artifacts use only the new
key. The overlap end date and rollback owner belong in release evidence.

After the overlap window, a new bootstrapper removes the old entry or marks it
`retired`; `revoked` is used for compromise or emergency replacement and is
never bypassed by a manifest or local setting. A key that cannot validate a
new release does not cause automatic downgrade: the current known-good active
composition remains launchable and the update fails closed.

The current `facm-production-r1` entry is a candidate-active identity only.
Before real production cutover, the release owner must approve the formal
identity, replace/retire the local validation arrangement, and ship the
reviewed bootstrapper containing the intended production public-key table.

## Required signing evidence

Every signing event must retain, without private material:

- release/version and repository commit;
- key ID and public-key fingerprint;
- artifact logical name and relative payload path;
- exact payload byte count and SHA-256;
- signature algorithm and expected signature path;
- signer request digest and response digest;
- authorization reference, operator/service identity, UTC timestamp, and result;
- validator result for the completed bundle.

The evidence must not contain private keys, private-key passwords, tokens,
cookies, raw credentials, or secret-bearing command lines.

## Non-negotiable rejection rules

Production rejects unsigned manifests, `unsigned-local`, insecure HTTP,
`facm-test-only-r1`, unknown/planned/retired/revoked key IDs, mismatched
application/component metadata, modified signed bytes, invalid package hashes,
and downgrade attempts. Local unsigned mode remains a separate explicit
development boundary and cannot add production trust.

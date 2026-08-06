# FACM Rewrite Architecture

## Components

- `MainWindow`: user-facing dashboard and explicit confirmations.
- `PayloadService`: reads the embedded manifest, validates paths and extensions, extracts one selected item, verifies SHA-256, and starts it with current-user or opt-in elevated permissions.
- `MaintenanceService`: inspects and removes only directories owned by FACM under `%LocalAppData%\FACM`; reparse points are rejected.
- `scripts`: reproducible build, hash refresh, Authenticode signing, and verification.

## Security boundaries

- No action runs automatically at application startup.
- The main process uses `asInvoker`.
- Embedded files must be declared in the manifest and have a valid SHA-256.
- Extraction uses a versioned local application-data path rather than a random system temporary directory.
- Existing extracted files are reused only when their hash matches.
- Maintenance paths cannot escape the FACM data root.
- No packer, shellcode loader, hidden execution, security-product exclusion, or third-party installation-directory deletion is included.

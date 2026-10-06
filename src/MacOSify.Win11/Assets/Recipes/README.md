# Verified local recipes

macOSify does not redistribute Apple artwork, cursor packs, third-party icon packs,
or opaque system patchers. The Icons and Cursors switches become available only
when a locally reviewed, reversible recipe is present.

Create either `icons` or `cursors` below this directory. Each recipe directory
must contain `recipe.json`, an apply script, and a revert script:

```json
{
  "id": "cursors",
  "displayName": "Licensed cursor pack",
  "applyScript": "apply.ps1",
  "applySha256": "64 lowercase or uppercase hexadecimal SHA-256 characters",
  "revertScript": "revert.ps1",
  "revertSha256": "64 lowercase or uppercase hexadecimal SHA-256 characters",
  "assets": [
    {
      "path": "pack\\cursor-file.cur",
      "sha256": "64 lowercase or uppercase hexadecimal SHA-256 characters"
    }
  ]
}
```

Rules enforced by the app:

- Script paths must remain inside their recipe directory.
- Both scripts must exist and match the pinned SHA-256 values before the restore
  point or any system change is attempted.
- Every payload consumed by either script must be listed in `assets`; all payload
  hashes are verified during preflight and again from the recovery copy.
- Scripts run with Windows PowerShell 5.1, `-NoProfile`, and a 15-minute timeout.
- A failed or cancelled apply invokes the verified revert script from the durable
  operation journal.
- The revert script and declared payloads are copied into the transaction's
  protected ProgramData recovery directory before apply. Revert scripts must be
  idempotent and safe after a partially completed apply.

Only use assets and scripts that you are licensed to use and have reviewed. A
recipe is trusted code running as Administrator; a checksum proves identity, not
safety.

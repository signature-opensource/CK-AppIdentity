# Running the tests on Unix

Part of the code behaves differently on Unix and must be tested there: the store permissions
(owner-only `0700`/`0600` private store, setgid group shared store, umask correction) and the
`FileLock` (a direct `flock(2)` that must work even when `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1`).

## From Windows (WSL)

```powershell
.\Tests\Unix\Run-UnixTests.ps1
```

If running scripts is disabled on the machine (the default execution policy), use:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tests\Unix\Run-UnixTests.ps1
```

Everything is automated and idempotent:

1. **WSL is installed if needed.** This requires administrator rights (a UAC prompt) and a reboot:
   the script stops and must be run again after the reboot.
2. **A dedicated `CK-UnixTests` distribution is created** from the latest Ubuntu 24.04 WSL image
   (downloaded from https://releases.ubuntu.com/noble/, SHA256 checked, cached in
   `%LOCALAPPDATA%\CK-UnixTests\images`). It is independent of any other distribution and lives in
   `%LOCALAPPDATA%\CK-UnixTests\CK-UnixTests`.
3. **It is provisioned** by [provision.sh](provision.sh): packages, a regular `tester` user and the
   .NET SDK required by `global.json`. This is done again only when `provision.sh` or `global.json`
   changes.
4. **The tests run** with [run-tests.sh](run-tests.sh) as `tester`, see below.

Options:

| Option | |
|---|---|
| `-Linux alpine-minimal` | Tests on Alpine (musl, BusyBox, no ICU: the .NET globalization is invariant) in the `CK-UnixTests-alpine-minimal` distribution, created from the Alpine mini root file system (about 4 MB). The default is `ubuntu` (glibc). |
| `-Reset` | Deletes the distribution and creates it again. |
| `-Provision` | Provisions again even if nothing changed. |
| `-Filter FullyQualifiedName~FileStore` | A `dotnet test --filter` expression. |
| `-TestArguments '--x','y'` | Additional `dotnet test` arguments (from a PowerShell session only: `powershell -File` can't pass an array). |
| `-Distribution <name>` / `-UbuntuRelease <codename>` | Another distribution name or Ubuntu release. |

The results of a distribution are in `Tests/Unix/.results/<distribution>/`.

To remove everything: `wsl --unregister CK-UnixTests` (and `CK-UnixTests-alpine-minimal`) and delete
`%LOCALAPPDATA%\CK-UnixTests`.

## On Linux or macOS

```bash
Tests/Unix/run-tests.sh
```

The .NET SDK required by `global.json`, `rsync` and (for the cross-process lock tests) the `flock`
command of util-linux must be installed: on Debian/Ubuntu or Alpine, `sudo Tests/Unix/provision.sh "$PWD" "$USER"`
does it. On macOS, there is no `flock` command: the cross-process lock test is ignored.

## What run-tests.sh does

- It refuses to run as root: root bypasses the permission checks that the store relies on.
- When the sources are on a file system that doesn't support the Unix permissions and locks (a WSL
  `/mnt/c` drive, a network or FUSE mount), they are copied with `rsync` into `--work` (by default
  `~/src/CK-AppIdentity`) and the tests run there.
- The tests run 3 times:
  - `default`: the regular run (umask `022`).
  - `no-dotnet-locking`: with `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1` (the .NET `FileShare` emulation is disabled).
  - `umask-027`: a restrictive umask must not narrow the group permissions of a shared store.
- The `.trx` results and the test logs are in `Tests/Unix/.results/<run time>/` (ignored by git).
  The exit code is 0 when all the passes succeeded.

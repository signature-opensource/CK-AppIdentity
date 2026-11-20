# Running the tests on Unix

Part of the code behaves differently on Unix and must be tested there: the store permissions (owner-only
`0700`/`0600` private store, setgid group shared store, umask correction) and the `FileLock` (a direct `flock(2)` that
must work even when `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1`).

The tests run with the Unix tools of the Stack: see
[Running the tests of a repository on Unix](https://github.com/signature-opensource/CK-Stack/blob/main/Tools/Unix/README.md)
(the WSL distribution `CK-UnixTests`, its provisioning, the options, the results).

From Windows, in this repository:

```powershell
.\Tests\Unix\Run-UnixTests.ps1
```

[Run-UnixTests.ps1](Run-UnixTests.ps1) finds the Stack repository above this one and runs its `Tools/Unix/Run-UnixTests.ps1`
on this repository, with the same options (`-Linux alpine-minimal`, `-Reset`, `-Filter ...`).

What is specific to CK-AppIdentity is in [unix-tests.conf](unix-tests.conf): its test project and its 3 passes.

| Pass | |
|---|---|
| `default` | The regular run (umask `022`). |
| `no-dotnet-locking` | `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1`: the .NET `FileShare` emulation is disabled, the store locks must still exclude each other (`FileLock` uses `flock(2)` directly). |
| `umask-027` | A restrictive umask must not narrow the group permissions of a shared store. |

Each pass starts with a fresh `TestStore`.

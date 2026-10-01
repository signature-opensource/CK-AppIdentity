#!/usr/bin/env bash
# Runs the CK-AppIdentity tests on Linux or macOS, as a regular user, from a native file system.
#
#   run-tests.sh [--source <dir>] [--work <dir>] [--results <dir>] [-- <extra dotnet test arguments>]
#
# --source   The repository to test. Defaults to the repository that contains this script.
#            When it is on a file system that doesn't support the Unix permissions or locking
#            semantics (a WSL /mnt/c drive, a network or FUSE mount), use --work.
# --work     Copies (rsync) the source into this directory and runs the tests there.
#            Defaults to ~/src/CK-AppIdentity when the source is on a WSL Windows drive.
# --results  Where the .trx results and the test logs are copied. Defaults to <source>/Tests/Unix/.results.
#
# The tests run 3 times:
#   - "default": the regular run (umask 022).
#   - "no-dotnet-locking": with DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1, the .NET FileShare emulation is
#     disabled: the store locks must still exclude each other (FileLock uses flock(2) directly).
#   - "umask-027": a restrictive umask must not narrow the group permissions of a shared store.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_dir="$(cd "$script_dir/../.." && pwd)"
work_dir=""
results_dir=""
extra_args=()

while [ $# -gt 0 ]; do
    case "$1" in
        --source) source_dir="$(cd "$2" && pwd)"; shift 2 ;;
        --work) work_dir="$2"; shift 2 ;;
        --results) results_dir="$2"; shift 2 ;;
        --) shift; extra_args=("$@"); break ;;
        *) echo "Unknown argument '$1'." >&2; exit 2 ;;
    esac
done
results_dir="${results_dir:-$source_dir/Tests/Unix/.results}"

if [ "$(id -u)" -eq 0 ]; then
    echo "The tests must not run as root: root bypasses the permission checks that the store relies on." >&2
    exit 1
fi
command -v dotnet > /dev/null || { echo "dotnet is not installed (see provision.sh)." >&2; exit 1; }

fs_type() {
    if [ "$(uname -s)" = "Linux" ]; then stat -f -c %T "$1"; else echo "native"; fi
}

source_fs="$(fs_type "$source_dir")"
case "$source_fs" in
    # BusyBox's stat (Alpine) reports a WSL Windows drive as UNKNOWN: copying is always safe.
    v9fs|9p|drvfs|fuseblk|nfs|cifs|smb2|UNKNOWN)
        work_dir="${work_dir:-$HOME/src/CK-AppIdentity}" ;;
esac

if [ -n "$work_dir" ]; then
    echo "== Copying '$source_dir' ($source_fs) to '$work_dir'"
    mkdir -p "$work_dir"
    # The modes of a Windows drive are meaningless: they are normalized.
    rsync -a --delete --no-perms --no-owner --no-group --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r \
          --exclude 'bin/' --exclude 'obj/' --exclude '.vs/' \
          --exclude 'Tests/**/TestStore/' --exclude 'Tests/**/Logs/' --exclude 'Tests/Unix/.results/' \
          "$source_dir/" "$work_dir/"
    test_dir="$work_dir"
else
    test_dir="$source_dir"
fi

work_fs="$(fs_type "$test_dir")"
case "$work_fs" in
    v9fs|9p|drvfs|fuseblk|nfs|cifs|smb2)
        echo "'$test_dir' is on a '$work_fs' file system: the Unix permissions and locks can't be tested there. Use --work." >&2
        exit 1 ;;
esac

project="$test_dir/Tests/CK.AppIdentity.Tests"
run_id="$(date -u +%Y%m%dT%H%M%SZ)"
run_results="$results_dir/$run_id"
mkdir -p "$run_results"

echo "== $(uname -sm), $(dotnet --version), user $(id -un), tests in '$test_dir' ($work_fs)"
echo "== Build"
dotnet build "$project" -c Debug -nologo -v quiet

failed=()
run_pass() {
    local name="$1"; shift
    echo "== Pass '$name'"
    # Each pass starts with a fresh test store.
    rm -rf "$project/TestStore"
    if ( "$@" dotnet test "$project" -c Debug --no-build -nologo \
                --logger "trx;LogFileName=$name.trx" --results-directory "$run_results" ${extra_args[@]+"${extra_args[@]}"} ); then
        echo "== Pass '$name' succeeded."
    else
        echo "== Pass '$name' FAILED."
        failed+=("$name")
    fi
    if [ -d "$project/Logs" ]; then
        mkdir -p "$run_results/$name-Logs"
        cp -r "$project/Logs/." "$run_results/$name-Logs/"
        rm -rf "$project/Logs"
    fi
}

with_umask_022() { ( umask 022; "$@" ); }
with_umask_027() { ( umask 027; "$@" ); }
without_dotnet_locking() { ( umask 022; DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1 "$@" ); }

run_pass default with_umask_022
run_pass no-dotnet-locking without_dotnet_locking
run_pass umask-027 with_umask_027

echo "== Results in '$run_results'"
if [ ${#failed[@]} -gt 0 ]; then
    echo "== FAILED passes: ${failed[*]}"
    exit 1
fi
echo "== All passes succeeded."

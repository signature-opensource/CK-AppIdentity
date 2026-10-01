#!/usr/bin/env bash
# Provisions a Debian/Ubuntu machine (typically the dedicated "CK-UnixTests" WSL distribution created
# by Run-UnixTests.ps1) to run the CK-AppIdentity tests. Must run as root. Idempotent.
#
#   provision.sh <repository-root> [user-name]
#
# - Installs the required packages (rsync to copy the sources, util-linux for the flock command
#   used by the cross-process lock tests, ICU for .NET).
# - Creates a regular user (default "tester"): the tests must not run as root, root bypasses the
#   permission checks that the store relies on.
# - Installs the .NET SDK required by the repository's global.json in /usr/share/dotnet.
# - In WSL, configures /etc/wsl.conf: the default user and no Windows PATH (a Windows dotnet.exe
#   must never be picked). The distribution must be restarted for this to apply (wsl --terminate).
set -euo pipefail

repo="${1:?Usage: provision.sh <repository-root> [user-name]}"
user="${2:-tester}"

if [ "$(id -u)" -ne 0 ]; then
    echo "provision.sh must run as root." >&2
    exit 1
fi
if ! command -v apt-get > /dev/null; then
    echo "provision.sh supports Debian/Ubuntu (apt-get) only. Install rsync, flock (util-linux), ICU and the .NET SDK manually." >&2
    exit 1
fi

echo "== Packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends ca-certificates curl rsync util-linux libicu-dev git > /dev/null

echo "== User '$user'"
if ! id "$user" > /dev/null 2>&1; then
    useradd --create-home --shell /bin/bash "$user"
fi

echo "== .NET SDK (from $repo/global.json)"
installer=/tmp/dotnet-install.sh
curl -sSL --retry 3 https://dot.net/v1/dotnet-install.sh -o "$installer"
# Remove the CRs: global.json is checked out with CRLF (see .gitattributes).
tr -d '\r' < "$repo/global.json" > /tmp/global.json
bash "$installer" --jsonfile /tmp/global.json --install-dir /usr/share/dotnet --no-path > /dev/null
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
cat > /etc/profile.d/dotnet.sh << 'EOF'
export DOTNET_ROOT=/usr/share/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
EOF
dotnet --version

if grep -qi microsoft /proc/version 2> /dev/null; then
    echo "== WSL configuration"
    cat > /etc/wsl.conf << EOF
[user]
default=$user

[interop]
appendWindowsPath=false

[boot]
systemd=false
EOF
    # The Ubuntu WSL image may ask interactively to create a user on its first launch (OOBE):
    # the user is created above.
    if [ -f /etc/wsl-distribution.conf ]; then
        sed -i '/^\[oobe\]/,/^\[/{/^[[:space:]]*command[[:space:]]*=/d}' /etc/wsl-distribution.conf
    fi
fi

echo "== Provisioned."

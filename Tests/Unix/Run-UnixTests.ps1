#Requires -Version 5.1
<#
.SYNOPSIS
Runs the CK-AppIdentity tests on Linux, in a dedicated WSL distribution.

.DESCRIPTION
Everything is automated and idempotent:
  1. Installs WSL if needed (this requires administrator rights - a UAC prompt - and a reboot:
     the script stops and must be run again after the reboot).
  2. Creates the dedicated distribution (default "CK-UnixTests") from the latest Ubuntu WSL image
     of the release (its SHA256 is checked). The distribution is independent of any other one:
     it can be reset at any time with -Reset. It lives in %LOCALAPPDATA%\CK-UnixTests.
  3. Provisions it (see provision.sh): packages, a regular "tester" user and the .NET SDK required
     by global.json. This is done again only when provision.sh or global.json changes (or with -Provision).
  4. Runs run-tests.sh as "tester": the working tree is copied into the Linux file system (the Windows
     drives don't support the Unix permissions and locks) and the tests run 3 times (default,
     DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1, umask 027).

The .trx results and the test logs are in Tests\Unix\.results\<run time>.
The exit code is 0 when all the passes succeeded.

.PARAMETER Distribution
The name of the dedicated WSL distribution.

.PARAMETER UbuntuRelease
The Ubuntu release code name (see https://releases.ubuntu.com/).

.PARAMETER Reset
Unregisters (deletes) the distribution and creates it again.

.PARAMETER Provision
Provisions the distribution even if provision.sh and global.json didn't change.

.PARAMETER Filter
A "dotnet test --filter" expression (like "FullyQualifiedName~FileStore").

.PARAMETER TestArguments
Additional arguments for "dotnet test". This is an array: it can't be used with "powershell -File"
(that passes '--x','y' as a single string), only from a PowerShell session.

.EXAMPLE
.\Tests\Unix\Run-UnixTests.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Tests\Unix\Run-UnixTests.ps1 -Filter FullyQualifiedName~AtomicWriteAndLock
#>
[CmdletBinding()]
param(
    [string] $Distribution = 'CK-UnixTests',
    [string] $UbuntuRelease = 'noble',
    [switch] $Reset,
    [switch] $Provision,
    [string] $Filter,
    [string[]] $TestArguments = @()
)
if( $Filter ) { $TestArguments = @( '--filter', $Filter ) + $TestArguments }

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
# wsl.exe writes UTF-16 by default.
$env:WSL_UTF8 = '1'

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$home_ = Join-Path $env:LOCALAPPDATA 'CK-UnixTests'
$user = 'tester'

function Write-Step( [string] $message ) { Write-Host "== $message" -ForegroundColor Cyan }

# Runs wsl.exe, its output goes to the console (not to the pipeline): returns the exit code.
function Invoke-Wsl( [string[]] $arguments ) {
    & wsl.exe @arguments | Out-Host
    return $LASTEXITCODE
}

# Runs wsl.exe and returns its standard output lines (null on error). The standard error is ignored:
# in Windows PowerShell, redirecting a native command's stderr raises errors.
function Get-WslOutput( [string[]] $arguments ) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & wsl.exe @arguments 2> $null
        if( $LASTEXITCODE -ne 0 ) { return $null }
        return @( $lines | ForEach-Object { ($_ -replace "`0", '').Trim() } | Where-Object { $_ } )
    }
    catch { return $null }
    finally { $ErrorActionPreference = $previous }
}

# Windows PowerShell returns the content as a byte array when the server sends no text content type.
function Get-WebText( [string] $uri ) {
    $content = (Invoke-WebRequest -Uri $uri -UseBasicParsing).Content
    if( $content -is [byte[]] ) { return [Text.Encoding]::UTF8.GetString( $content ) }
    return [string]$content
}

function Get-Distributions {
    $names = Get-WslOutput '--list', '--quiet'
    if( $null -eq $names ) { return @() }
    return $names
}

# 1. WSL itself (the Windows inbox wsl.exe stub only supports --install).
if( $null -eq (Get-WslOutput '--version') ) {
    Write-Step 'WSL is not installed: installing it (administrator rights are required).'
    $p = Start-Process -FilePath 'wsl.exe' -ArgumentList '--install', '--no-distribution' -Verb RunAs -Wait -PassThru
    if( $p.ExitCode -ne 0 ) { throw "WSL installation failed (exit code $($p.ExitCode))." }
    Write-Host 'WSL has been installed. Restart Windows, then run this script again.' -ForegroundColor Yellow
    exit 3
}

# 2. The dedicated distribution.
$exists = (Get-Distributions) -contains $Distribution
if( $exists -and $Reset ) {
    Write-Step "Unregistering '$Distribution'."
    if( (Invoke-Wsl '--unregister', $Distribution) -ne 0 ) { throw "Unable to unregister '$Distribution'." }
    $exists = $false
}
if( -not $exists ) {
    $arch = if( $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' ) { 'arm64' } else { 'amd64' }
    $index = "https://releases.ubuntu.com/$UbuntuRelease/"
    Write-Step "Looking for the latest Ubuntu '$UbuntuRelease' WSL image ($arch) in $index"
    $page = Get-WebText $index
    $pattern = "ubuntu-(\d+\.\d+(?:\.\d+)?)-wsl-$arch\.wsl"
    $latest = [regex]::Matches( $page, $pattern ) |
                ForEach-Object { [pscustomobject]@{ Name = $_.Value; Version = [version]$_.Groups[1].Value } } |
                Sort-Object Version -Unique | Select-Object -Last 1
    if( -not $latest ) { throw "No '$pattern' image found in $index." }

    $images = Join-Path $home_ 'images'
    New-Item -ItemType Directory -Force -Path $images | Out-Null
    # wsl --import expects a tar file: the .wsl image is a .tar.gz.
    $image = Join-Path $images ($latest.Name -replace '\.wsl$', '.tar.gz')
    $sumsUrl = "$($index)SHA256SUMS"
    $sums = Get-WebText $sumsUrl
    $expected = ([regex]::Match( $sums, "(?m)^([0-9a-f]{64}) \*?$([regex]::Escape( $latest.Name ))\s*$" )).Groups[1].Value
    if( -not $expected ) { throw "No SHA256 for '$($latest.Name)' in $sumsUrl." }
    if( -not (Test-Path $image) -or (Get-FileHash $image -Algorithm SHA256).Hash -ne $expected ) {
        Write-Step "Downloading $($latest.Name)."
        Invoke-WebRequest -Uri "$index$($latest.Name)" -OutFile $image -UseBasicParsing
        $actual = (Get-FileHash $image -Algorithm SHA256).Hash
        if( $actual -ne $expected ) {
            Remove-Item $image
            throw "SHA256 mismatch for '$($latest.Name)': expected $expected, got $actual."
        }
    }
    $location = Join-Path $home_ $Distribution
    Write-Step "Creating the '$Distribution' distribution in '$location'."
    New-Item -ItemType Directory -Force -Path $location | Out-Null
    if( (Invoke-Wsl '--import', $Distribution, $location, $image, '--version', '2') -ne 0 ) { throw "Unable to import '$Distribution'." }
}

# The repository, seen from the distribution.
# (a function that returns a single line returns a string, not an array: @() ensures an array.)
$wslRepository = @( Get-WslOutput '-d', $Distribution, '-u', 'root', '--exec', 'wslpath', '-a', ($repository -replace '\\', '/') )
if( $wslRepository.Count -eq 0 -or -not $wslRepository[0] ) { throw "Unable to compute the WSL path of '$repository'." }
$wslRepository = $wslRepository[0]

# 3. Provisioning (only when needed).
$hash = (Get-FileHash -Algorithm SHA256 -InputStream ([IO.MemoryStream]::new( [Text.Encoding]::UTF8.GetBytes(
            (Get-Content -Raw (Join-Path $PSScriptRoot 'provision.sh')) + (Get-Content -Raw (Join-Path $repository 'global.json')) )))).Hash
$provisioned = Get-WslOutput '-d', $Distribution, '-u', 'root', '--exec', 'bash', '-c', 'cat /etc/ck-unixtests.provisioned 2> /dev/null || true'
if( $Provision -or "$provisioned".Trim() -ne $hash ) {
    Write-Step "Provisioning '$Distribution'."
    $command = "tr -d '\r' < '$wslRepository/Tests/Unix/provision.sh' > /tmp/provision.sh && bash /tmp/provision.sh '$wslRepository' $user && echo $hash > /etc/ck-unixtests.provisioned"
    if( (Invoke-Wsl '-d', $Distribution, '-u', 'root', '--exec', 'bash', '-c', $command) -ne 0 ) { throw "Provisioning of '$Distribution' failed." }
    # /etc/wsl.conf is read when the distribution starts.
    Invoke-Wsl '--terminate', $Distribution | Out-Null
}

# 4. The tests.
Write-Step "Running the tests in '$Distribution' as '$user'."
$extra = ''
if( $TestArguments.Count -gt 0 ) {
    $extra = ' -- ' + (($TestArguments | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join ' ')
}
$command = "tr -d '\r' < '$wslRepository/Tests/Unix/run-tests.sh' > /tmp/run-tests.sh && bash /tmp/run-tests.sh --source '$wslRepository' --work ~/src/CK-AppIdentity --results '$wslRepository/Tests/Unix/.results'$extra"
$exitCode = Invoke-Wsl '-d', $Distribution, '-u', $user, '--exec', 'bash', '-lc', $command
if( $exitCode -eq 0 ) { Write-Host 'All the Unix test passes succeeded.' -ForegroundColor Green }
else { Write-Host "Unix tests FAILED (exit code $exitCode). See Tests\Unix\.results." -ForegroundColor Red }
exit $exitCode

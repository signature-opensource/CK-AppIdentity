#Requires -Version 5.1
<#
.SYNOPSIS
Runs the CK-AppIdentity tests on Linux: the Run-UnixTests.ps1 of the Stack (Tools/Unix), on this repository.

.DESCRIPTION
The Stack repository (.PublicStack or .PrivateStack) is found above this repository. The arguments are the ones of the
Stack's script (-Linux alpine-minimal, -Reset, -Provision, -Filter...). The specifics of CK-AppIdentity (its test
project, its passes) are in unix-tests.conf.

.EXAMPLE
.\Tests\Unix\Run-UnixTests.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Tests\Unix\Run-UnixTests.ps1 -Filter FullyQualifiedName~AtomicWriteAndLock
#>
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tool = $null
for( $d = Split-Path $repository -Parent; $d -and -not $tool; $d = Split-Path $d -Parent ) {
    foreach( $stack in '.PublicStack', '.PrivateStack' ) {
        $candidate = Join-Path $d "$stack\Tools\Unix\Run-UnixTests.ps1"
        if( Test-Path $candidate ) { $tool = $candidate; break }
    }
}
if( -not $tool ) { throw "No Stack repository with Tools\Unix\Run-UnixTests.ps1 above '$repository'." }
& $tool -Repository $repository @args
exit $LASTEXITCODE

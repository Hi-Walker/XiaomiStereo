[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string[]]$TestExe,
    [Parameter(Mandatory=$true)][string]$LogDirectory,
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 120,
    [string[]]$TestArgument = @(),
    [string]$WorkingDirectory = '',
    [switch]$Fixture
)
$ErrorActionPreference = 'Stop'
# No SwitchDesktop, shell launches, system audio, or production-app operations.
if ($Fixture -and $TestArgument.Count -eq 0) { $TestArgument = @('--fixture') }
if (-not ('PrivateDesktopRunner' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'PrivateDesktopRunner.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')
}
$logRoot = [IO.Path]::GetFullPath($LogDirectory)
[void][IO.Directory]::CreateDirectory($logRoot)
$results = @()
$failed = $false
$counter = 0
foreach ($test in $TestExe) {
    $exe = (Resolve-Path -LiteralPath $test -ErrorAction Stop).Path
    $name = [IO.Path]::GetFileNameWithoutExtension($exe)
    # Prevent accidentally running the installed application or a hardware test.
    if ($name -notmatch '^(AudioTests|RoutingTests|LifecycleTests|VolumeTests|StateDirectoryTests|ActivationTests|SettingsTests|ShutdownPowerTests)$' -and -not ($Fixture -and $name -eq 'PrivateDesktopFixture')) {
        throw "Only the offline test executables or the explicit harmless fixture are allowed: $exe"
    }
    $cwd = if ($WorkingDirectory) { (Resolve-Path -LiteralPath $WorkingDirectory).Path } else { [IO.Path]::GetDirectoryName($exe) }
    $prefix = '{0:D2}-{1}' -f (++$counter),$name
    $result = [PrivateDesktopRunner]::Run($exe,[string[]]$TestArgument,$cwd,(Join-Path $logRoot "$prefix.stdout.log"),(Join-Path $logRoot "$prefix.stderr.log"),$TimeoutSeconds)
    $results += $result
    $result | Select-Object Executable,ExitCode,TimedOut,ElapsedMilliseconds,Desktop,DesktopClosed,InputBefore,InputAfter
    if ($result.ExitCode -ne 0 -or -not $result.DesktopClosed) { $failed=$true }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logRoot 'results.json') -Encoding UTF8
if ($failed) { exit 1 }
exit 0

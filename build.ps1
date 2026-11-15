param([string]$ToolCache = (Join-Path $env:LOCALAPPDATA 'XiaomiStereo.Build'), [switch]$Tests, [switch]$CompileTestsOnly)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
function Confirm-Hash($path, $expected) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Hash mismatch: $path" }
}
function Get-Tool($id, $version, $hash) {
    $zip = Join-Path $ToolCache "$id.$version.zip"
    $dir = Join-Path $ToolCache "$id.$version"
    if (!(Test-Path -LiteralPath $zip)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile "$zip.part"
        Confirm-Hash "$zip.part" $hash
        Move-Item -LiteralPath "$zip.part" -Destination $zip -Force
    }
    Confirm-Hash $zip $hash
    Expand-Archive -LiteralPath $zip -DestinationPath $dir -Force
    return $dir
}
[void][IO.Directory]::CreateDirectory($ToolCache)
$compiler = Get-Tool 'microsoft.net.compilers' '3.11.0' 'E4A540A9CB41D15E6E1CC05FA2CADB847454DF9EBE49D683CDED2780506EF880'
$pack = Get-Tool 'microsoft.netframework.referenceassemblies.net48' '1.0.3' '8A7E348538E7EB91351696911689F49E3D4F63F8BAB517432BBE159B8B1104A2'
$refs = Join-Path $pack 'build\.NETFramework\v4.8'
Confirm-Hash "$root\lib\NAudio.Core.dll" 'F297E28C7061E4294FC65A6BE1629D951B0496950F0060C919FD9F305F27AA91'
Confirm-Hash "$root\lib\NAudio.Wasapi.dll" 'C25E5C0694DC709C72FE01182DB5CCFEA9229F65BC7DEAA9B0298234C84A8FC8'
$out = Join-Path $root 'release'
[void][IO.Directory]::CreateDirectory($out)
$arguments = @('/noconfig','/nostdlib+','/langversion:5','/nologo','/codepage:65001','/deterministic+','/optimize+','/unsafe','/platform:x64',"/pathmap:$root=/_/XiaomiStereo")
foreach ($ref in 'mscorlib','System','System.Core','System.Configuration','System.Windows.Forms','System.Drawing','System.Web.Extensions','System.Runtime.Serialization','System.Xml','Facades\netstandard') { $arguments += "/reference:$refs\$ref.dll" }
foreach ($dll in 'NAudio.Core.dll','NAudio.Wasapi.dll') {
    $arguments += "/reference:$root\lib\$dll"
    Copy-Item -LiteralPath "$root\lib\$dll" -Destination $out -Force
}
$source = @(Get-ChildItem -LiteralPath "$root\src" -Filter *.cs | Sort-Object Name | ForEach-Object FullName)
$icon = "$root\src\XiaomiStereo.ico"
& "$compiler\tools\csc.exe" @arguments /target:winexe /main:StereoProgram "/out:$out\小米立体声.exe" "/win32icon:$icon" "/resource:$icon,XiaomiStereo.ico" @source
if ($LASTEXITCODE) { throw "Release compilation failed" }
Copy-Item -LiteralPath "$root\app.config" -Destination "$out\小米立体声.exe.config" -Force
Copy-Item -LiteralPath "$root\licenses\NAudio.txt" -Destination $out -Force
if ($Tests -or $CompileTestsOnly) {
    $testOut = Join-Path $root 'test-bin'
    [void][IO.Directory]::CreateDirectory($testOut)
    Copy-Item "$root\lib\*.dll" -Destination $testOut -Force
    foreach ($test in (Get-ChildItem -LiteralPath "$root\tests" -Filter *.cs | Sort-Object Name)) {
        $name = $test.BaseName
        & "$compiler\tools\csc.exe" @arguments /target:exe "/main:$name" "/out:$testOut\$name.exe" @source $test.FullName
        if ($LASTEXITCODE) { throw "Test compilation failed: $name" }
        if ($CompileTestsOnly -or $name -eq 'HardwareAcceptance') { continue } # Hardware tests require an explicit test session.
        & "$testOut\$name.exe"
        if ($LASTEXITCODE) { throw "Test failed: $name" }
    }
}

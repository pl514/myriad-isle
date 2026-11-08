$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
# ---- WinRT 元数据：图片翻译用的 Windows OCR ----
$winmd = $null
$unionRoot = 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata'
if (Test-Path $unionRoot) {
    # 只认版本号目录（10.0.xxxxx.0），Facade 目录里的 winmd 不含 OCR
    $winmd = Get-ChildItem $unionRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}
$runtimeFacade = "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll"
$refs = @(
    "/r:$framework\WPF\PresentationCore.dll",
    "/r:$framework\WPF\PresentationFramework.dll",
    "/r:$framework\WPF\WindowsBase.dll",
    "/r:$framework\System.Xaml.dll",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:$framework\System.Net.Http.dll",
    "/r:$framework\System.Web.Extensions.dll"
)
if ($winmd -and (Test-Path $runtimeFacade)) {
    $refs += "/r:$winmd"
    $refs += "/r:$runtimeFacade"
    $refs += "/r:$framework\System.Runtime.WindowsRuntime.dll"
}
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$out\MyriadIsle.exe" `
    "/win32manifest:$root\src\app.manifest" `
    "/win32icon:$root\src\app.ico" `
    $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Write-Output "Built: $out\MyriadIsle.exe"

# Publishes the three shipping executables (framework-dependent, win-x64) into
# dist\app, ready for the installer to package. Requires the .NET Desktop Runtime
# 8 on the target machine (the installer verifies this).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "dist\app"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$projects = @(
    "src\Bastion.Service\Bastion.Service.csproj",
    "src\Bastion.Gatekeeper\Bastion.Gatekeeper.csproj",
    "src\Bastion.App\Bastion.App.csproj"
)

foreach ($proj in $projects) {
    Write-Host "Publishing $proj ..."
    dotnet publish (Join-Path $root $proj) -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=false -p:DebugType=none -o $out --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $proj" }
}

# Bundle brand icon + product docs consumed by the app/installer.
Copy-Item (Join-Path $root "assets\bastion.ico") $out -Force
foreach ($doc in @("LICENSE.txt", "How-it-works.txt", "README.md")) {
    $src = Join-Path $root "docs\$doc"
    if (Test-Path $src) { Copy-Item $src $out -Force }
}

$size = "{0:N1} MB" -f ((Get-ChildItem $out -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "Published to $out  ($size)"
Get-ChildItem $out -Filter *.exe | ForEach-Object { Write-Host "  $($_.Name)" }

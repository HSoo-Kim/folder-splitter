param(
    [switch]$UiTests,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
}
if (-not (Test-Path $dotnet)) {
    throw ".NET 10 SDK is required. Install it, then run this script again."
}

function Run-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

Run-Dotnet build "$root/src/FolderSplitter/FolderSplitter.csproj" -c Release --nologo
Run-Dotnet run --project "$root/tests/QuadExplorer.Tests/QuadExplorer.Tests.csproj" -c Release
if ($UiTests) {
    Run-Dotnet run --project "$root/tests/ExplorerFavorites.Tests/ExplorerFavorites.Tests.csproj" -c Release
    Run-Dotnet run --project "$root/tests/QuadExplorer.UiTests/QuadExplorer.UiTests.csproj" -c Release
}
if ($Publish) {
    Run-Dotnet publish "$root/src/FolderSplitter/FolderSplitter.csproj" -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o "$root/artifacts/FolderSplitter"
    Compress-Archive -Path "$root/artifacts/FolderSplitter/FolderSplitter.exe", "$root/README.md", `
        "$root/README.en.md", "$root/README.zh-CN.md", "$root/README.ja.md" `
        -DestinationPath "$root/artifacts/folder-splitter-win-x64.zip" -Force
    Get-ChildItem "$root/artifacts" -Directory |
        Where-Object { $_.Name -like "FolderSplitter-v*" -or $_.Name -eq "win-x64" } |
        Remove-Item -Recurse -Force
    $legacyArchive = Join-Path $root "artifacts/QuadExplorer-win-x64.zip"
    if (Test-Path $legacyArchive) { Remove-Item $legacyArchive -Force }
}

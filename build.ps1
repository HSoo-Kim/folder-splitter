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
Run-Dotnet run --project "$root/tests/PathSearch.Tests/PathSearch.Tests.csproj" -c Release
if ($UiTests) {
    Run-Dotnet run --project "$root/tests/ExplorerFavorites.Tests/ExplorerFavorites.Tests.csproj" -c Release
    Run-Dotnet build "$root/tools/PrivateDesktopQa/PrivateDesktopQa.csproj" -c Release --nologo
    Run-Dotnet build "$root/tests/QuadExplorer.UiTests/QuadExplorer.UiTests.csproj" -c Release --nologo
    $keepLog = -not [string]::IsNullOrWhiteSpace($env:FOLDERSPLITTER_QA_LOG_DIR)
    $logDirectory = if ($keepLog) { $env:FOLDERSPLITTER_QA_LOG_DIR } else {
        Join-Path ([IO.Path]::GetTempPath()) ("FolderSplitter-QA-" + [Guid]::NewGuid().ToString("N"))
    }
    [IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $log = Join-Path $logDirectory ("native-ui-" + [Guid]::NewGuid().ToString("N") + ".log")
    try {
        & $dotnet "$root/tools/PrivateDesktopQa/bin/Release/net10.0-windows/PrivateDesktopQa.dll" `
            --log $log --timeout 600 -- $dotnet `
            "$root/tests/QuadExplorer.UiTests/bin/Release/net10.0-windows/QuadExplorer.UiTests.dll"
        $nativeExit = $LASTEXITCODE
        if (Test-Path -LiteralPath $log) {
            Get-Content -LiteralPath $log
        }
        if ($nativeExit -ne 0) { throw "Private-desktop native tests failed with exit code $nativeExit." }
    }
    finally {
        if (-not $keepLog) { Remove-Item -LiteralPath $logDirectory -Recurse -Force }
    }
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

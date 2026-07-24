Param ($Version = "1.0.0-pre")
$ErrorActionPreference = "Stop"
pushd $PSScriptRoot

function Run-DotNet {
    ..\0install.ps1 run --batch --version 9.0.200.. https://apps.0install.net/dotnet/sdk.xml @args
    if ($LASTEXITCODE -ne 0) {throw "Exit Code: $LASTEXITCODE"}
}

echo "Build binaries"
if ($env:CI) { $ci = "/p:ContinuousIntegrationBuild=True" }
Run-DotNet msbuild /v:Quiet /Restore /t:Build /p:Configuration=Release /p:Version=$Version $ci

echo "Prepare binaries for publishing"
Run-DotNet msbuild /v:Quiet /t:Publish /p:NoBuild=True /p:Configuration=Release /p:Version=$Version
Remove-Item ..\artifacts\Release\net8.0\publish -Recurse -Include *.xml,*.pdb,Microsoft.CodeAnalysis*

popd

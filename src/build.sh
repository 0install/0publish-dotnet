#!/usr/bin/env bash
set -e
cd `dirname $0`

dotnet="../0install.sh run --version 9.0.200.. https://apps.0install.net/dotnet/sdk.xml"

# Avoid terminal rendering issues in CI
if [ -n "$CI" ]; then
    export MSBUILDTERMINALLOGGER="off"
fi

echo "Build binaries"
$dotnet msbuild -v:Quiet -restore -t:Build -p:Configuration=Release -p:Version=${1:-1.0.0-pre} ${CI+-p:ContinuousIntegrationBuild=True}

echo "Prepare binaries for publishing"
$dotnet msbuild -v:Quiet -t:Publish -p:NoBuild=True -p:Configuration=Release -p:Version=${1:-1.0.0-pre}
find ../artifacts/Release/net8.0/publish \( -name "*.xml" -o -name "*.pdb" -o -name "Microsoft.CodeAnalysis*" \) -type f -delete

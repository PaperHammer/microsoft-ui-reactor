param(
    [string] $Repository = (Join-Path $PSScriptRoot '../..'),
    [string] $Name = 'repro'
)

$repositoryPath = (Resolve-Path -LiteralPath $Repository).Path
$testProject = Join-Path $repositoryPath 'tests/Reactor.Tests/Reactor.Tests.csproj'
$reproTargets = Join-Path $PSScriptRoot 'Repro.targets'
$results = Join-Path $PSScriptRoot "results/$Name"

Push-Location -LiteralPath $repositoryPath
try {
    # global.json selects the repository's SDK and Microsoft.Testing.Platform runner.
    dotnet restore $testProject -p:Platform=x64 -p:NuGetAudit=false --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build $testProject -c Release --no-restore -p:Platform=x64 `
        -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true -p:CopilotSkipCliDownload=true `
        "-p:CustomAfterMicrosoftCommonTargets=$reproTargets" -m:1 -nodereuse:false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test $testProject --no-build -c Release -p:Platform=x64 `
        --filter-class '*ResourceCollisionReproTests*' --report-trx --results-directory $results
    exit $LASTEXITCODE
}
finally { Pop-Location }

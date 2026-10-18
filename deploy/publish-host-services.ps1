param(
    [string]$OutputPath = "$PSScriptRoot/artifacts/host"
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$status = & git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $status) { throw 'Publication requires a clean committed checkout.' }
$revision = & git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -cnotmatch '^[a-f0-9]{40}$') { throw 'Cannot freeze source revision.' }
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$frozenSource = Join-Path $tempParent ('whaledeck-host-source-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $frozenSource | Out-Null
try {
    $archive = Join-Path $frozenSource 'source.tar'
    & git -C $repoRoot archive --format=tar --output=$archive $revision
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    & tar -xf $archive -C $frozenSource
    if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
    foreach ($component in 'Agent', 'MaintenanceHost') {
        $target = if ($component -eq 'Agent') { 'agent' } else { 'maintenance' }
        dotnet publish "$frozenSource/src/WhaleDeck.$component/WhaleDeck.$component.csproj" -c Release -r linux-x64 --self-contained true -o "$OutputPath/$target" "-p:SourceRevisionId=$revision"
        if ($LASTEXITCODE -ne 0) { throw "$component publication failed." }
    }
}
finally {
    $resolvedFrozenSource = [IO.Path]::GetFullPath($frozenSource)
    if ([IO.Path]::GetDirectoryName($resolvedFrozenSource) -ne $tempParent.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolvedFrozenSource) -cnotmatch '^whaledeck-host-source-[a-f0-9]{32}$') {
        throw 'Refusing cleanup outside the exact temporary source directory.'
    }
    Remove-Item -LiteralPath $resolvedFrozenSource -Recurse -Force
}
python "$PSScriptRoot/host/validate-runtime.py" "$OutputPath"
if ($LASTEXITCODE -ne 0) { throw 'Self-contained runtime validation failed.' }
Get-ChildItem "$OutputPath" -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "{0}  {1}" -f $_.Hash.ToLowerInvariant(), $_.Path.Substring($OutputPath.Length + 1).Replace('\', '/') } | Set-Content "$OutputPath/SHA256SUMS"

param(
    [Parameter(Mandatory = $true)] [string] $Title,
    [Parameter(Mandatory = $true)] [string] $Text,
    [string] $Link = "https://github.com/Endennsss/Orbitra-Launcher",
    [string] $Remote = "custom",
    [string] $Branch = "main"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$newsPath = Join-Path $repositoryRoot "news.json"
$remoteAddress = "git@github.com:Endennsss/Orbitra-Launcher.git"

if (-not (Test-Path -LiteralPath $newsPath)) {
    Set-Content -LiteralPath $newsPath -Value "[]" -Encoding utf8
}

$entries = @(Get-Content -LiteralPath $newsPath -Raw -Encoding utf8 | ConvertFrom-Json)
$entry = [ordered]@{
    title = $Title
    summary = $Text
    date = (Get-Date -Format "dd.MM.yyyy")
    url = $Link
}
$updatedEntries = @($entry) + $entries
$updatedEntries | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $newsPath -Encoding utf8

Push-Location $repositoryRoot
try {
    $existingRemote = git remote get-url $Remote 2>$null
    if ($LASTEXITCODE -ne 0) {
        git remote add $Remote $remoteAddress
    }
    elseif ($existingRemote -ne $remoteAddress) {
        throw "Remote '$Remote' уже указывает на другой репозиторий: $existingRemote"
    }

    git add -- news.json
    git commit -m "news: $Title"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось создать коммит новости." }

    git push $Remote "HEAD:$Branch"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось отправить новость на GitHub." }

    Write-Host "Новость опубликована: $Title" -ForegroundColor Green
}
finally {
    Pop-Location
}

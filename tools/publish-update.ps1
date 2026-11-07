# Publish a new version of PhotonCanvas from this computer, without GitHub's build minutes: build
# the latest master and put it up as a GitHub release, which PhotonCanvas finds and puts in by itself
# (Settings > Updates). The same release the Release workflow (.github/workflows/release.yml) makes.
#
#   Double-click "Publish update.cmd" in the project folder, or:
#   tools/publish-update.ps1            # build master and publish it, if it isn't published yet
#   tools/publish-update.ps1 -Force     # publish it again under a new number even if it is
#   tools/publish-update.ps1 -DryRun    # build it and show what would go up, publish nothing
#
# It builds in its own copy of the project (a git worktree, by default in
# %LOCALAPPDATA%\PhotonCanvas-publish\build), so your project folder, its changes and the copy of
# PhotonCanvas you're running are left alone. Needs Git for Windows (for tools/publish.sh) and the
# .NET 10 SDK. To publish it needs a GitHub key that can write releases: the GitHub CLI's if it's
# signed in, otherwise it asks for one once and keeps it, encrypted for your Windows account.
#
# Kept to plain ASCII: Windows PowerShell 5.1 reads a script without a BOM as ANSI.
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$DryRun,
    [string]$Repo = $(if ($env:RELEASE_REPO) { $env:RELEASE_REPO } else { 'noalear/PhotonCanvas' }),
    [string]$Branch = 'master',
    [string]$Platforms = 'win-x64',
    [string]$WorkDir
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Windows PowerShell's progress bar slows uploads to a crawl
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

$OnWindows = $env:OS -eq 'Windows_NT'
$Arrow = [string][char]0x2192
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $WorkDir) {
    $base = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { Join-Path $HOME '.cache' }
    $WorkDir = Join-Path $base 'PhotonCanvas-publish'
}
$BuildDir = Join-Path $WorkDir 'build'
$KeyFile = Join-Path $WorkDir 'github-key.txt'

function Say([string]$text) { Write-Host $text }
function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Stop-With([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Red; exit 1 }

# A program's output lines, its messages left on the screen. (Windows PowerShell turns a program's
# messages into errors when they're redirected, and this script stops at errors.)
function Run-Program([string]$exe, [string[]]$arguments, [switch]$Quiet) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Quiet) { $out = & $exe @arguments 2>$null } else { $out = & $exe @arguments }
    } finally { $ErrorActionPreference = $old }
    @($out | ForEach-Object { "$_" })
}

function Git {
    $out = Run-Program git $args
    if ($LASTEXITCODE -ne 0) { Stop-With "git $($args -join ' ') failed (above)." }
    $out
}

# ---- The GitHub key ------------------------------------------------------------------------------

function Find-Gh {
    $c = Get-Command gh -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($p in @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    $null
}

function Read-SavedKey {
    if (-not (Test-Path $KeyFile)) { return $null }
    try {
        $secure = Get-Content $KeyFile -Raw | ConvertTo-SecureString
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    } catch { $null }
}

function Ask-ForKey {
    $owner = $Repo.Split('/')[0]
    $url = 'https://github.com/settings/personal-access-tokens/new?name=PhotonCanvas+publishing' +
        '&description=Publishes+PhotonCanvas+updates+from+this+computer&target_name=' + $owner +
        '&expires_in=none&contents=write'
    Say ''
    Say "Publishing needs a GitHub key that can make releases in $Repo. (You only do this once.)"
    Say 'Your browser opens the page to make one. On it:'
    Say "  - Repository access: Only select repositories $Arrow $($Repo.Split('/')[1])"
    Say "  - Permissions: Contents $Arrow Read and write"
    Say '  - Generate token, copy it, and paste it here (right-click pastes).'
    Say '  (The read-only key in PhotonCanvas'' Settings can''t publish: this one is separate.)'
    try { Start-Process $url } catch { Say "  Open this page: $url" }
    $secure = Read-Host 'GitHub key' -AsSecureString
    $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    if (-not $key.Trim()) { Stop-With 'No key given: nothing was published.' }
    $key.Trim()
}

function Save-Key([string]$key) {
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    ConvertTo-SecureString $key -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $KeyFile
}

function Get-Key {
    if ($env:GH_TOKEN) { return @{ Key = $env:GH_TOKEN; From = 'GH_TOKEN' } }
    if ($env:GITHUB_TOKEN) { return @{ Key = $env:GITHUB_TOKEN; From = 'GITHUB_TOKEN' } }
    $saved = Read-SavedKey
    if ($saved) { return @{ Key = $saved; From = 'saved' } }
    $gh = Find-Gh
    if ($gh) {
        $t = Run-Program $gh @('auth', 'token') -Quiet
        if ($LASTEXITCODE -eq 0 -and $t.Count -gt 0 -and $t[0].Trim()) { return @{ Key = $t[0].Trim(); From = 'gh' } }
    }
    @{ Key = (Ask-ForKey); From = 'new' }
}

function Api([string]$method, [string]$url, $body, [string]$inFile, [string]$contentType) {
    $headers = @{
        Authorization = "Bearer $script:Key"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'PhotonCanvas-publish'
    }
    $p = @{ Method = $method; Uri = $url; Headers = $headers; TimeoutSec = 1800 }
    if ($null -ne $body) {
        $p.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5))
        $p.ContentType = 'application/json; charset=utf-8'
    }
    if ($inFile) { $p.InFile = $inFile; $p.ContentType = $contentType }
    Invoke-RestMethod @p
}

function Status-Of($err) {
    try { [int]$err.Exception.Response.StatusCode } catch { 0 }
}

# ---- Get the latest master into the build copy ---------------------------------------------------

Say "PhotonCanvas: publish an update from this computer to $Repo"
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Stop-With 'Git isn''t installed (https://git-scm.com/download/win).' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-With 'The .NET SDK isn''t installed (https://dotnet.microsoft.com/download/dotnet/10.0).' }

Step "Getting the latest $Branch"
Git -C $Root fetch --quiet --force --tags origin $Branch | Out-Null
if (-not (Test-Path (Join-Path $BuildDir '.git'))) {
    if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    Git -C $Root worktree prune | Out-Null
    Git -C $Root worktree add --quiet --detach $BuildDir "origin/$Branch" | Out-Null
} else {
    Git -C $BuildDir checkout --quiet --force --detach "origin/$Branch" | Out-Null
}
# Builds from before stay (they make the next one quicker); the last one's output doesn't.
$dist = Join-Path $BuildDir 'dist'
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
$sha = (Git -C $BuildDir rev-parse HEAD | Select-Object -First 1).Trim()
$subject = (Git -C $BuildDir log -1 --format=%s | Select-Object -First 1)
Say "Latest: $($sha.Substring(0, 9)) $subject"

$already = @(Git -C $BuildDir tag --points-at HEAD --list 'v*') | Where-Object { $_ }
if ($already.Count -gt 0 -and -not $Force) {
    Say ''
    Say "That's already published, as $($already[0]). Nothing new to publish."
    Say "(To publish it again anyway: Publish update.cmd -Force)"
    exit 0
}

# The version: major.minor from Directory.Build.props, then one past the highest release of it.
$props = Get-Content (Join-Path $BuildDir 'Directory.Build.props') -Raw
$m = [regex]::Match($props, '<Version>(\d+)\.(\d+)\.\d+</Version>')
$majorMinor = if ($m.Success) { "$($m.Groups[1].Value).$($m.Groups[2].Value)" } else { '0.1' }
$highest = 0
foreach ($t in @(Git -C $BuildDir tag --list "v$majorMinor.*")) {
    $n = 0
    if ([int]::TryParse(("$t".Trim()).Substring($majorMinor.Length + 2), [ref]$n) -and $n -gt $highest) { $highest = $n }
}
$Version = "$majorMinor.$($highest + 1)"
Say "This will be version $Version."

# Ask for the key before the build, so a missing one doesn't waste it.
$script:Key = $null
if (-not $DryRun) {
    $k = Get-Key
    $script:Key = $k.Key
    try { Api GET "https://api.github.com/repos/$Repo" | Out-Null }
    catch {
        if ($k.From -eq 'saved') { Remove-Item -Force $KeyFile }
        Stop-With "GitHub didn't accept the key for $Repo ($(Status-Of $_)). Run it again to give a new one."
    }
    if ($k.From -eq 'new') { Save-Key $script:Key; Say 'Key kept for next time.' }
}

# ---- Build -----------------------------------------------------------------------------------------

Step "Building $Platforms (a few minutes)"
$bash = 'bash'
if ($OnWindows) {
    # Git for Windows' bash, not WSL's (C:\Windows\System32\bash.exe).
    $bash = $null
    $git = (Get-Command git).Source
    foreach ($p in @((Join-Path (Split-Path (Split-Path $git)) 'bin\bash.exe'), "$env:ProgramFiles\Git\bin\bash.exe")) {
        if (Test-Path $p) { $bash = $p; break }
    }
    if (-not $bash) { Stop-With 'Couldn''t find Git for Windows'' bash.exe (it comes with Git: https://git-scm.com/download/win).' }
}
$env:VERSION = $Version
$env:RELEASE_REPO = $Repo
Push-Location $BuildDir
$old = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try { & $bash tools/publish.sh @($Platforms -split '\s+' | Where-Object { $_ }) } finally { $ErrorActionPreference = $old; Pop-Location }
if ($LASTEXITCODE -ne 0) { Stop-With 'The build failed (above): nothing was published.' }

$files = @(Get-ChildItem $dist -File | Where-Object { $_.Name -like 'PhotonCanvas-*.zip' -or $_.Name -like 'PhotonCanvas-*.tar.gz' })
if ($files.Count -eq 0) { Stop-With 'Nothing was built: nothing was published.' }
$files += @(Get-ChildItem $dist -File -Filter 'PhotonCanvas-*.files.txt')
# The checksums the program checks every download against (it won't install without them), as sha256sum writes them.
$sums = ($files | Sort-Object Name | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }) -join "`n"
$sumsPath = Join-Path $dist 'SHA256SUMS'
[IO.File]::WriteAllText($sumsPath, $sums + "`n")
$files += Get-Item $sumsPath

# What's new: the changes since the last release.
$last = Run-Program git @('-C', $BuildDir, 'describe', '--tags', '--match', 'v*', '--abbrev=0', 'HEAD') -Quiet
$range = if ($LASTEXITCODE -eq 0 -and $last.Count -gt 0) { "$($last[0].Trim())..HEAD" } else { 'HEAD' }
$changes = @(Git -C $BuildDir log --no-merges '--format=- %s' $range) | Select-Object -First 60
$notes = (@("What's new in ${Version}:", '') + $changes + @('',
    "PhotonCanvas downloads this version by itself and puts it in when you close it (Settings $Arrow Updates). To install it by hand, unpack the file for your computer in place of the old one: your designs, settings and material library are kept in your PhotonCanvas folder, not with the program.",
    '',
    'PhotonCanvas is free to use, for personal or commercial work; all rights reserved. It may not be sold, redistributed or published in a modified form without written permission. The full terms are in the LICENSE file that comes with it.')) -join "`n"

if ($DryRun) {
    Step "Would publish v$Version (dry run: nothing published)"
    $files | ForEach-Object { Say ("  {0,-34} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
    Say ''
    Say $notes
    exit 0
}

# ---- Publish ---------------------------------------------------------------------------------------

Step "Publishing v$Version"
# A draft first, so nobody's copy sees the release until every file is up.
try {
    $release = Api POST "https://api.github.com/repos/$Repo/releases" @{
        tag_name = "v$Version"; target_commitish = $sha; name = "PhotonCanvas $Version"; body = $notes; draft = $true }
} catch {
    $code = Status-Of $_
    if ($code -eq 403 -or $code -eq 404) {
        if (Test-Path $KeyFile) { Remove-Item -Force $KeyFile }
        Stop-With "GitHub won't let this key make releases in $Repo ($code). It needs Contents: Read and write on that repository. Run it again to give a new key."
    }
    Stop-With "GitHub didn't take the release ($code): $($_.Exception.Message)"
}
try {
    $uploads = $release.upload_url -replace '\{.*$', ''
    foreach ($f in $files) {
        Say ("  {0} ({1:N1} MB)" -f $f.Name, ($f.Length / 1MB))
        $type = if ($f.Name -like '*.zip') { 'application/zip' } elseif ($f.Name -like '*.gz') { 'application/gzip' } else { 'text/plain' }
        Api POST "${uploads}?name=$([Uri]::EscapeDataString($f.Name))" $null $f.FullName $type | Out-Null
    }
    $done = Api PATCH "https://api.github.com/repos/$Repo/releases/$($release.id)" @{ draft = $false; make_latest = 'true' }
} catch {
    $why = $_.Exception.Message
    try { Api DELETE "https://api.github.com/repos/$Repo/releases/$($release.id)" | Out-Null } catch { }
    Stop-With "The upload failed, so nothing was published: $why"
}
Git -C $Root fetch --quiet --tags origin | Out-Null

Write-Host ''
Write-Host "Published PhotonCanvas $Version" -ForegroundColor Green
Say $done.html_url
Say "PhotonCanvas finds it the next time it starts or checks (Settings $Arrow Updates $Arrow Check now)."

#requires -Version 7.4
[CmdletBinding()]
param([ValidateRange(0,65535)][int]$Port=0,[switch]$NoBrowser,[switch]$Demo,[switch]$SelfTest,[string]$DataDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { throw 'This app requires Add-Type in FullLanguage mode. Ask your administrator if local policy blocks it; this app does not bypass policy.' }
$root=$PSScriptRoot
$version=([IO.File]::ReadAllText((Join-Path $root 'VERSION'))).Trim()
if (-not ('ClassicEngine' -as [type])) { Add-Type -Path @((Join-Path $root 'lib/Native.cs'),(Join-Path $root 'lib/Protocol.cs'),(Join-Path $root 'lib/LocalServer.cs'),(Join-Path $root 'tests/SelfTests.cs')) }
if ($SelfTest) { & (Join-Path $root 'tests/Test-Protocol.ps1'); exit }
if (-not $DataDirectory) {
    $base=if ($IsWindows) { [Environment]::GetFolderPath('LocalApplicationData') } elseif ($env:XDG_DATA_HOME) { $env:XDG_DATA_HOME } else { Join-Path $HOME '.local/share' }
    $DataDirectory=Join-Path $base 'AMAG23-Classic'
}
# Appearance belongs to this user's local app settings, not to the device.
# Browser localStorage cannot reliably persist across our random localhost ports.
$preferencesPath=Join-Path $DataDirectory 'preferences.json'
function Get-AppTheme {
    try {
        if ([IO.File]::Exists($preferencesPath)) {
            $prefs=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($preferencesPath)) -AsHashtable
            if ($prefs -is [System.Collections.IDictionary] -and [string]$prefs['theme'] -cin @('system','light','dark')) {
                return [string]$prefs['theme']
            }
        }
    } catch { Write-Warning 'Appearance settings could not be read; using System theme.' }
    return 'system'
}
function Set-AppTheme([string]$newTheme) {
    if ($newTheme -cnotin @('system','light','dark')) { throw 'Invalid appearance setting.' }
    [void][IO.Directory]::CreateDirectory($DataDirectory)
    $temp=Join-Path $DataDirectory ([IO.Path]::GetRandomFileName())
    try {
        $json=ConvertTo-Json -InputObject @{theme=$newTheme} -Compress
        [IO.File]::WriteAllText($temp,$json,[Text.UTF8Encoding]::new($false))
        if (-not $IsWindows) {
            [IO.File]::SetUnixFileMode($temp,([IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite))
        }
        [IO.File]::Move($temp,$preferencesPath,$true)
    } finally {
        if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
    }
}

$backupRoot=Join-Path $DataDirectory $(if ($Demo) {'demo-backups'} else {'backups'})
$engine=[ClassicEngine]::new($backupRoot,[bool]$Demo)
$server=[ClassicServer]::new($Port)
$token=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$origin="http://127.0.0.1:$($server.Port)"
$url="$origin/#$token"
function Reply-Json($request,$value,[int]$status=200) {
    $text=ConvertTo-Json -InputObject $value -Depth 40 -Compress
    $request.Reply($status,'application/json; charset=utf-8',[Text.Encoding]::UTF8.GetBytes($text))
}
function Handle-Request($request) {
    if ($request.Header('Host') -cne "127.0.0.1:$($server.Port)") { throw 'Invalid local Host header.' }
    $route=$request.Path
    if ($request.Method -eq 'GET' -and $route -in @('/','/index.html','/app.js','/codec.js','/app.css','/theme.js')) {
        $name=if($route -eq '/'){'index.html'}else{$route.TrimStart('/')}
        $mime=if($name.EndsWith('.html')){'text/html'}elseif($name.EndsWith('.css')){'text/css'}else{'text/javascript'}
        $request.Reply(200,"$mime; charset=utf-8",[IO.File]::ReadAllBytes((Join-Path $root "ui/$name"))); return
    }
    if ($request.Header('X-Amag-Token') -cne $token) { throw 'Invalid app session. Open the address printed in the PowerShell window.' }
    if ($request.Header('Origin') -and $request.Header('Origin') -cne $origin) { throw 'Cross-origin requests are not allowed.' }
    if ($request.Method -eq 'GET') {
        switch ($route) {
            '/api/session' { Reply-Json $request @{ok=$true;version=$version;theme=(Get-AppTheme);demo=[bool]$Demo;backups=$backupRoot}; return }
            '/api/devices' { Reply-Json $request @{ok=$true;devices=@($engine.Devices())};return }
            '/api/log' { Reply-Json $request @{ok=$true;lines=@($engine.log);lastBackup=$engine.lastBackup};return }
            default { throw 'Unknown endpoint.' }
        }
    }
    if ($request.Method -ne 'POST' -or -not $request.Header('Content-Type').StartsWith('application/json')) { throw 'Expected a JSON POST request.' }
    $body=ConvertFrom-Json -InputObject $request.Body -AsHashtable
    if ($body -isnot [System.Collections.IDictionary]) { throw 'Expected JSON object.' }
    switch ($route) {
        '/api/theme' { Set-AppTheme ([string]$body.theme); Reply-Json $request @{ok=$true;theme=(Get-AppTheme)};return }
        '/api/read' { $config=$engine.Connect([string]$body.path); Reply-Json $request @{ok=$true;config=$config;backup=$engine.lastBackup};return }
        '/api/preview' { $plan=$engine.Preview([string]$body.keymap,[string]$body.custom,[string]$body.quick,[string]$body.extra); Reply-Json $request @{ok=$true;plan=$plan};return }
        '/api/upload' {
            $config=$engine.Upload([string]$body.revision,[string]$body.keymap,[string]$body.custom,[string]$body.quick,[string]$body.extra,($body.flashConfirmed -ceq $true))
            Reply-Json $request @{ok=$true;config=$config;backup=$engine.lastBackup};return
        }
        '/api/identify' { $engine.Identify(($body.enabled -ceq $true));Reply-Json $request @{ok=$true};return }
        '/api/recover' { $engine.RecoverKeyboard(); Reply-Json $request @{ok=$true};return }
        default { throw 'Unknown endpoint.' }
    }
}
Write-Host "`nAMAG23 Classic v$version" -ForegroundColor Cyan
Write-Host $(if($Demo){'DEMO: simulated device, no USB access.'}else{'Native HID configuration editor. No gbmgr.exe. No replacement firmware.'})
Write-Host "Open: $url"
Write-Host "Backups: $backupRoot"
Write-Host 'Keep this PowerShell window open. Close the GUI, then press Ctrl+C here to quit.'
if (-not $NoBrowser) {
    try {
        $browser=$null
        if ($IsWindows) {
            $paths=@("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe","$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe","$env:ProgramFiles\Google\Chrome\Application\chrome.exe")
            $browser=$paths | Where-Object {[IO.File]::Exists($_)} | Select-Object -First 1
        } else {
            foreach ($name in @('chromium','chromium-browser','google-chrome','microsoft-edge')) {
                $cmd=Get-Command $name -ErrorAction SilentlyContinue
                if($cmd){$browser=$cmd.Source;break}
            }
        }
        if($browser){Start-Process -FilePath $browser -ArgumentList "--app=$url" | Out-Null}
        elseif($IsWindows){Start-Process $url | Out-Null}
        else {Start-Process 'xdg-open' -ArgumentList $url | Out-Null}
    } catch { Write-Warning 'Open the local address printed above manually.' }
}
try {
    while($true) {
        if(-not $server.Pending()){Start-Sleep -Milliseconds 60;continue}
        $request=$null
        try { $request=$server.Accept(); Handle-Request $request }
        catch {
            $message=$_.Exception.Message
            Write-Warning $message
            if($request){try{Reply-Json $request @{ok=$false;error=$message} 400}catch{}}
        } finally {if($request){$request.Dispose()}}
    }
} finally { $engine.Close();$server.Dispose() }

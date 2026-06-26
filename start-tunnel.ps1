# Bat cloudflared quick tunnel cho API (cong 5087) va TU DONG ghi URL moi vao Gist
# de app mobile tu nhan (khoi copy/dan tay). Chay SAU khi start-dev.ps1 da bat API.
#
#   cd E:\Flutter_Code\quant-flow-bots ; .\start-tunnel.ps1
#
# Giu cua so nay mo - dong la tunnel chet. Ctrl+C de dung.

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$cf        = Join-Path $root 'cloudflared.exe'
$tokenFile = Join-Path $root '.tunnel-token'
$gistId    = '3fa600bc2917d3d21027e2de5b773a06'
$apiUrl    = 'http://localhost:5087'

if (-not (Test-Path $cf))        { Write-Host "Khong thay cloudflared.exe o $cf" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $tokenFile)) { Write-Host "Khong thay .tunnel-token ($tokenFile)" -ForegroundColor Red; exit 1 }
$token = (Get-Content $tokenFile -Raw).Trim()

# Canh bao neu API chua chay (tunnel van bat duoc nhung se tro vao cho trong).
try {
    Invoke-WebRequest -Uri "$apiUrl/health" -TimeoutSec 4 -UseBasicParsing | Out-Null
    Write-Host 'API 5087 dang chay. OK.' -ForegroundColor Green
} catch {
    Write-Host 'CANH BAO: API 5087 chua phan hoi. Hay chay start-dev.ps1 truoc.' -ForegroundColor Yellow
}

$errLog = Join-Path $env:TEMP 'qfb-cloudflared.err.log'
$outLog = Join-Path $env:TEMP 'qfb-cloudflared.out.log'
foreach ($f in @($errLog, $outLog)) { if (Test-Path $f) { Remove-Item $f -Force } }

Write-Host 'Dang bat cloudflared tunnel (http2)...' -ForegroundColor Cyan
$proc = Start-Process -FilePath $cf `
    -ArgumentList @('tunnel', '--url', $apiUrl, '--protocol', 'http2') `
    -RedirectStandardError $errLog -RedirectStandardOutput $outLog -PassThru -NoNewWindow

# Cho URL trycloudflare xuat hien trong log (cloudflared in ra stderr).
$tunnelUrl = $null
$deadline  = (Get-Date).AddSeconds(45)
while ((Get-Date) -lt $deadline -and -not $tunnelUrl) {
    Start-Sleep -Milliseconds 500
    foreach ($f in @($errLog, $outLog)) {
        if (Test-Path $f) {
            $m = Select-String -Path $f -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($m) { $tunnelUrl = $m.Matches[0].Value; break }
        }
    }
}

if (-not $tunnelUrl) {
    Write-Host 'Khong lay duoc URL tunnel (timeout 45s). Xem log:' -ForegroundColor Red
    Write-Host "  $errLog" -ForegroundColor DarkGray
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    exit 1
}
Write-Host "URL tunnel: $tunnelUrl" -ForegroundColor Green

# Ghi URL vao Gist (app doc file nay khi mo).
$content = @{ baseUrl = $tunnelUrl; updatedAt = (Get-Date).ToString('o') } | ConvertTo-Json -Compress
$body    = @{ files = @{ 'tunnel.json' = @{ content = $content } } } | ConvertTo-Json -Depth 6
try {
    Invoke-RestMethod -Method Patch -Uri "https://api.github.com/gists/$gistId" `
        -Headers @{ Authorization = "token $token"; 'User-Agent' = 'qfb-tunnel'; Accept = 'application/vnd.github+json' } `
        -Body $body | Out-Null
    Write-Host 'Da cap nhat Gist -> mo app la tu nhan URL (khong can dan tay).' -ForegroundColor Green
} catch {
    Write-Host "Loi cap nhat Gist: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Tunnel van chay - ban co the dan URL thu cong vao app neu can.' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '====================================================================' -ForegroundColor Yellow
Write-Host "  Tunnel DANG CHAY: $tunnelUrl"                                       -ForegroundColor Yellow
Write-Host '  GIU cua so nay mo. Ctrl+C de dung.'                                 -ForegroundColor Yellow
Write-Host '====================================================================' -ForegroundColor Yellow

# Giu tien trinh song + stream log ra cua so.
try {
    Get-Content $errLog -Wait
} finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
}

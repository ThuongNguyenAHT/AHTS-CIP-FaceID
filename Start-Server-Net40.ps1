Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  KHỞI ĐỘNG CIP CONFIRMATION (.NET Framework 4.0 - IIS Express)" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$root = $PSScriptRoot
$sitePath = Join-Path $root "src\Cip.Server.Net40\bin\Debug\net40\site"

if (-not (Test-Path $sitePath)) {
    Write-Host "Đang build Cip.Server.Net40..." -ForegroundColor Yellow
    dotnet build (Join-Path $root "CipConfirmation.Net40.sln") -c Debug
}

$iisExpress = "C:\Program Files\IIS Express\iisexpress.exe"
if (-not (Test-Path $iisExpress)) {
    $iisExpress = "C:\Program Files (x86)\IIS Express\iisexpress.exe"
}

if (-not (Test-Path $iisExpress)) {
    Write-Error "Không tìm thấy IIS Express tại $iisExpress. Vui lòng cài đặt IIS Express."
    exit 1
}

Write-Host "Tablet URL: http://localhost:5189/tablet" -ForegroundColor Green
Write-Host "Admin URL:  http://localhost:5189/admin" -ForegroundColor Green
Write-Host "Tài khoản: admin / Cip@123456" -ForegroundColor Yellow
Write-Host "Đang mở trình duyệt..." -ForegroundColor White

Start-Process "http://localhost:5189/tablet"
& $iisExpress /path:"$sitePath" /port:5189 /clr:v4.0

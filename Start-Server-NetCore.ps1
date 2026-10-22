Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  KHỞI ĐỘNG CIP CONFIRMATION (.NET Core / .NET 9)" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "Tablet URL: http://localhost:5188/tablet" -ForegroundColor Green
Write-Host "Admin URL:  http://localhost:5188/admin" -ForegroundColor Green
Write-Host "Tài khoản: admin / Cip@123456" -ForegroundColor Yellow
Write-Host "Đang khởi động..." -ForegroundColor White

dotnet run --project .\src\Cip.Server\Cip.Server.csproj

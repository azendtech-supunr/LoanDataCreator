# Clean Rebuild Script for LoanDataCreator
# This ensures all old compiled files are removed before rebuilding

Write-Host "===== LoanDataCreator Clean Rebuild =====" -ForegroundColor Cyan
Write-Host ""

# Navigate to project directory
$projectDir = "C:\Users\supun\WORK\Azend\Saral\Tools\LoanDataCreator\LoanDataCreator"
Set-Location $projectDir

Write-Host "Step 1: Cleaning build artifacts..." -ForegroundColor Yellow
dotnet clean
if ($LASTEXITCODE -ne 0) {
    Write-Host "Clean failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Step 2: Removing bin and obj directories..." -ForegroundColor Yellow
Remove-Item -Path "bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "obj" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Step 3: Rebuilding project..." -ForegroundColor Yellow
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "===== Rebuild Complete! =====" -ForegroundColor Green
Write-Host ""
Write-Host "You can now run the application with:" -ForegroundColor Cyan
Write-Host "  dotnet run --all" -ForegroundColor White
Write-Host ""

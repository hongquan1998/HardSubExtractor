# Quick Build Script
# Build portable EXE trong 1 command

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Build Portable EXE" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Building..." -ForegroundColor Yellow
Write-Host ""

# Build single file
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "  Build Success!" -ForegroundColor Green
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host ""
    
    $outputPath = "bin\Release\net8.0-windows\win-x64\publish\HardSubExtractor.exe"
    
    if (Test-Path $outputPath) {
        $fileSize = [math]::Round((Get-Item $outputPath).Length / 1MB, 2)
        
        Write-Host "Output:" -ForegroundColor Cyan
        Write-Host "  File: $outputPath" -ForegroundColor Gray
        Write-Host "  Size: $fileSize MB" -ForegroundColor Gray
        Write-Host ""
        
        Write-Host "Ready to distribute!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Chi can copy file .exe nay la xong!" -ForegroundColor Green
        Write-Host "Khong can cai dat gi them!" -ForegroundColor Green
        Write-Host ""
        
        # Open folder
        Write-Host "Mo thu muc output? (y/n)" -ForegroundColor Yellow
        $open = Read-Host
        if ($open -eq "y") {
            explorer.exe "bin\Release\net8.0-windows\win-x64\publish\"
        }
    }
} else {
    Write-Host ""
    Write-Host "Build failed!" -ForegroundColor Red
    Write-Host ""
}

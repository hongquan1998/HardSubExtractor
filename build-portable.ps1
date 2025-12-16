# Build Portable Package - Hard Subtitle Extractor
# Version: 1.3.0

Write-Host "=================================" -ForegroundColor Cyan
Write-Host "Hard Subtitle Extractor - BUILD" -ForegroundColor Cyan
Write-Host "Version: 1.3.0" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# 1. Clean previous build
Write-Host "[1/5] Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path "bin\Release") {
    Remove-Item "bin\Release" -Recurse -Force
    Write-Host "  ? Cleaned" -ForegroundColor Green
}

# 2. Build Release (NOT single file - keep ffmpeg/tessdata separate)
Write-Host ""
Write-Host "[2/5] Building Release..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ? Build FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "  ? Build succeeded" -ForegroundColor Green

# 3. Check output
$publishDir = "bin\Release\net8.0-windows\win-x64\publish"
Write-Host ""
Write-Host "[3/5] Checking output..." -ForegroundColor Yellow

if (-not (Test-Path $publishDir)) {
    Write-Host "  ? Publish folder not found!" -ForegroundColor Red
    exit 1
}

# Check required files
$requiredFiles = @(
    "$publishDir\HardSubExtractor.exe",
    "$publishDir\ffmpeg\bin\ffmpeg.exe",
    "$publishDir\tessdata\chi_sim.traineddata",
    "$publishDir\tessdata\eng.traineddata"
)

$allExist = $true
foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "  ? $(Split-Path $file -Leaf)" -ForegroundColor Green
    } else {
        Write-Host "  ? Missing: $file" -ForegroundColor Red
        $allExist = $false
    }
}

if (-not $allExist) {
    Write-Host ""
    Write-Host "Build incomplete - missing required files!" -ForegroundColor Red
    exit 1
}

# 4. Create portable package
Write-Host ""
Write-Host "[4/5] Creating portable package..." -ForegroundColor Yellow

$packageName = "HardSubExtractor-v1.3.0-Portable"
$packageDir = "bin\$packageName"

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

# Copy all files
Copy-Item "$publishDir\*" $packageDir -Recurse -Force

# Remove unnecessary files
$filesToRemove = @(
    "*.pdb",
    "*.xml",
    "web.config"
)

foreach ($pattern in $filesToRemove) {
    Get-ChildItem $packageDir -Filter $pattern -Recurse | Remove-Item -Force
}

Write-Host "  ? Package created: $packageDir" -ForegroundColor Green

# 5. Create ZIP
Write-Host ""
Write-Host "[5/5] Creating ZIP archive..." -ForegroundColor Yellow

$zipPath = "bin\$packageName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -Force

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "  ? ZIP created: $zipPath ($zipSize MB)" -ForegroundColor Green

# Summary
Write-Host ""
Write-Host "=================================" -ForegroundColor Cyan
Write-Host "BUILD COMPLETE!" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "?? Portable Package:" -ForegroundColor Cyan
Write-Host "   $packageDir" -ForegroundColor White
Write-Host ""
Write-Host "?? ZIP Archive:" -ForegroundColor Cyan
Write-Host "   $zipPath ($zipSize MB)" -ForegroundColor White
Write-Host ""

# Calculate total size
$folderSize = [math]::Round((Get-ChildItem $packageDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
Write-Host "?? Package Size:" -ForegroundColor Cyan
Write-Host "   Folder: $folderSize MB" -ForegroundColor White
Write-Host "   ZIP: $zipSize MB" -ForegroundColor White
Write-Host ""

Write-Host "? Ready to distribute!" -ForegroundColor Green
Write-Host ""
Write-Host "User ch? c?n:" -ForegroundColor Yellow
Write-Host "  1. Extract ZIP" -ForegroundColor White
Write-Host "  2. Run HardSubExtractor.exe" -ForegroundColor White
Write-Host "  3. Done! (Không c?n cài gì thêm)" -ForegroundColor White
Write-Host ""

# Ask to open folder
$response = Read-Host "M? folder output? (Y/N)"
if ($response -eq "Y" -or $response -eq "y") {
    explorer.exe "bin\"
}

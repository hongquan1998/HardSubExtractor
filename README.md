# ?? Hard Subtitle Extractor v1.2

**Extract hard-coded subtitles from videos using OCR + Multi-threaded processing**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## ? What's New in v1.2

### ?? Multi-threaded OCR Processing (2-15x Faster!)

**Before:** Single-threaded, 20+ minutes for 20-min video  
**After:** Multi-threaded, 2-4 minutes with 8+ CPU cores

**New Features:**
- ? **Auto-detect optimal thread count** (CPU cores - 1)
- ? **User-adjustable threads** via UI control
- ? **Real-time FPS monitoring** with ETA
- ? **Improved progress reporting** (2% intervals)
- ? **Thread-safe parallel processing**

**Performance:**
- 4 cores: **4-6x faster** ?
- 8 cores: **7-10x faster** ??
- 16 cores: **10-15x faster** ???

See: [MULTITHREADING_OPTIMIZATION.md](DOCS/MULTITHREADING_OPTIMIZATION.md)

---

## ?? Features

### Core Features

- **?? Video to SRT:** Extract hardcoded subtitles from videos
- **?? 6 Languages:** Chinese (Simplified/Traditional), English, Japanese, Korean, Vietnamese
- **?? ROI Selection:** Drag & drop to select subtitle area
- **?? Smart Processing:**
  - Multi-threaded OCR (NEW v1.2)
  - Duplicate detection & removal
  - Auto timing adjustment
  - Text normalization
- **?? Translation Prompt:** 1-click generate AI-ready translation prompt
- **?? Export:** UTF-8 SRT file format

---

## ?? Quick Start

### 1. Run Auto-Setup (First Time Only)

**Right-click PowerShell ? Run as Administrator:**

```powershell
powershell -ExecutionPolicy Bypass -File auto-setup.ps1
```

This will:
- ? Download FFmpeg (if not present)
- ? Download Tesseract trained data
- ? Add FFmpeg to PATH
- ? Verify installation

### 2. Run Application

```bash
dotnet run
```

### 3. Process Video

```
1. [Browse Video] ? Select video file
2. [Select Subtitle Area] ? Drag ROI on video preview
3. Adjust settings:
   - FPS: 2 (recommended)
   - Threads: Auto (CPU cores - 1)
   - Language: Select source language
4. [Start OCR] ? Wait for processing
5. [Clean Subtitle] ? Remove duplicates (optional)
6. [Fix Time] ? Adjust timing (optional)
7. [Export SRT] ? Save subtitle file
```

---

## ?? Configuration

### UI Controls

| Control | Default | Range | Description |
|---------|---------|-------|-------------|
| **FPS** | 2 | 1-4 | Frames per second to extract |
| **Threads** | CPU-1 | 1-Cores | Parallel OCR threads |
| **Language** | Chinese Simplified | 6 options | OCR language |

### Recommendations

**FPS Selection:**
- 1 fps: Fast but may miss short subtitles
- 2 fps: ? **Recommended** balance
- 3-4 fps: Slower, better for rapidly changing subs

**Thread Selection:**
- Auto (CPU-1): ? **Recommended** for most cases
- Laptop: Reduce 2-3 threads to avoid overheating
- Desktop: Use all cores for maximum speed

---

## ?? Performance Benchmarks

### Test: 20-minute video, 2 FPS, 2400 frames

| CPU | Threads | Speed (fps) | Total Time | Speedup |
|-----|---------|-------------|------------|---------|
| 4 cores | 3 | 6.8 | 5:53 | 4x ? |
| 8 cores | 7 | 12.3 | 3:15 | 7x ?? |
| 16 cores | 15 | 22.1 | 1:48 | 12x ??? |

**Note:** Actual performance depends on video resolution, ROI size, and hardware (SSD vs HDD).

---

## ?? Troubleshooting

### OCR Running Slow (15+ minutes)

**Problem:** Not using enough threads

**Solution:**
1. Check **Threads** value in UI
2. Increase to `CPU cores - 1` (e.g., 7 for 8-core CPU)
3. Monitor Task Manager for CPU usage (~80-90% is good)

**If still slow:**
- Check Disk usage (should use SSD)
- Lower FPS to 1
- Reduce video resolution

### "Not Responding" During OCR

**Problem:** UI thread blocked

**Solution:** This should NOT happen in v1.2!
- Make sure you're using the latest version
- Check that `Task.Run` wraps `Parallel.ForEach`
- Report as bug if it persists

---

## ?? Documentation

| Document | Description |
|----------|-------------|
| [MULTITHREADING_OPTIMIZATION.md](DOCS/MULTITHREADING_OPTIMIZATION.md) | Multi-threading implementation details |
| [PERFORMANCE_OPTIMIZATION.md](DOCS/PERFORMANCE_OPTIMIZATION.md) | Unsafe code & image preprocessing |
| [HOW_TO_RUN.md](DOCS/HOW_TO_RUN.md) | Step-by-step usage guide |

---

**Version:** 1.2.0  
**Status:** Production Ready ?  
**Performance:** 2-15x faster than v1.0  
**Updated:** 2024

**Happy Subtitle Extracting! ??**

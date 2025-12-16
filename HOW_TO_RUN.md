# HUONG DAN CHAY UNG DUNG

## QUICK START (1 Buoc Duy Nhat!)

### Chay Ung Dung Ngay

```bash
dotnet run
```

**TAT CA DA CO SAN!**
- ? FFmpeg: Da co trong `ffmpeg/bin/`
- ? Tessdata: Da co trong `tessdata/` (6 ngon ngu)
- ? Khong can cai dat gi them!

**Ung dung se tu dong tim FFmpeg trong:**
1. Thu muc hien tai (`ffmpeg.exe`)
2. Thu muc `ffmpeg/bin/` (development) ?
3. Thu muc `bin/` (publish)
4. System PATH (fallback)

Thanh cong! Ung dung se khoi chay ngay voi FFmpeg va Tessdata da san sang.

---

## DA CO SAN TRONG SOURCE

### FFmpeg
```
ffmpeg/
??? bin/
    ??? ffmpeg.exe  ? Co san
```

### Tessdata (6 ngon ngu)
```
tessdata/
??? chi_sim.traineddata  ? Chinese Simplified (2.35 MB)
??? chi_tra.traineddata  ? Chinese Traditional (2.26 MB)
??? eng.traineddata      ? English (3.92 MB)
??? jpn.traineddata      ? Japanese (2.36 MB)
??? kor.traineddata      ? Korean (1.6 MB)
??? vie.traineddata      ? Vietnamese (0.51 MB)
```

**KHONG CAN DOWNLOAD GI THEM!**

---

## BUILD FILE EXE PORTABLE

### Build Single File EXE

```bash
# Build 1 file .exe duy nhat (portable)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Output:
# bin\Release\net8.0-windows\win-x64\publish\HardSubExtractor.exe
```

**Ket qua:**
- 1 file `.exe` (~80-100MB)
- Chua tat ca: .NET, FFmpeg, Tessdata
- Copy la chay!
- Khong can cai dat gi!

### Build voi Files Rieng (nho hon)

```bash
# Build voi cac file rieng biet
dotnet publish -c Release -r win-x64 --self-contained

# Output:
# bin\Release\net8.0-windows\win-x64\publish/
# ??? HardSubExtractor.exe
# ??? ffmpeg.exe
# ??? tessdata/
# ??? ...
```

**Ket qua:**
- Nho hon (~100-150MB tong)
- De customize
- Nhieu file

---

## TU DONG TIM FFMPEG

**Ung dung da duoc update de tu dong tim FFmpeg!**

### Cach hoat dong:

1. **Priority 1:** FFmpeg trong thu muc ung dung (portable)
   ```
   HardSubExtractor.exe
   ffmpeg.exe  ? Tim o day truoc
   ```

2. **Priority 2:** FFmpeg trong `ffmpeg/bin/` (development) ? DA CO
   ```
   HardSubExtractor/
   ??? ffmpeg/
   ?   ??? bin/
   ?       ??? ffmpeg.exe  ? Tim o day
   ??? HardSubExtractor.exe
   ```

3. **Priority 3:** System PATH (fallback)
   ```
   C:\...\ffmpeg\bin\ffmpeg.exe  ? Tim o day cuoi cung
   ```

### Test FFmpeg Detection:

```powershell
# Chay script test
powershell -ExecutionPolicy Bypass -File test-ffmpeg-detection.ps1
```

**Output mau:**
```
========================================
  FFmpeg Detection Test
========================================

Checking FFmpeg locations...

  [FOUND] FFmpeg bin folder (development)
          D:\...\ffmpeg\bin\ffmpeg.exe
          Version: ffmpeg version N-122158-...

========================================
  FFmpeg FOUND (1 location(s))
========================================

Application se tu dong dung FFmpeg da tim thay!
```

---

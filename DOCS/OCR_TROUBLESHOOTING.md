# ?? OCR TROUBLESHOOTING GUIDE - Không L?y ???c Ph? ??

## V?n ??

**Tri?u ch?ng:**
- OCR ch?y nhanh h?n v?i multi-threading ?
- Nh?ng không nh?n di?n ???c text t? video ?
- Subtitle preview tr?ng ho?c r?t ít k?t qu?
- Log hi?n th? "X empty" frames

---

## ?? Các Nguyên Nhân Ph? Bi?n

### 1. Ch?n Sai ROI (Region of Interest)

**V?n ??:** 
- ROI không ch?a ph? ??
- ROI quá nh? ho?c quá l?n
- Ph? ?? n?m ngoài vùng ?ã ch?n

**Cách ki?m tra:**
1. M? `VideoPreviewForm` (b??c 1)
2. Kéo chu?t ch?n CHÍNH XÁC vùng có ph? ??
3. Ph? ?? th??ng ?:
   - **Bottom center** (ph? bi?n nh?t)
   - Top (ít g?p)
   - Left/Right (r?t hi?m)

**Gi?i pháp:**
```
? ?ÚNG: Ch?n vùng ch?a toàn b? text
???????????????????????????
?                         ?
?                         ?
?     ???????????????     ? ? ROI covers subtitle area
?     ?  ????   ?     ?
?     ???????????????     ?
???????????????????????????

? SAI: ROI quá nh? ho?c l?ch
???????????????????????????
?  ????                   ? ? ROI too small
?  ???? ????           ?
?                         ?
???????????????????????????
```

---

### 2. Ch?n Sai Language

**V?n ??:** 
- Ch?n ngôn ng? không kh?p v?i ph? ?? trong video
- VD: Video ti?ng Trung nh?ng ch?n English

**Cách ki?m tra:**
- Xem ph? ?? trong video
- Xác ??nh ngôn ng? chính xác

**Mapping:**
| Video Language | Ch?n trong App |
|----------------|----------------|
| ???? | ???? (Chinese Simplified) |
| ???? | ???? (Chinese Traditional) |
| English | English |
| ??? | ??? (Japanese) |
| ??? | ??? (Korean) |
| Ti?ng Vi?t | Ti?ng Vi?t (Vietnamese) |

**Gi?i pháp:**
```csharp
// Trong Form:
1. Nhìn ph? ?? trong video
2. Ch?n ?úng language trong dropdown
3. Ch?y l?i OCR
```

---

### 3. FPS Quá Th?p

**V?n ??:**
- FPS = 1 ? miss nhi?u subtitle (??c bi?t subtitle ng?n)
- Không ?? frames ?? detect text

**Cách ki?m tra:**
- Xem log: "Extracted X frames"
- Video 20 phút:
  - FPS 1 ? 1200 frames
  - FPS 2 ? 2400 frames ?
  - FPS 3 ? 3600 frames
  - FPS 4 ? 4800 frames

**Gi?i pháp:**
```
T?ng FPS lên 2 ho?c 3:
- FPS 2: ? Recommended cho h?u h?t tr??ng h?p
- FPS 3: Cho subtitle thay ??i nhanh
- FPS 4: Ch? khi subtitle c?c k? ng?n (<1s)
```

---

### 4. Ch?t L??ng Video Kém

**V?n ??:**
- Video resolution th?p (360p, 480p)
- Ph? ?? b? blur, m?
- Compression artifacts

**Cách ki?m tra:**
- Enable **Debug Mode** checkbox
- Xem preprocessed images trong `%TEMP%\OCR_Debug\`
- N?u text không rõ trong preprocessed image ? video quality issue

**Gi?i pháp:**
1. S? d?ng video HD (720p+, t?t nh?t 1080p)
2. Avoid heavily compressed videos
3. N?u ch? có video kém:
   - T?ng FPS lên 3-4
   - Tùy ch?nh preprocessing (advanced)

---

### 5. Preprocessing Quá M?nh

**V?n ??:**
- Preprocessing làm m?t text
- Threshold không phù h?p
- Contrast quá cao

**Cách ki?m tra:**
1. Enable **Debug Mode** ?
2. Check `0_original_XXXX.png` vs `1_preprocessed_XXXX.png`
3. N?u text bi?n m?t trong preprocessed ? preprocessing issue

**Before/After Example:**
```
Original:          Preprocessed (Good):   Preprocessed (Bad):
???????????       ???????????             ???????????
? ???? ?   ?   ? ?? ??  ?       ?    ? ????????? 
???????????       ???????????             ???????????
                   (Text clear)            (Text lost!)
```

**Gi?i pháp:**
Ch?nh trong `OcrService.cs`:
```csharp
// Try lighter preprocessing
var scaled = UpscaleImage(original, minWidth: 800);  // Lower from 1000
var contrasted = AdjustContrast(grayscale, contrast: 1.5f);  // Lower from 2.0
```

---

### 6. Tesseract Page Segmentation Mode

**V?n ??:**
- PSM (Page Segmentation Mode) không phù h?p
- PSM 6 = block of text (có th? không t?t cho 1 dòng)
- PSM 7 = single line (t?t h?n cho subtitle)

**Hi?n t?i (v1.2):**
```csharp
_engine.SetVariable("tessedit_pageseg_mode", "7");  // Single line mode
```

**Th? nghi?m PSM modes:**
| PSM | Description | Use Case |
|-----|-------------|----------|
| 3 | Auto (default) | Generic |
| 6 | Uniform block | Multi-line paragraph |
| 7 | Single line ? | **Subtitle (recommended)** |
| 11 | Sparse text | Short phrases |
| 13 | Raw line | Single row |

**Gi?i pháp:**
N?u PSM 7 không work, th? PSM 3 ho?c 6.

---

## ??? Debug Workflow

### Step 1: Enable Debug Mode

1. Tick **"Debug OCR (save images)"** checkbox trong UI
2. Run OCR
3. Ki?m tra folder: `%TEMP%\OCR_Debug\`

S? th?y:
```
OCR_Debug/
??? 0_original_0001.png      ? Original cropped frame
??? 1_preprocessed_0001.png  ? After preprocessing
??? 0_original_0002.png
??? 1_preprocessed_0002.png
??? ...
```

### Step 2: Phân Tích Images

**Ki?m tra `0_original_XXXX.png`:**
- ? Có text rõ ràng ? OK, v?n ?? ? preprocessing
- ? Không có text ho?c m? ? ROI selection sai

**Ki?m tra `1_preprocessed_XXXX.png`:**
- ? Text v?n rõ (?en/tr?ng t??ng ph?n) ? OK
- ? Text b? m?t ho?c blur ? Preprocessing quá m?nh

### Step 3: Log Analysis

Xem tab **Log** trong app:
```
[10:30:15] OCR completed: 2400 frames in 03:15 (avg: 12.3 fps)
[10:30:15] Results: 1823 frames with text, 577 empty
[10:30:15] ?? WARNING: Most frames returned empty! Check:
[10:30:15]   1. ROI selection is correct (covers subtitle area)
[10:30:15]   2. Language selection matches video
[10:30:15]   3. Enable Debug Mode to see preprocessed images
```

**Interpretation:**
- `577 empty / 2400 total` = 24% empty ? Normal (không ph?i m?i frame có subtitle)
- `2000 empty / 2400 total` = 83% empty ? ? Problem!

---

## ?? Solutions by Symptom

### Symptom: 80%+ Empty Results

**Probable Causes (in order):**
1. ? ROI selection wrong ? **Reselect ROI carefully**
2. ? Wrong language ? **Change language setting**
3. ? FPS too low ? **Increase FPS to 2-3**

**Quick Fix:**
```
1. Go back to step 1 "Preview & Select ROI"
2. Kéo ch?n l?i CHÍNH XÁC vùng subtitle
3. ??m b?o ROI không quá nh? (ít nh?t 1600x80 pixels)
4. Ch?n ?úng language
5. Start OCR l?i
```

### Symptom: Some Text Detected But Gibberish

**Probable Causes:**
1. ? Wrong language setting
2. ? Preprocessing too aggressive
3. ? Low video quality

**Solutions:**
1. **Change language** to match video
2. **Disable preprocessing** temporarily (advanced)
3. **Use higher quality video**

### Symptom: Text Detected But Incomplete

**Probable Causes:**
1. ? ROI too small (cuts off text)
2. ? FPS too low (misses short subtitles)

**Solutions:**
1. **Expand ROI** to include full subtitle area
2. **Increase FPS** to 3

---

## ?? Expected Results

### Good OCR Run

```
? Stats:
  - Total frames: 2400
  - Frames with text: 1800 (75%)
  - Empty frames: 600 (25%)
  - Subtitles detected: 150-200
  
? Log shows:
  - [OCR] Confidence: 85-95%
  - No "Low confidence" warnings
  
? Subtitle Preview:
  - Text readable and makes sense
  - Timestamps reasonable
  - Few duplicates
```

### Bad OCR Run

```
? Stats:
  - Total frames: 2400
  - Frames with text: 50 (2%)
  - Empty frames: 2350 (98%)
  - Subtitles detected: 0-5
  
? Log shows:
  - ?? WARNING: Most frames returned empty!
  - [OCR] Low confidence: 10-30%
  
? Subtitle Preview:
  - Empty or gibberish
  - Very few entries
```

---

## ?? Advanced Troubleshooting

### Test with Known Good Video

1. Find a video with **clear, large subtitles**
2. HD quality (1080p)
3. Simple font, high contrast
4. Test OCR on this video first
5. If works ? original video issue
6. If doesn't work ? app configuration issue

### Manually Test ROI

```csharp
// In Form1.cs, add test code:
private void TestOcr()
{
    // Extract one frame manually
    // Crop ROI
    // Save to file
    // Manually OCR with Tesseract command line
    
    // Example:
    tesseract test_frame.png output -l chi_sim
}
```

### Compare with Other OCR Tools

1. Try online OCR: https://www.newocr.com/
2. Upload cropped subtitle frame
3. If online OCR works but app doesn't ? app issue
4. If online OCR also fails ? video quality issue

---

## ?? Quick Checklist

Tr??c khi ch?y OCR, check:

- [ ] **ROI**: Ch?n CHÍNH XÁC vùng subtitle
- [ ] **Language**: Kh?p v?i video (Chinese, Japanese, etc.)
- [ ] **FPS**: ??t ít nh?t 2 (recommended)
- [ ] **Video Quality**: HD (720p+) preferred
- [ ] **Threads**: Auto (CPU-1) is fine
- [ ] **Debug Mode**: Enable n?u có v?n ??

---

## ?? Pro Tips

### 1. Test on Short Clip First

```
Thay vì process toàn b? video 20 phút:
1. C?t 1-2 phút ??u
2. Test OCR trên clip ng?n
3. Xác nh?n settings ?úng
4. Sau ?ó m?i process full video
```

### 2. Use Multiple Languages

```csharp
// N?u video có subtitle mix language (English + Chinese)
// Ch?y OCR 2 l?n:
1. First run: chi_sim
2. Second run: eng
3. Merge results
```

### 3. Adjust Contrast in Video Editor

```
N?u subtitle quá m?:
1. M? video trong editor (VLC, Premiere, etc.)
2. T?ng contrast/brightness
3. Export video m?i
4. OCR video ?ã enhance
```

---

## ?? Still Having Issues?

### Collect Debug Info

1. Enable **Debug Mode**
2. Run OCR on short clip (1 min)
3. Collect:
   - Screenshot of ROI selection
   - Screenshot of preprocessed images
   - Log output
   - Video sample (if possible)

### Report Issue

Include:
```
1. Video specs:
   - Resolution: 1920x1080
   - Format: MP4
   - Subtitle type: Hardcoded Chinese Simplified
   
2. App settings:
   - FPS: 2
   - Language: chi_sim
   - Threads: 7
   
3. Results:
   - Total frames: 2400
   - Empty: 2300 (96%)
   - Subtitles: 5
   
4. Debug images: [attach]
5. Log output: [attach]
```

---

## ? Summary

### Top 3 Fixes (90% of cases)

1. **Reselect ROI carefully** ???
   - Make sure it covers full subtitle area
   - Not too small, not too large

2. **Check Language Setting** ??
   - Must match video subtitle language
   - Chinese ? Japanese ? Korean

3. **Enable Debug Mode** ?
   - See what preprocessing does
   - Diagnose preprocessing issues

### Remember

OCR accuracy depends on:
- ? Correct ROI selection (most important!)
- ? Matching language
- ? Good video quality
- ? Appropriate preprocessing

**Good luck! ??**

---

**Version:** 1.2.1  
**Topic:** OCR Troubleshooting  
**Last Updated:** 2024


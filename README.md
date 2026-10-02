# PhotoMontage

蒙太奇相片編輯器，VB.NET + WinForms，`net8.0-windows`。

- **拼貼**：自動排版或固定版型、換位與取景、間距／圓角／背景、文字圖層、復原重做、高解析匯出。
- **馬賽克**：用大量照片拼出一張主圖，CIELAB 配色、使用次數與相鄰重複控制、疊色、超大尺寸 PNG 匯出。
可單獨執行，也可掛進 iPhoto.Net。完整規劃見 [docs/PLAN.md](docs/PLAN.md)。

## 專案結構

| 專案 | 類型 | 說明 |
|------|------|------|
| `src/PhotoMontage.Core` | 類別庫 | 資料模型、版型、排版與馬賽克演算法、渲染（不依賴 UI） |
| `src/PhotoMontage.Imaging` | 類別庫 | WIC 解碼器（解碼時縮小、EXIF、HEIC） |
| `src/PhotoMontage.WinForms` | 類別庫 | 編輯器 UI 與對外 API |
| `src/PhotoMontage.App` | WinExe | 獨立執行版（`PhotoMontage.exe`） |
| `tests/PhotoMontage.Core.Tests` | MSTest | Core 單元測試 |

## 建置與測試

```bat
dotnet build PhotoMontage.sln
dotnet test PhotoMontage.sln
dotnet run --project src\PhotoMontage.App -- C:\Photos\a.jpg C:\Photos\b.jpg
```

## 掛進 iPhoto.Net

完整步驟見 [docs/INTEGRATION.md](docs/INTEGRATION.md)。摘要：

1. 把 `PhotoMontage.Core`、`PhotoMontage.Imaging`、`PhotoMontage.WinForms` 加進 iPhoto.Net 的方案，iPhoto.Net 參考 `PhotoMontage.WinForms`。
2. 選單事件中呼叫：

   ```vb
   Dim result = PhotoMontage.MontageEditor.ShowDialog(Me, New PhotoMontage.MontageOptions With {
       .InitialPhotos = selectedFilePaths,
       .DefaultExportFolder = albumFolder,
       .CloseAfterExport = True
   })
   If result.Success Then AddPhotoToAlbum(result.OutputPath)
   ```

## 縮圖快取

縮圖存在 `%LOCALAPPDATA%\PhotoMontage\cache`，超過 500 MB 會自動刪除最久沒用的檔案；整個資料夾可隨時刪除。

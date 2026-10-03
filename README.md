# PhotoMontage

蒙太奇相片編輯器，VB.NET + WinForms（Aqua 控制項，與 iPhoto 外觀一致），`net8.0-windows`。

- **拼貼**：自動排版或 47 種版型（縮圖挑選）、拖曳分隔線調整格子大小、換位、裁切視窗（旋轉、翻轉）、間距／圓角／背景、文字圖層、復原重做、高解析匯出與列印。
- **自由拼貼**：照片任意擺放、縮放、旋轉、重疊與裁切（可選比例），白邊／拍立得外框與陰影，對齊吸附、圖層；排列方式有隨機散佈、整齊格狀、螺旋、圓環、愛心、扇形、照片堆、斜向瀑布，可選重疊與方向。
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

介面使用 **Aqua.Net**（與 iPhoto.Net 共用），預設位置為 `C:\專案\RunTime\Aqua.Net`：

```
C:\專案\
├─ PhotoMontage\
└─ RunTime\Aqua.Net\Aqua.Net.vbproj
```

放在別處時，修改 `Directory.Build.props` 的 `AquaNetProject`，或建置時加上 `-p:AquaNetProject=完整路徑`。

**Visual Studio**：開啟 `PhotoMontage.sln`（已包含 Aqua.Net），按 F5。

**命令列**：Aqua.Net 的 net35 目標需要 Visual Studio 的 MSBuild，所以命令列請直接建置專案，不要建置整個 .sln：

```bat
dotnet build src\PhotoMontage.App
dotnet test tests\PhotoMontage.Core.Tests
dotnet run --project src\PhotoMontage.App -- C:\Photos\a.jpg C:\Photos\b.jpg
```

Aqua.Net 的影片控制項依賴 LibVLC（約 200 MB 原生檔）；蒙太奇不播放影片，`Directory.Build.targets` 會把它排除在輸出之外。

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

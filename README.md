# PhotoMontage

蒙太奇相片編輯器（拼貼 / 馬賽克），VB.NET + WinForms，`net8.0-windows`。
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

1. 在 `iPhoto.Net.vbproj` 加入參考（路徑依實際 clone 位置調整）：

   ```xml
   <ItemGroup>
     <ProjectReference Include="..\..\PhotoMontage\src\PhotoMontage.WinForms\PhotoMontage.WinForms.vbproj" />
   </ItemGroup>
   ```

   並把 `PhotoMontage.Core`、`PhotoMontage.Imaging`、`PhotoMontage.WinForms` 加進 iPhoto.Net 的 .sln。
   函式庫為 AnyCPU，會隨 iPhoto 以 x86 載入，不需另外設定。

2. 在選單或工具列呼叫：

   ```vb
   Dim result = PhotoMontage.MontageEditor.ShowDialog(Me, New PhotoMontage.MontageOptions With {
       .InitialPhotos = selectedFilePaths,
       .DefaultExportFolder = albumFolder
   })
   If result.Success Then
       ' result.OutputPath 為匯出的作品
   End If
   ```

   或把 `PhotoMontage.MontageEditorControl` 放進自己的視窗，用 `AddPhotos(paths)` 加入照片（立即返回、背景讀取），訂閱 `Exported` 事件。

## 縮圖快取

縮圖存在 `%LOCALAPPDATA%\PhotoMontage\cache`，超過 500 MB 會自動刪除最久沒用的檔案；整個資料夾可隨時刪除。

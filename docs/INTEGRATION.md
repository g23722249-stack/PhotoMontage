# 掛進 iPhoto.Net

本文件說明如何把蒙太奇編輯器加到 `C:\專案\iPhoto\iPhoto.Net\iPhoto.Net.vbproj`。
已用與 iPhoto.Net 相同設定（`net8.0-windows`、WinForms、`PlatformTarget=x86`、`Option Strict Off`、自訂 `Sub Main`）的專案驗證過可編譯。

## 1. 取得程式碼

放在 iPhoto 旁邊（以下路徑都以此為準）：

```bat
cd C:\專案
git clone -b claude/quirky-rubin-3mnvhs https://github.com/g23722249-stack/PhotoMontage.git
```

完成後的位置：

```
C:\專案\
├─ iPhoto\iPhoto.Net\iPhoto.Net.vbproj
└─ PhotoMontage\src\
   ├─ PhotoMontage.Core\
   ├─ PhotoMontage.Imaging\
   └─ PhotoMontage.WinForms\
```

## 2. 加入方案與參考

PhotoMontage 的介面使用與 iPhoto 相同的 **Aqua.Net**（`C:\專案\RunTime\Aqua.Net\Aqua.Net.vbproj`），兩者參考的是同一個專案，iPhoto.Net 的方案裡已經有它，不需要重複加入。

1. 在 Visual Studio 開啟 iPhoto.Net 的 `.sln`。
2. 方案總管 → 在方案上按右鍵 → **加入 → 現有專案**，依序加入：
   - `C:\專案\PhotoMontage\src\PhotoMontage.Core\PhotoMontage.Core.vbproj`
   - `C:\專案\PhotoMontage\src\PhotoMontage.Imaging\PhotoMontage.Imaging.vbproj`
   - `C:\專案\PhotoMontage\src\PhotoMontage.WinForms\PhotoMontage.WinForms.vbproj`
3. 在 **iPhoto.Net** 專案上按右鍵 → **加入 → 專案參考** → 勾選 `PhotoMontage.WinForms`。

   或直接在 `iPhoto.Net.vbproj` 加入：

   ```xml
   <ItemGroup>
     <ProjectReference Include="..\..\PhotoMontage\src\PhotoMontage.WinForms\PhotoMontage.WinForms.vbproj" />
   </ItemGroup>
   ```

   只需參考 WinForms 專案；Core、Imaging 會自動帶入。

不需要其他設定：

- 三個函式庫都是 AnyCPU，會隨 iPhoto 以 **x86** 載入，不影響 Jet 4.0。
- 本專案自己使用 `Option Strict On`，與 iPhoto.Net 的 `Off` 互不影響。
- 命名空間是 `PhotoMontage`，不會和 `iPhoto` 衝突。

## 3. 加入選單

在主視窗（例如 `frmMain`）加一個選單項目或工具列按鈕「建立蒙太奇…」，事件處理如下。
`GetSelectedPhotoPaths`、`CurrentAlbumFolder`、`AddPhotoToAlbum` 請換成 iPhoto 現有的對應程序。

```vb
Private Sub mnuMontage_Click(sender As Object, e As EventArgs) Handles mnuMontage.Click
    ' 目前選取的照片完整路徑（沒有選取時可傳空清單，使用者可在編輯器裡再加）
    Dim photos As List(Of String) = GetSelectedPhotoPaths()

    Dim result = PhotoMontage.MontageEditor.ShowDialog(Me, New PhotoMontage.MontageOptions With {
        .Title = "建立蒙太奇",
        .InitialPhotos = photos,
        .DefaultExportFolder = CurrentAlbumFolder,   ' 匯出對話框預設的資料夾
        .CloseAfterExport = True,                    ' 匯出後自動關閉並回傳結果
        .ShowExportCompletedMessage = False,         ' 由 iPhoto 自己處理後續
        .AquaColor = Aqua.ColorConstants.Blue        ' 與 iPhoto 使用的 Aqua 主題色一致
    })

    If result.Success Then
        ' result.OutputPath 是匯出的作品，例如加入目前相簿
        AddPhotoToAlbum(result.OutputPath)
    End If
End Sub
```

### 進階：嵌入自己的視窗

```vb
Dim editor As New PhotoMontage.MontageEditorControl() With {.Dock = DockStyle.Fill}
AddHandler editor.Exported, Sub(s, ev) AddPhotoToAlbum(ev.OutputPath)
panelHost.Controls.Add(editor)
editor.AddPhotos(GetSelectedPhotoPaths())   ' 立即返回，背景讀取
```

| 成員 | 說明 |
|---|---|
| `AddPhotos(paths)` | 加入照片或資料夾，立即返回 |
| `CancelImport()` / `IsImporting` | 取消／查詢匯入 |
| `Undo()` / `Redo()` | 復原／重做 |
| `ShowExportDialog()` | 開啟匯出對話框，回傳檔案路徑或 Nothing |
| `ShowPrintDialog()` | 開啟列印對話框（選印表機、紙張、方向、邊界、完整顯示／填滿），已送出列印時回傳 True；快速鍵 Ctrl+P |
| `Exported` 事件 | 匯出成功，`e.OutputPath` 為檔案路徑 |

## 4. 發行

建置後 iPhoto 的輸出資料夾會多出：

- `PhotoMontage.Core.dll`
- `PhotoMontage.Imaging.dll`
- `PhotoMontage.WinForms.dll`
- `System.Drawing.Common.dll`（若 iPhoto 已參考其他版本，NuGet 會自動取較新的版本）

`Aqua.Net.dll` 與 LibVLC 本來就隨 iPhoto 發行，不需額外處理。

與 `iPhoto.exe` 一起發行即可。縮圖快取放在 `%LOCALAPPDATA%\PhotoMontage\cache`，可隨時刪除。

## 5. 驗收清單

1. 在 iPhoto 選幾張照片 → 「建立蒙太奇…」 → 編輯器開啟且照片已載入。
2. 調整版型、文字 → 匯出 → 編輯器自動關閉，作品出現在相簿。
3. 不選照片直接開啟 → 可在編輯器內加入照片。
4. 在編輯器開著時，於檔案總管移動其中一張原圖 → 匯出仍成功，並提示該張改用縮圖。
5. 匯出「列印 A4」→ 工作管理員觀察 iPhoto 記憶體不會暴增（x86 下應遠低於 1 GB）。

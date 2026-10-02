# 蒙太奇相片功能規劃

> 狀態：v6 · 2026-10-02（M0～M3 已完成）
> 已確認：**桌面程式**、可單獨執行、也能掛進 `iPhoto.Net.vbproj`；不需帳號/雲端；MVP 含文字圖層；不做影片輸出。

## 1. 功能定義

| 模式 | 說明 | 階段 |
|------|------|------|
| **A. 拼貼（Collage）** | 多張照片依版型排列、裁切成一張作品，可加文字 | MVP |
| **B. 馬賽克（Mosaic）** | 大量小照片當「像素」，拼出一張主圖 | 第二階段 |

使用流程：`匯入照片 → 選模式/版型 → 自動排版 → 微調（換位、取景、樣式、文字）→ 預覽 → 匯出`

## 2. 「單獨執行 + 可嵌入」的方案

核心想法：**功能全部寫在類別庫（DLL），獨立執行檔只是一層殼**。iPhoto.Net 參考同一個 DLL，就能用和獨立版完全一樣的編輯器。

```
PhotoMontage.sln
├─ src/
│  ├─ PhotoMontage.Core        (VB.NET 類別庫)  資料模型、版型、排版、匯入流程、快取、馬賽克演算法、渲染、匯出
│  ├─ PhotoMontage.Imaging     (VB.NET 類別庫)  WIC 解碼器（UseWPF），實作 Core 的 IImageCodec
│  ├─ PhotoMontage.WinForms    (VB.NET 類別庫)  MontageEditorControl (UserControl)、MontageEditorForm、對外 API
│  └─ PhotoMontage.App         (VB.NET WinExe)  獨立執行的殼：Sub Main → 開啟 MontageEditorForm
└─ tests/
   └─ PhotoMontage.Core.Tests  (MSTest)
```

```
         ┌──────────────────┐        ┌──────────────────────────┐
         │ PhotoMontage.App │        │ iPhoto.Net (既有 vbproj)  │
         │   （獨立 .exe）   │        │  選取照片 → 呼叫 API        │
         └────────┬─────────┘        └────────────┬─────────────┘
                  │ ProjectReference / DLL 參考     │
                  └──────────────┬────────────────┘
                     ┌───────────┴────────────┐
                     │ PhotoMontage.WinForms  │  UI（編輯器）
                     └───────────┬────────────┘
                     ┌───────────┴────────────┐
                     │ PhotoMontage.Core      │  不依賴任何 UI
                     └────────────────────────┘
```

### 2.1 技術選型
- **語言：VB.NET**，與 iPhoto.Net 一致，方便同一個團隊維護、除錯時可直接逐步進入。
- **UI：WinForms**，提供 `UserControl`（可塞進宿主自己的視窗）和 `Form`（彈出式對話框）兩種用法。
- **目標框架：`net8.0-windows`**，與 iPhoto.Net 相同；SDK 樣式專案，可直接用 `ProjectReference`。
- **平台：函式庫一律 AnyCPU**。iPhoto.Net 因 Jet 4.0 固定 `x86`，AnyCPU 的 DLL 會跟著以 32 位元載入，不需另外建置。
- **Option Strict On**（本專案自己的設定，與 iPhoto.Net 的 `Off` 互不影響）。
- **繪圖：System.Drawing（GDI+）**。拼貼足夠；馬賽克用 `LockBits` 直接讀寫像素，並用 `Parallel.For` 平行計算。
- **Core 不參考 WinForms**，之後若要換 WPF UI 或命令列批次處理，演算法不用動。

### 2.2 對外 API（給 iPhoto.Net 呼叫）

```vb
' 用法一：彈出對話框（最簡單）
Dim opts As New MontageOptions With {
    .InitialPhotos = selectedFilePaths,      ' iPhoto.Net 目前選取的照片
    .DefaultExportFolder = albumFolder,
    .Mode = MontageMode.Collage
}
Dim result As MontageResult = MontageEditor.ShowDialog(Me, opts)
If result.Success Then
    ' 例如把作品加回相簿
    AddPhotoToAlbum(result.OutputPath)
End If

' 用法二：嵌入宿主視窗
Dim editor As New MontageEditorControl() With {.Dock = DockStyle.Fill}
editor.LoadPhotos(selectedFilePaths)
AddHandler editor.Exported, Sub(s, e) AddPhotoToAlbum(e.OutputPath)
panelHost.Controls.Add(editor)

' 用法三：不開 UI，直接產生（批次 / 自動化）
Dim bmp As Bitmap = MontageRenderer.Render(project, New Size(3000, 3000))
```

- `MontageEditor`、`MontageOptions`、`MontageResult`、`MontageEditorControl`、`Exported` 事件就是**唯一的公開介面**，其餘標成 `Friend`，以後改內部實作不會影響 iPhoto.Net。
- 獨立版 `PhotoMontage.App` 也是呼叫 `MontageEditor.ShowDialog(Nothing, opts)`，兩邊行為一致。

### 2.3 掛進 iPhoto.Net 的方式（擇一）
1. **ProjectReference**（建議，同一個方案一起開發時）：把 `PhotoMontage.Core`、`PhotoMontage.WinForms` 加進 iPhoto.Net 的 .sln，在 `iPhoto.Net.vbproj` 加（假設 repo clone 在 `C:\專案\PhotoMontage`）
   `<ProjectReference Include="..\..\PhotoMontage\src\PhotoMontage.WinForms\PhotoMontage.WinForms.vbproj" />`
2. **DLL 參考**：直接參考建置好的 `PhotoMontage.Core.dll`、`PhotoMontage.WinForms.dll`。
3. **本機 NuGet 套件**：`dotnet pack` 後放在本機資料夾當套件來源，版本管理最乾淨。

在 iPhoto.Net 的照片清單右鍵選單或工具列加「建立蒙太奇…」，呼叫用法一即可。

## 3. 功能清單

### 3.1 拼貼模式（MVP）
- [ ] 匯入：拖放、檔案選擇、或由宿主傳入路徑清單
- [ ] 預設版型：2/3/4/6/9 格、橫幅、直幅、不規則格；照片數不固定時自動排版
- [ ] 照片自動填滿格子（cover），可在格內拖曳取景、滾輪縮放
- [ ] 拖曳交換照片位置
- [ ] 樣式：間距、圓角、背景色/背景圖
- [ ] 畫布比例：1:1、4:5、9:16、16:9、A4、自訂
- [ ] **文字圖層**：字型、大小、顏色、粗體/斜體、外框、陰影、對齊、拖曳定位與旋轉
- [ ] 復原 / 重做（Ctrl+Z / Ctrl+Y）
- [ ] 匯出 PNG / JPEG（可選品質）、解析度預設（1080px、4K、列印 300dpi）

### 3.2 馬賽克模式（第二階段）
- [ ] 選主圖 + 素材照片（資料夾或宿主傳入，建議 ≥ 100 張）
- [ ] 參數：格子數（40×40～120×120）、素材重複上限、主圖疊色強度
- [ ] 低解析度即時預覽 → 背景執行緒高解析算圖，有進度條與取消
- [ ] 點擊格子可查看/替換該格素材
- [ ] 文字圖層同樣可用

### 3.3 Backlog
- 專案存檔（`.pmontage`，JSON + 照片相對路徑）以便再編輯
- 自由拼貼（任意旋轉、疊放）
- 濾鏡 / 色調統一
- 人臉偵測，裁切時避開臉部

## 4. 資料模型（Core）

```vb
Public Class MontageProject
    Public Property Mode As MontageMode            ' Collage / Mosaic
    Public Property CanvasSize As Size
    Public Property Background As BackgroundStyle
    Public Property Photos As List(Of PhotoAsset)   ' 原圖路徑 + 縮圖 + 平均色
    Public Property Collage As CollageSettings      ' TemplateId, Gap, Radius, Cells
    Public Property Mosaic As MosaicSettings        ' TargetPhotoId, Cols, Rows, Tint, MaxRepeat
    Public Property Texts As List(Of TextLayer)
End Class

Public Class Cell
    Public Property Rect As RectangleF   ' 0~1 相對座標，與輸出解析度無關
    Public Property PhotoId As String
    Public Property Crop As CropInfo     ' OffsetX, OffsetY, Scale
End Class

Public Class TextLayer
    Public Property Text As String
    Public Property FontFamily As String
    Public Property FontSize As Single   ' 以畫布高度的比例儲存，縮放匯出時大小一致
    Public Property Color As Color
    Public Property Outline As OutlineStyle
    Public Property Shadow As ShadowStyle
    Public Property Position As PointF   ' 0~1 相對座標
    Public Property Rotation As Single
End Class
```

- **預覽與匯出共用同一個 `MontageRenderer`**，只差輸出尺寸，所見即所得。
- 復原/重做以「命令模式」實作（每個編輯動作一個 `IEditCommand`，有 `Execute` / `Undo`）。

## 5. 關鍵演算法

### 5.1 拼貼自動排版
- 依照片長寬比分配格子（橫圖進橫格），選總裁切損失最小的分配方式。
- 照片數不符合預設版型時，用 justified layout（類似 Google 相簿逐列排版）算出每列高度，使每列剛好填滿寬度。

### 5.2 馬賽克配對
1. 素材預處理：縮成 64×64，計算平均色並轉 **CIELAB**（比 RGB 更接近人眼感受）；進階可存 2×2 子區塊色保留細節。
2. 主圖切成 Cols × Rows，計算每格平均色。
3. 用 **k-d tree** 找 Lab 距離最近的素材。
4. 避免重複：限制每張素材使用次數、相鄰格不可相同。
5. 輸出時每格疊上 0～30% 的主圖原色，遠看更容易辨識。

## 6. 效能與注意事項
- 匯入時在背景產生縮圖（≤ 512px）供編輯用，**只有匯出才讀原圖**；縮圖快取在 `%LOCALAPPDATA%\PhotoMontage\cache`。
- 讀圖後依 EXIF Orientation 旋轉（手機直拍照片常見問題）。
- 讀檔用 `Image.FromStream` 並複製成新 Bitmap，避免 GDI+ 鎖住原始檔（宿主可能同時要移動或刪除該檔）。
- 所有 `Bitmap` / `Graphics` 都要 `Using` 釋放，防止長時間在宿主中執行時記憶體洩漏。
- HEIC：GDI+ 不支援。先用 WIC（需安裝 Windows「HEIF 影像延伸模組」）嘗試，失敗時提示使用者；若一定要支援可再評估 Magick.NET。
- **32 位元記憶體預算**：嵌入 iPhoto.Net 時是 x86 行程，位址空間最多約 4 GB，且大塊連續記憶體容易因碎片化配置失敗。
  - 單張 Bitmap 上限暫定 **6400 萬像素**（32bpp 約 256 MB），超過就**分塊渲染**、逐塊寫入檔案。
  - 縮圖快取用 LRU，記憶體中最多保留約 200 MB。
  - 馬賽克素材只保留 64×64 縮圖與平均色，不保留原圖。
  - 匯出前先估算記憶體需求，不足時提示降低解析度，避免宿主程式 `OutOfMemoryException` 崩潰。
- 獨立執行版為 AnyCPU（64 位元），沒有上述限制，但演算法以 x86 預算設計，兩邊行為一致。
- 高 DPI：表單設定 `AutoScaleMode.Dpi`，畫布繪製以實際像素計算。
- 長時間工作用 `Async`/`Await` + `IProgress(Of T)` + `CancellationToken`，不卡住宿主 UI 執行緒。

## 7. 開發里程碑

| 階段 | 內容 | 預估 |
|------|------|------|
| M0 ✅ | 方案骨架：Core / WinForms / App / Tests 四個專案、對外 API 雛形、內建版型與單元測試 | 0.5 週 |
| M1 ✅ | 照片匯入（WIC 縮小解碼）、縮圖快取、EXIF 轉正、縮圖清單（多選/拖曳排序/排序/移除） | 1 週 |
| M2 ✅ | 拼貼：版型系統、自動排版、渲染器、自動分配、拖曳換位、格內取景、畫布比例 | 2 週 |
| M3 ✅ | 樣式（間距、圓角、背景色、背景圖）、**文字圖層**、復原重做 | 1.5 週 |
| M4 | 匯出、**掛進 iPhoto.Net 實測** → **MVP** | 1 週 |
| M5 | 馬賽克：色彩分析、k-d tree 配對、預覽 | 2 週 |
| M6 | 馬賽克高解析分塊匯出、進度/取消、重複控制、疊色 | 1 週 |

## 8. 測試策略
- **單元測試（MSTest）**：版型座標、照片-格子配對、Lab 轉換、k-d tree、命令模式的 Undo/Redo。
- **渲染測試**：固定輸入 → 比對輸出像素差異（容許少量誤差）。
- **整合測試**：建一個最小 WinForms 宿主，以 API 開啟編輯器並匯出，模擬 iPhoto.Net 的用法。
- **效能基準**：500 張素材 × 100×100 格馬賽克的算圖時間與記憶體峰值。

## 8.5 M1 匯入設計（已實作）

流程：`Prepare`（展開資料夾、正規化路徑、去重複、副檔名與數量上限；不讀檔）→ 先顯示佔位格 →
`ProcessAsync`（背景平行：讀檔即關檔 → 檔頭判斷格式 → 讀資訊 → 超過 2 億像素拒絕 → 縮小解碼到 512px → EXIF 轉正 → 平均色 → 快取）。

- **解碼：WIC**（`PhotoMontage.Imaging.WicImageCodec`）。JPEG 解碼時直接縮小，4800 萬像素照片產生縮圖約 1 MB 記憶體；安裝 HEIF 延伸模組後可讀 HEIC。Core 只依賴 `IImageCodec`，單元測試以假解碼器替換。
- **同時解碼數量**：x86 為 2、64 位元為 4（不超過 CPU 核心數）。
- **快取**：記憶體 LRU 200 MB ＋ 磁碟 `%LOCALAPPDATA%\PhotoMontage\cache`（鍵 = 路徑＋大小＋修改時間的 SHA-256；照片修改後自動重做；損壞的快取檔自動刪除；啟動時背景整理到 500 MB 以內）。快取命中時完全不讀原圖。
- **不鎖檔**：`FileShare.ReadWrite Or Delete` 讀完立即關閉。
- **失敗分類**：找不到、沒有權限、被占用、格式不支援、損壞、缺 HEIF 解碼器、解析度過高、超過數量上限。單張失敗不中斷，清單上標紅、滑鼠停留顯示原因，下方統一顯示「N 張無法匯入」。失敗的照片重新加入時會取代舊項目。
- **取消**：已完成的保留，未處理的移除。
- **數量上限**：拼貼 100 張、馬賽克 2000 張（含既有照片）。
- **縮圖清單**：只繪製可見項目；Ctrl/Shift 多選、Ctrl+A、Delete 移除、拖曳調整順序、右鍵選單（移除、依匯入順序／檔名（自然排序）／拍攝時間排列）、拖放檔案或資料夾。
- **對外 API**：`MontageEditorControl.AddPhotos(paths)`（立即返回）、`CancelImport()`、`IsImporting`。`ShowDialog` 在視窗 `Shown` 之後才開始匯入。

已知限制：解碼時忽略色彩描述檔（避免損壞的 ICC 造成失敗），Display P3 照片縮圖顏色會略淡；匯出（M4）時再處理色彩管理。

## 8.6 M2 拼貼畫布設計（已實作）

- **版面計算（Core，皆有單元測試）**
  - `CellGeometry`：0~1 相對座標 → 像素；外緣留完整間距、相鄰格子各留一半，所有可見間距一樣寬。
  - `CropMath`：cover 填滿＋縮放（1～5 倍）＋平移（-1～1），裁切範圍永遠在照片內；拖曳時照片跟著滑鼠走。
  - `PhotoAssignment`：以**匈牙利演算法**求總裁切損失最小的配對（貪婪法會讓直圖搶走正方格、最後橫圖被塞進直長格）；損失相同時維持照片順序。
  - `JustifiedLayout`：「自動排版」依照片長寬比逐列排版，動態規劃平均分列，列數依畫布形狀選擇；所有照片裁切比例相同且很小。新專案預設使用。
  - `CanvasPresets`：1:1、4:5、9:16、2:3、3:2、4:3、16:9、A4 直／橫，長邊 2048 px。
- **渲染**：`CollageRenderer` 預覽與匯出共用（預覽傳縮圖、匯出傳原圖），支援間距與圓角裁切。
- **畫布操作**
  - 拖曳照片到另一格 → 交換。
  - 雙擊格子 → 取景模式：拖曳平移、滾輪縮放，格子外半透明顯示整張照片；Esc 或點其他地方結束。
  - 滾輪在任何有照片的格子上直接縮放；右鍵：調整取景、重設取景、清空此格；Delete 清空。
  - 從縮圖清單拖照片到格子（已在別格則互換）、雙擊縮圖放入第一個空格；已使用的縮圖顯示綠色勾勾。
  - 自動排版時，在畫布上換位會改寫照片順序並依新順序重排；重排時保留每張照片的取景。
- **更新時機**：換版型 → 重新分配；換比例 → 自動排版重排、固定版型保留位置只重設取景；匯入／移除照片 → 節流（每 250 ms 最多一次）更新：自動排版重排、固定版型補空格。
- **預覽影像**：只為放在格子裡的照片建立 512 px Bitmap，移出格子即釋放。

## 8.7 M3 樣式、文字、復原（已實作）

- **樣式**（右側「樣式」頁）：間距 0～8%、圓角 0～100%、背景色、背景圖（cover 鋪滿畫布，從間距與空格露出；預覽解碼到長邊 1600 px）。
- **文字圖層**（右側「文字」頁＋畫布）：
  - 內容（可多行）、字型（系統已安裝字型）、大小、顏色、粗體／斜體、對齊、外框（寬度＋顏色）、陰影（顏色保留半透明）、旋轉。
  - 畫布：點選文字 → 拖曳移動；拖曳上方圓點旋轉（Shift 每 15°）；Ctrl+滾輪調整大小；雙擊跳到內容輸入框；Delete／右鍵刪除；Esc 取消選取。
  - 以 `GraphicsPath.AddString` 繪製，外框與陰影都依字級等比例；字級、位置以畫布比例儲存，任何輸出尺寸都一致。找不到字型時改用系統預設字型。
- **復原／重做**（Ctrl+Z、Ctrl+Y／Ctrl+Shift+Z、工具列按鈕）：
  - 以 `DesignState`（畫布、背景、版型、格子與取景、文字）的 JSON 快照實作，最多 100 步；連續操作（拖曳、滑桿、打字）以相同 key 在 1.5 秒內合併成一步；與目前相同的快照自動略過。
  - **不含照片清單**（匯入、移除、縮圖排序不列入復原），縮圖清單是照片的唯一來源；快照中已被移除的照片會顯示為空格。
  - 自動排版時復原會先還原照片順序再重排，保留快照中的取景。
  - 輸入框有焦點時 Ctrl+Z 交給輸入框自己處理。
- `DesignState` 之後可直接作為專案存檔格式（Backlog）的基礎。

## 9. 已確認的宿主資訊（iPhoto.Net）
- SDK 樣式 vbproj，`net8.0-windows`，WinForms，`PlatformTarget=x86`（Jet 4.0），`Option Strict Off`，自訂 `Sub Main`。
- 已用模擬相同設定的宿主專案實測：可 `ProjectReference` 本專案並呼叫 `MontageEditor.ShowDialog`、`MontageEditorControl`，建置無警告。

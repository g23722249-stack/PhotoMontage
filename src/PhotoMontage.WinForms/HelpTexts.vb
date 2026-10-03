''' <summary>
''' 所有說明提示的文字（集中在此，方便統一修改用語）。
''' 標題是功能名稱（有快速鍵時附上），內容說明用途與操作方式，每行保持簡短。
''' </summary>
Friend NotInheritable Class HelpTexts

    Private Sub New()
    End Sub

#Region "編輯器"

    Public Shared ReadOnly Mode As New HelpTip("模式",
        "拼貼：照片排進格子版型。",
        "自由拼貼：照片任意擺放、旋轉、重疊。",
        "馬賽克：用許多照片拼出一張主圖。",
        "各模式的設定分開保存，切換不會遺失。")

    Public Shared ReadOnly Undo As New HelpTip("復原（Ctrl+Z）",
        "取消上一個動作，可以連續復原多步。")

    Public Shared ReadOnly Redo As New HelpTip("重做（Ctrl+Y）",
        "恢復剛才復原的動作。")

    Public Shared ReadOnly AddText As New HelpTip("新增文字",
        "在畫布中央加入一段文字，",
        "到「文字」分頁修改內容與樣式。")

    Public Shared ReadOnly AddPhotos As New HelpTip("加入照片",
        "選擇一張或多張照片加入。",
        "也可以把檔案直接拖到左側清單或畫布上。")

    Public Shared ReadOnly AddFolder As New HelpTip("加入資料夾",
        "加入資料夾（含子資料夾）中所有的照片。")

    Public Shared ReadOnly CancelImport As New HelpTip("取消讀取",
        "停止讀取剩下的照片。")

    Public Shared ReadOnly Export As New HelpTip("匯出",
        "把作品存成 JPEG 或 PNG 圖檔，",
        "可以選擇解析度與儲存位置。")

    Public Shared ReadOnly Print As New HelpTip("列印（Ctrl+P）",
        "選擇印表機與紙張後印出作品，",
        "列印前可以預覽作品在紙上的位置。")

#End Region

#Region "版面"

    Public Shared ReadOnly CanvasRatio As New HelpTip("畫布比例",
        "作品的長寬比例。",
        "變更後版型會依新比例重新排列。")

    Public Shared ReadOnly Templates As New HelpTip("版型",
        "選擇照片的排列方式。",
        "「自動排版」依照片數量與直橫方向安排。",
        "在畫布上拖曳照片可以互換位置。")

    Public Shared ReadOnly AutoAssign As New HelpTip("重新自動分配",
        "依照片的直橫方向，",
        "重新把照片放進最合適的格子。")

#End Region

#Region "樣式"

    Public Shared ReadOnly Gap As New HelpTip("間距",
        "格子之間與畫布邊緣的留白寬度。")

    Public Shared ReadOnly Radius As New HelpTip("圓角",
        "格子四角的圓弧程度，0 為直角。")

    Public Shared ReadOnly BackgroundColor As New HelpTip("背景色",
        "畫布的底色，會從間距與空格中露出。")

    Public Shared ReadOnly BackgroundImage As New HelpTip("背景圖",
        "選一張圖片鋪滿整張畫布當作背景。")

    Public Shared ReadOnly ClearBackgroundImage As New HelpTip("移除背景圖",
        "不使用背景圖，只保留背景色。")

#End Region

#Region "文字"

    Public Shared ReadOnly TextContent As New HelpTip("內容",
        "要顯示的文字，可以換行。",
        "也可以在畫布上雙擊文字直接編輯。")

    Public Shared ReadOnly TextFont As New HelpTip("字型",
        "文字使用的字型。")

    Public Shared ReadOnly TextSize As New HelpTip("大小",
        "文字大小（相對畫布）。",
        "在畫布上選取文字後，Ctrl+滾輪也能調整。")

    Public Shared ReadOnly TextColor As New HelpTip("文字顏色",
        "按一下選擇文字的顏色。")

    Public Shared ReadOnly TextBold As New HelpTip("粗體",
        "文字加粗。")

    Public Shared ReadOnly TextItalic As New HelpTip("斜體",
        "文字傾斜。")

    Public Shared ReadOnly TextAlignment As New HelpTip("對齊",
        "多行文字的對齊方式。")

    Public Shared ReadOnly TextOutline As New HelpTip("外框",
        "在文字周圍加上描邊，",
        "放在照片上會更清楚；0 為不加。")

    Public Shared ReadOnly TextOutlineColor As New HelpTip("外框色",
        "文字描邊的顏色。")

    Public Shared ReadOnly TextShadow As New HelpTip("陰影",
        "在文字後方加上陰影，增加立體感。")

    Public Shared ReadOnly TextShadowColor As New HelpTip("陰影色",
        "陰影的顏色與透明度。")

    Public Shared ReadOnly TextRotation As New HelpTip("旋轉",
        "文字的角度。",
        "在畫布上拖曳文字上方的圓點也能旋轉，",
        "按住 Shift 每 15° 對齊。")

    Public Shared ReadOnly DeleteText As New HelpTip("刪除這段文字",
        "刪除目前選取的文字（可以復原）。")

#End Region

#Region "馬賽克"

    Public Shared ReadOnly MosaicUseSelected As New HelpTip("用左側選取的照片",
        "把左側清單中選取的照片當作主圖。")

    Public Shared ReadOnly MosaicPickTarget As New HelpTip("選擇主圖檔案",
        "從電腦中選一張圖片當作主圖。")

    Public Shared ReadOnly MosaicRatio As New HelpTip("畫布比例",
        "「依主圖比例」與主圖的長寬比相同，",
        "也可以改成固定比例。")

    Public Shared ReadOnly MosaicColumns As New HelpTip("每列格數",
        "格數越多，主圖越清楚，但每張素材越小。",
        "變更後需要重新產生。")

    Public Shared ReadOnly MosaicMaxRepeat As New HelpTip("每張素材最多使用",
        "限制同一張照片最多出現幾次，0 為不限。",
        "照片越多，可以設得越低。")

    Public Shared ReadOnly MosaicAvoidAdjacent As New HelpTip("避免相鄰格子重複",
        "相鄰的格子不放同一張照片，畫面較不單調。")

    Public Shared ReadOnly MosaicTint As New HelpTip("疊上主圖",
        "把主圖半透明疊在馬賽克上，遠看更像主圖。",
        "約 10～20% 最自然；調整後不必重新產生。")

    Public Shared ReadOnly MosaicGenerate As New HelpTip("產生馬賽克",
        "依目前設定挑選素材並排出馬賽克。",
        "照片很多時需要一點時間，可以隨時取消。")

    Public Shared ReadOnly MosaicCancel As New HelpTip("取消",
        "停止產生馬賽克。")

#End Region

#Region "自由拼貼"

    Public Shared ReadOnly FreeFrame As New HelpTip("外框",
        "無：只有照片。",
        "白邊：四周等寬的白邊。",
        "拍立得：底部較寬的白邊。")

    Public Shared ReadOnly FreeFrameWidth As New HelpTip("外框寬度",
        "白邊的寬度（相對照片大小）。")

    Public Shared ReadOnly FreeSize As New HelpTip("大小",
        "照片寬度佔畫布寬度的比例。",
        "也可以拖曳照片四角，或用滑鼠滾輪縮放。")

    Public Shared ReadOnly FreeRotation As New HelpTip("旋轉",
        "照片的角度。",
        "在畫布上拖曳照片上方的圓點也能旋轉，",
        "按住 Shift 每 15° 對齊。")

    Public Shared ReadOnly FreeShadow As New HelpTip("陰影",
        "在照片下方加上柔和的陰影，",
        "看起來像放在桌上的相片。")

    Public Shared ReadOnly FreeBringToFront As New HelpTip("移到最上層",
        "讓選取的照片蓋在其他照片上面。")

    Public Shared ReadOnly FreeSendToBack As New HelpTip("移到最下層",
        "讓選取的照片放到其他照片下面。")

    Public Shared ReadOnly FreeApplyFrameToAll As New HelpTip("外框套用到全部照片",
        "把選取照片的外框、寬度與陰影，",
        "套用到畫布上所有的照片。")

    Public Shared ReadOnly FreeLooseness As New HelpTip("隨性程度",
        "「自動散佈」時照片的偏移、傾斜與重疊程度。",
        "設為「整齊」時照片不傾斜。")

    Public Shared ReadOnly FreeScatter As New HelpTip("自動散佈",
        "依隨性程度重新擺放所有照片，",
        "每按一次換一種排法。")

    Public Shared ReadOnly FreeTidy As New HelpTip("整齊排列",
        "把所有照片排成整齊、不傾斜的格狀。")

#End Region

#Region "匯出"

    Public Shared ReadOnly ExportPreset As New HelpTip("解析度",
        "分享到社群選 1080 即可；",
        "要列印請選 300 dpi 的選項。")

    Public Shared ReadOnly ExportCustomEdge As New HelpTip("自訂長邊",
        "解析度選「自訂…」時，作品長邊的像素數。")

    Public Shared ReadOnly ExportFormat As New HelpTip("格式",
        "JPEG：檔案小，適合分享。",
        "PNG：無損畫質，檔案較大。")

    Public Shared ReadOnly ExportQuality As New HelpTip("JPEG 品質",
        "數字越高畫質越好、檔案越大，",
        "一般 85～95 即可。")

    Public Shared ReadOnly ExportPath As New HelpTip("儲存位置",
        "匯出檔案的完整路徑，可以直接修改檔名。")

    Public Shared ReadOnly ExportBrowse As New HelpTip("瀏覽",
        "選擇儲存的資料夾與檔名。")

    Public Shared ReadOnly ExportStart As New HelpTip("匯出",
        "開始產生圖檔，過程中可以取消。")

#End Region

#Region "列印"

    Public Shared ReadOnly PrintPrinter As New HelpTip("印表機",
        "要使用的印表機。",
        "選「Microsoft Print to PDF」可存成 PDF。")

    Public Shared ReadOnly PrintPaper As New HelpTip("紙張",
        "紙張大小，預設為印表機的預設紙張。")

    Public Shared ReadOnly PrintOrientation As New HelpTip("方向",
        "自動：橫的作品印橫向，直的印直向。",
        "也可以固定直向或橫向。")

    Public Shared ReadOnly PrintFit As New HelpTip("版面",
        "完整顯示：整張作品印出，紙上可能留白。",
        "填滿紙張：印滿紙張，比例不同時邊緣會裁掉。")

    Public Shared ReadOnly PrintMargin As New HelpTip("邊界",
        "紙張四周的留白：無、窄（約 0.6 公分）、",
        "一般（約 1.3 公分）。")

    Public Shared ReadOnly PrintCopies As New HelpTip("份數",
        "要印幾份。")

    Public Shared ReadOnly PrintMore As New HelpTip("印表機設定",
        "開啟 Windows 的印表機對話框，",
        "可以選擇相紙、列印品質等選項。")

    Public Shared ReadOnly PrintStart As New HelpTip("列印",
        "以印表機解析度產生作品並送出列印。")

#End Region

End Class

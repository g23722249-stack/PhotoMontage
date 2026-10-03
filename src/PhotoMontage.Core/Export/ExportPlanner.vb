Imports System.Drawing

''' <summary>匯出前的計算：輸出尺寸、每張照片需要解碼的大小、記憶體估計。</summary>
Public Module ExportPlanner

    ''' <summary>單張輸出影像的像素上限（6400 萬，24 位元約 192 MB）。</summary>
    Public Const MaxOutputPixels As Long = 64_000_000L

    Public Const MinLongEdge As Integer = 256
    Public Const MaxLongEdge As Integer = 16000

    ''' <summary>依畫布比例與長邊計算輸出尺寸。</summary>
    Public Function ComputeOutputSize(canvasAspect As Double, longEdge As Integer) As Size
        If canvasAspect <= 0 OrElse Double.IsNaN(canvasAspect) Then canvasAspect = 1
        longEdge = Math.Max(1, longEdge)
        If canvasAspect >= 1 Then
            Return New Size(longEdge, Math.Max(1, CInt(Math.Round(longEdge / canvasAspect))))
        End If
        Return New Size(Math.Max(1, CInt(Math.Round(longEdge * canvasAspect))), longEdge)
    End Function

    ''' <summary>
    ''' 照片需要解碼到的長邊：剛好足夠讓格子在輸出中保有原生解析度（多留 5%），且不超過原圖。
    ''' 小格子不會解碼整張大圖，可大幅降低記憶體用量。
    ''' </summary>
    ''' <param name="photoSize">已轉正的原圖尺寸；未知時依格子大小估計。</param>
    ''' <param name="cellSize">格子在輸出影像中的像素大小。</param>
    Public Function RequiredDecodeEdge(photoSize As Size, cellSize As SizeF, crop As CropInfo) As Integer
        Dim cellLong = CInt(Math.Ceiling(Math.Max(cellSize.Width, cellSize.Height)))
        If photoSize.Width <= 0 OrElse photoSize.Height <= 0 Then Return Math.Max(64, cellLong * 2)
        If cellSize.Width <= 0 OrElse cellSize.Height <= 0 Then Return 64

        Dim photoLong = Math.Max(photoSize.Width, photoSize.Height)
        Dim oriented = PhotoOrientation.OrientedSize(New SizeF(photoSize.Width, photoSize.Height), crop)
        Dim scale = CropMath.CoverScale(oriented, cellSize) * CropMath.EffectiveScale(crop)
        Dim needed = CInt(Math.Ceiling(photoLong * Math.Min(1.0, scale * 1.05)))
        Return Math.Max(64, Math.Min(photoLong, needed))
    End Function

    ''' <summary>
    ''' 匯出時的記憶體高峰估計（位元組）：輸出影像＋最大的一張解碼照片（解碼資料與 Bitmap 各一份）＋背景圖。
    ''' </summary>
    Public Function EstimatePeakBytes(outputSize As Size, largestDecodeEdge As Integer, Optional hasBackground As Boolean = False) As Long
        Dim output = CLng(outputSize.Width) * outputSize.Height * 3
        Dim photo = CLng(largestDecodeEdge) * largestDecodeEdge * 4 * 2
        Dim bgEdge = CLng(Math.Max(outputSize.Width, outputSize.Height))
        Dim background = If(hasBackground, bgEdge * bgEdge * 4 * 2, 0L)
        Return output + photo + background
    End Function

    ''' <summary>串流寫出 PNG（馬賽克）時的像素上限：16000 × 16000。</summary>
    Public Const MaxStreamingPixels As Long = CLng(MaxLongEdge) * MaxLongEdge

    ''' <summary>
    ''' 依模式與格式檢查輸出尺寸。馬賽克的 PNG 以分段串流寫出，上限為 <see cref="MaxStreamingPixels"/>；
    ''' 其他情況需要整張 Bitmap，上限為 <see cref="MaxOutputPixels"/>。
    ''' </summary>
    Public Function ValidateOutputSize(size As Size, format As ExportFormat, mode As MontageMode) As String
        If mode = MontageMode.Mosaic AndAlso format = ExportFormat.Png Then
            If size.Width < 1 OrElse size.Height < 1 Then Return "輸出尺寸無效。"
            If size.Width > MaxLongEdge OrElse size.Height > MaxLongEdge Then Return $"長邊不可超過 {MaxLongEdge} 像素。"
            Return Nothing
        End If
        Dim invalid = ValidateOutputSize(size)
        If invalid IsNot Nothing AndAlso mode = MontageMode.Mosaic Then invalid &= "（PNG 可輸出更大的尺寸）"
        Return invalid
    End Function

    ''' <summary>檢查輸出尺寸；不合法時回傳原因，合法時回傳 Nothing。</summary>
    Public Function ValidateOutputSize(size As Size) As String
        If size.Width < 1 OrElse size.Height < 1 Then Return "輸出尺寸無效。"
        If CLng(size.Width) * size.Height > MaxOutputPixels Then
            Return $"輸出尺寸 {size.Width} × {size.Height} 超過上限（{MaxOutputPixels \ 1_000_000} 百萬像素），請降低解析度。"
        End If
        Return Nothing
    End Function

    ''' <summary>預設檔名，例如「蒙太奇_20241002_153000.jpg」。</summary>
    Public Function DefaultFileName(format As ExportFormat, now As Date) As String
        Return $"蒙太奇_{now:yyyyMMdd_HHmmss}{GetExtension(format)}"
    End Function

    Public Function GetExtension(format As ExportFormat) As String
        Return If(format = ExportFormat.Png, ".png", ".jpg")
    End Function

    ''' <summary>副檔名與格式不符時改成正確的副檔名。</summary>
    Public Function EnsureExtension(path As String, format As ExportFormat) As String
        Dim ext = IO.Path.GetExtension(path)
        Dim ok = If(format = ExportFormat.Png,
                    ext.Equals(".png", StringComparison.OrdinalIgnoreCase),
                    ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) OrElse ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        Return If(ok, path, IO.Path.ChangeExtension(path, GetExtension(format)))
    End Function

End Module

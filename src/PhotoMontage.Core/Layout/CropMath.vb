Imports System.Drawing

''' <summary>照片在格子內的取景計算：置中填滿（cover）＋縮放＋平移。</summary>
Public Module CropMath

    Public Const MinScale As Single = 1.0F
    Public Const MaxScale As Single = 5.0F

    ''' <summary>
    ''' 依取景設定算出要從照片裁出的區域（照片像素座標）。
    ''' 裁切區域的長寬比永遠等於格子，且不會超出照片範圍。
    ''' </summary>
    Public Function GetSourceRect(imageSize As SizeF, cellSize As SizeF, crop As CropInfo) As RectangleF
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 OrElse cellSize.Width <= 0 OrElse cellSize.Height <= 0 Then
            Return New RectangleF(PointF.Empty, imageSize)
        End If

        Dim s = CoverScale(imageSize, cellSize) * ClampScale(crop.Scale)
        Dim vw = cellSize.Width / s
        Dim vh = cellSize.Height / s
        Dim cx = imageSize.Width / 2 + Clamp(crop.OffsetX) * (imageSize.Width - vw) / 2
        Dim cy = imageSize.Height / 2 + Clamp(crop.OffsetY) * (imageSize.Height - vh) / 2
        Return New RectangleF(cx - vw / 2, cy - vh / 2, vw, vh)
    End Function

    ''' <summary>
    ''' 裁切視窗用：取景設定 → 轉正後照片上的裁切框（像素）。比例為 <paramref name="aspect"/>（寬 / 高）。
    ''' </summary>
    Public Function ToCropRect(orientedImageSize As SizeF, aspect As Double, crop As CropInfo) As RectangleF
        If aspect <= 0 Then aspect = orientedImageSize.Width / Math.Max(1.0F, orientedImageSize.Height)
        Return GetSourceRect(orientedImageSize, New SizeF(CSng(aspect), 1.0F), crop)
    End Function

    ''' <summary>
    ''' 裁切視窗用：轉正後照片上的裁切框 → 取景設定（旋轉、翻轉沿用 <paramref name="orientation"/>）。
    ''' 裁切框會先限制在照片範圍內，且不小於倍率上限允許的大小。
    ''' </summary>
    Public Function FromCropRect(orientedImageSize As SizeF, rect As RectangleF, orientation As CropInfo) As CropInfo
        Dim result As New CropInfo With {
            .Rotation = If(orientation Is Nothing, 0, orientation.Rotation),
            .FlipHorizontal = orientation IsNot Nothing AndAlso orientation.FlipHorizontal}
        Dim w = orientedImageSize.Width, h = orientedImageSize.Height
        If w <= 0 OrElse h <= 0 OrElse rect.Width <= 0 OrElse rect.Height <= 0 Then Return result

        Dim aspect = rect.Width / rect.Height
        Dim cover = CoverScale(orientedImageSize, New SizeF(aspect, 1.0F))   ' 最大裁切框的倍率
        Dim s = ClampScale(aspect / rect.Width / cover)
        result.Scale = s
        Dim vw = aspect / (cover * s)
        Dim vh = 1 / (cover * s)
        Dim cx = rect.X + rect.Width / 2
        Dim cy = rect.Y + rect.Height / 2
        If w - vw > 0.001F Then result.OffsetX = Clamp(CSng((cx - w / 2) / ((w - vw) / 2)))
        If h - vh > 0.001F Then result.OffsetY = Clamp(CSng((cy - h / 2) / ((h - vh) / 2)))
        Return result
    End Function

    ''' <summary>照片（原圖大小）依取景與旋轉，在轉正座標中要取用的區域。</summary>
    Public Function GetOrientedSourceRect(imageSize As SizeF, targetSize As SizeF, crop As CropInfo) As RectangleF
        Return GetSourceRect(PhotoOrientation.OrientedSize(imageSize, crop), targetSize, crop)
    End Function

    ''' <summary>
    ''' 在格子內拖曳 (<paramref name="dx"/>, <paramref name="dy"/>) 像素後的取景：照片跟著滑鼠移動。
    ''' </summary>
    Public Function Pan(crop As CropInfo, imageSize As SizeF, cellSize As SizeF, dx As Single, dy As Single) As CropInfo
        Dim result = Copy(crop)
        If imageSize.Width <= 0 OrElse imageSize.Height <= 0 OrElse cellSize.Width <= 0 OrElse cellSize.Height <= 0 Then Return result

        Dim s = CoverScale(imageSize, cellSize) * ClampScale(crop.Scale)
        Dim rangeX = (imageSize.Width - cellSize.Width / s) / 2   ' 照片像素
        Dim rangeY = (imageSize.Height - cellSize.Height / s) / 2
        If rangeX > 0.001F Then result.OffsetX = Clamp(crop.OffsetX - dx / s / rangeX)
        If rangeY > 0.001F Then result.OffsetY = Clamp(crop.OffsetY - dy / s / rangeY)
        Return result
    End Function

    ''' <summary>縮放取景（以目前畫面中心為準），倍率限制在 1 ~ 5。</summary>
    Public Function Zoom(crop As CropInfo, factor As Single) As CropInfo
        Dim result = Copy(crop)
        If factor > 0 Then result.Scale = ClampScale(crop.Scale * factor)
        Return result
    End Function

    ''' <summary>實際使用的縮放倍率（限制在 1 ~ 5）。</summary>
    Public Function EffectiveScale(crop As CropInfo) As Single
        Return ClampScale(crop.Scale)
    End Function

    ''' <summary>照片剛好蓋滿格子時，照片像素 → 格子像素的倍率。</summary>
    Public Function CoverScale(imageSize As SizeF, cellSize As SizeF) As Single
        Return Math.Max(cellSize.Width / imageSize.Width, cellSize.Height / imageSize.Height)
    End Function

    Private Function Copy(crop As CropInfo) As CropInfo
        Return New CropInfo With {.OffsetX = Clamp(crop.OffsetX), .OffsetY = Clamp(crop.OffsetY), .Scale = ClampScale(crop.Scale),
                                  .Rotation = crop.Rotation, .FlipHorizontal = crop.FlipHorizontal}
    End Function

    Private Function Clamp(value As Single) As Single
        Return Math.Max(-1.0F, Math.Min(1.0F, value))
    End Function

    Private Function ClampScale(value As Single) As Single
        Return Math.Max(MinScale, Math.Min(MaxScale, value))
    End Function

End Module

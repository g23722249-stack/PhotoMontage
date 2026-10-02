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

    ''' <summary>照片剛好蓋滿格子時，照片像素 → 格子像素的倍率。</summary>
    Public Function CoverScale(imageSize As SizeF, cellSize As SizeF) As Single
        Return Math.Max(cellSize.Width / imageSize.Width, cellSize.Height / imageSize.Height)
    End Function

    Private Function Copy(crop As CropInfo) As CropInfo
        Return New CropInfo With {.OffsetX = Clamp(crop.OffsetX), .OffsetY = Clamp(crop.OffsetY), .Scale = ClampScale(crop.Scale)}
    End Function

    Private Function Clamp(value As Single) As Single
        Return Math.Max(-1.0F, Math.Min(1.0F, value))
    End Function

    Private Function ClampScale(value As Single) As Single
        Return Math.Max(MinScale, Math.Min(MaxScale, value))
    End Function

End Module

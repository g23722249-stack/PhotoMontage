Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' 照片的旋轉（90° 為單位）與水平翻轉。取景與裁切都以「轉正後」的照片座標表示，
''' 繪製時再換回原圖座標，不需要另外產生旋轉後的影像。
''' </summary>
Public Module PhotoOrientation

    Public Function NormalizeRotation(rotation As Integer) As Integer
        Return ((rotation Mod 4) + 4) Mod 4
    End Function

    ''' <summary>轉正後的照片大小（轉 90° 或 270° 時寬高互換）。</summary>
    Public Function OrientedSize(size As SizeF, crop As CropInfo) As SizeF
        If crop IsNot Nothing AndAlso NormalizeRotation(crop.Rotation) Mod 2 = 1 Then Return New SizeF(size.Height, size.Width)
        Return size
    End Function

    Public Function OrientedSize(size As Size, crop As CropInfo) As Size
        If crop IsNot Nothing AndAlso NormalizeRotation(crop.Rotation) Mod 2 = 1 Then Return New Size(size.Height, size.Width)
        Return size
    End Function

    ''' <summary>原圖上的點 → 轉正後的點（先水平翻轉，再順時針旋轉）。</summary>
    Public Function ToOriented(p As PointF, imageSize As SizeF, crop As CropInfo) As PointF
        Dim w = imageSize.Width, h = imageSize.Height
        Dim x = If(crop.FlipHorizontal, w - p.X, p.X)
        Dim y = p.Y
        For i = 1 To NormalizeRotation(crop.Rotation)
            ' 順時針 90°：(x, y) 在 w×h → (h − y, x) 在 h×w
            Dim nx = h - y
            y = x
            x = nx
            Dim t = w
            w = h
            h = t
        Next
        Return New PointF(x, y)
    End Function

    ''' <summary>轉正後的點 → 原圖上的點。</summary>
    Public Function ToOriginal(q As PointF, imageSize As SizeF, crop As CropInfo) As PointF
        Dim oriented = OrientedSize(imageSize, crop)
        Dim w = oriented.Width, h = oriented.Height
        Dim x = q.X, y = q.Y
        For i = 1 To NormalizeRotation(crop.Rotation)
            ' 逆時針 90°：(x, y) 在 w×h → (y, w − x) 在 h×w
            Dim nx = y
            y = w - x
            x = nx
            Dim t = w
            w = h
            h = t
        Next
        If crop.FlipHorizontal Then x = imageSize.Width - x
        Return New PointF(x, y)
    End Function

    ''' <summary>
    ''' 把轉正後照片上的 <paramref name="orientedSource"/> 畫到 <paramref name="destination"/>。
    ''' 以三點平行四邊形繪製，旋轉與翻轉不需要額外影像。
    ''' </summary>
    Public Sub Draw(g As Graphics, image As Image, destination As RectangleF, orientedSource As RectangleF,
                    crop As CropInfo, Optional attrs As ImageAttributes = Nothing)
        Dim imageSize As New SizeF(image.Width, image.Height)
        Dim points = GetDrawPoints(imageSize, destination, orientedSource, crop, Nothing)
        Dim src = GetOriginalRect(imageSize, orientedSource, crop)
        g.DrawImage(image, points, src, GraphicsUnit.Pixel, attrs)
    End Sub

    ''' <summary>轉正座標的矩形對應到原圖上的矩形。</summary>
    Public Function GetOriginalRect(imageSize As SizeF, orientedRect As RectangleF, crop As CropInfo) As RectangleF
        Dim a = ToOriginal(New PointF(orientedRect.Left, orientedRect.Top), imageSize, crop)
        Dim b = ToOriginal(New PointF(orientedRect.Right, orientedRect.Bottom), imageSize, crop)
        Return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))
    End Function

    ''' <summary>
    ''' DrawImage 用的三個目標點：原圖矩形的左上、右上、左下角分別落在畫面的哪裡。
    ''' </summary>
    Public Function GetDrawPoints(imageSize As SizeF, destination As RectangleF, orientedSource As RectangleF,
                                  crop As CropInfo, originalRect As RectangleF?) As PointF()
        Dim src = If(originalRect, GetOriginalRect(imageSize, orientedSource, crop))
        Dim map = Function(p As PointF)
                      Dim q = ToOriented(p, imageSize, crop)
                      Return New PointF(destination.X + (q.X - orientedSource.X) / orientedSource.Width * destination.Width,
                                        destination.Y + (q.Y - orientedSource.Y) / orientedSource.Height * destination.Height)
                  End Function
        Return {map(New PointF(src.Left, src.Top)), map(New PointF(src.Right, src.Top)), map(New PointF(src.Left, src.Bottom))}
    End Function

End Module

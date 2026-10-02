Imports System.Drawing

''' <summary>從 <see cref="DecodedImage"/> 取樣色彩特徵。</summary>
Public Module ImageSampler

    ''' <summary>影像中央、長寬比為 <paramref name="aspect"/> 的最大區域（cover 裁切）。</summary>
    Public Function CoverRegion(width As Integer, height As Integer, aspect As Double) As RectangleF
        If aspect <= 0 Then aspect = 1
        Dim w As Double = width, h As Double = width / aspect
        If h > height Then
            h = height
            w = height * aspect
        End If
        Return New RectangleF(CSng((width - w) / 2), CSng((height - h) / 2), CSng(w), CSng(h))
    End Function

    ''' <summary>區域內的平均 RGB（至少取 1 個像素）。</summary>
    Public Function AverageRgb(image As DecodedImage, region As RectangleF) As (R As Double, G As Double, B As Double)
        Dim x0 = Math.Max(0, Math.Min(image.Width - 1, CInt(Math.Floor(region.Left))))
        Dim y0 = Math.Max(0, Math.Min(image.Height - 1, CInt(Math.Floor(region.Top))))
        Dim x1 = Math.Max(x0 + 1, Math.Min(image.Width, CInt(Math.Ceiling(region.Right))))
        Dim y1 = Math.Max(y0 + 1, Math.Min(image.Height, CInt(Math.Ceiling(region.Bottom))))

        Dim px = image.Pixels
        Dim r, g, b As Long
        For y = y0 To y1 - 1
            Dim i = (y * image.Width + x0) * 4
            For x = x0 To x1 - 1
                b += px(i)
                g += px(i + 1)
                r += px(i + 2)
                i += 4
            Next
        Next
        Dim n = CDbl((x1 - x0) * (y1 - y0))
        Return (r / n, g / n, b / n)
    End Function

    ''' <summary>區域的特徵（平均色＋2×2 子區塊）。</summary>
    Public Function Feature(image As DecodedImage, region As RectangleF) As MosaicFeature
        Dim hw = region.Width / 2, hh = region.Height / 2
        Dim parts = {
            AverageRgb(image, New RectangleF(region.X, region.Y, hw, hh)),
            AverageRgb(image, New RectangleF(region.X + hw, region.Y, hw, hh)),
            AverageRgb(image, New RectangleF(region.X, region.Y + hh, hw, hh)),
            AverageRgb(image, New RectangleF(region.X + hw, region.Y + hh, hw, hh))}
        Dim r = parts.Average(Function(p) p.R), g = parts.Average(Function(p) p.G), b = parts.Average(Function(p) p.B)
        Return New MosaicFeature(
            LabColor.FromRgb(r, g, b),
            parts.Select(Function(p) LabColor.FromRgb(p.R, p.G, p.B)).ToArray(),
            Color.FromArgb(ClampByte(r), ClampByte(g), ClampByte(b)))
    End Function

    ''' <summary>素材照片的特徵：先依格子長寬比從中央裁切。</summary>
    Public Function TileFeature(image As DecodedImage, tileAspect As Double) As MosaicFeature
        Return Feature(image, CoverRegion(image.Width, image.Height, tileAspect))
    End Function

    ''' <summary>主圖依畫布比例 cover 裁切後切成 columns × rows 格，回傳每格特徵（逐列）。</summary>
    Public Function AnalyzeGrid(image As DecodedImage, columns As Integer, rows As Integer, canvasAspect As Double) As MosaicFeature()
        If columns < 1 Then Throw New ArgumentOutOfRangeException(NameOf(columns))
        If rows < 1 Then Throw New ArgumentOutOfRangeException(NameOf(rows))
        Dim region = CoverRegion(image.Width, image.Height, canvasAspect)
        Dim cw = region.Width / columns, ch = region.Height / rows
        Dim result(columns * rows - 1) As MosaicFeature
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                result(r * columns + c) = Feature(image, New RectangleF(region.X + c * cw, region.Y + r * ch, cw, ch))
            Next
        Next
        Return result
    End Function

    Private Function ClampByte(v As Double) As Integer
        Return Math.Max(0, Math.Min(255, CInt(Math.Round(v))))
    End Function

End Module

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary><see cref="DecodedImage"/> 與 GDI+ <see cref="Bitmap"/> 的轉換。只能在 Windows 上使用。</summary>
Public Module BitmapConversion

    ''' <summary>建立同尺寸的 Bitmap。呼叫端負責 Dispose。</summary>
    Public Function ToBitmap(image As DecodedImage) As Bitmap
        If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))

        Dim bmp As New Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To image.Height - 1
                Marshal.Copy(image.Pixels, y * image.Stride, data.Scan0 + y * data.Stride, image.Stride)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function

    ''' <summary>建立長邊不超過 <paramref name="maxEdge"/> 的 Bitmap（不放大）。呼叫端負責 Dispose。</summary>
    Public Function ToBitmap(image As DecodedImage, maxEdge As Integer) As Bitmap
        If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))
        If maxEdge <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(maxEdge))

        Dim scale = Math.Min(1.0, maxEdge / Math.Max(image.Width, image.Height))
        If scale >= 1.0 Then Return ToBitmap(image)

        Dim w = Math.Max(1, CInt(Math.Round(image.Width * scale)))
        Dim h = Math.Max(1, CInt(Math.Round(image.Height * scale)))
        Using full = ToBitmap(image)
            Dim result As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using g = Graphics.FromImage(result)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.CompositingMode = CompositingMode.SourceCopy
                Using attrs As New ImageAttributes()
                    attrs.SetWrapMode(WrapMode.TileFlipXY) ' 避免邊緣出現半透明暈邊
                    g.DrawImage(full, New Rectangle(0, 0, w, h), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel, attrs)
                End Using
            End Using
            Return result
        End Using
    End Function

End Module

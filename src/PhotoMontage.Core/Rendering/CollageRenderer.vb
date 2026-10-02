Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>繪製選項。</summary>
Public Class RenderOptions
    ''' <summary>高品質內插（匯出用）；預覽可關閉以加快速度。</summary>
    Public Property HighQuality As Boolean = True

    ''' <summary>空格子的填色；Nothing 表示不畫（匯出時空格只露出背景）。</summary>
    Public Property EmptyCellColor As Color?

    ''' <summary>背景圖（已轉正），以 cover 方式鋪滿畫布；Nothing 表示只用背景色。影像由呼叫端擁有。</summary>
    Public Property BackgroundImage As Image

    Public Property DrawTexts As Boolean = True
End Class

''' <summary>
''' 拼貼渲染器。預覽與匯出共用：只差在目標大小與傳入的影像（預覽用縮圖、匯出用原圖），所見即所得。
''' </summary>
Public Module CollageRenderer

    ''' <param name="bounds">畫布在 <paramref name="g"/> 上的位置；間距、圓角都依此大小等比例計算。</param>
    ''' <param name="getImage">取得照片影像（已轉正）；回傳 Nothing 時該格視為空格。影像由呼叫端擁有。</param>
    Public Sub Render(g As Graphics, project As MontageProject, bounds As RectangleF,
                      getImage As Func(Of PhotoAsset, Image), Optional options As RenderOptions = Nothing)
        If options Is Nothing Then options = New RenderOptions()

        Dim state = g.Save()
        Try
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = If(options.HighQuality, PixelOffsetMode.HighQuality, PixelOffsetMode.Half)
            g.InterpolationMode = If(options.HighQuality, InterpolationMode.HighQualityBicubic, InterpolationMode.Bilinear)

            Using back As New SolidBrush(project.BackgroundColor)
                g.FillRectangle(back, bounds)
            End Using

            Using attrs As New ImageAttributes()
                attrs.SetWrapMode(WrapMode.TileFlipXY) ' 避免縮放時邊緣出現半透明線

                If options.BackgroundImage IsNot Nothing Then
                    Dim image = options.BackgroundImage
                    Dim src = CropMath.GetSourceRect(New SizeF(image.Width, image.Height), bounds.Size, New CropInfo())
                    Dim stateBg = g.Save()
                    g.SetClip(bounds)
                    g.DrawImage(image, Rectangle.Round(bounds), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs)
                    g.Restore(stateBg)
                End If

                Dim rects = CellGeometry.GetCellRects(project.Collage, bounds)
                For i = 0 To rects.Count - 1
                    DrawCell(g, project, project.Collage.Cells(i), rects(i), getImage, options, attrs)
                Next
            End Using

            If options.DrawTexts Then
                Dim stateText = g.Save()
                g.SetClip(bounds)
                For Each layer In project.Texts
                    TextLayerRenderer.Draw(g, layer, bounds)
                Next
                g.Restore(stateText)
            End If
        Finally
            g.Restore(state)
        End Try
    End Sub

    ''' <summary>格子的外框路徑（含圓角）。呼叫端負責 Dispose。</summary>
    Public Function CreateCellPath(rect As RectangleF, radius As Single) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2)
        If r < 0.5F Then
            path.AddRectangle(rect)
            Return path
        End If
        Dim d = r * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ''' <summary>照片在格子中實際繪製的來源區域（照片像素座標）。</summary>
    Public Function GetSourceRect(image As Image, cell As Cell, cellRect As RectangleF) As RectangleF
        Return CropMath.GetSourceRect(New SizeF(image.Width, image.Height), cellRect.Size, cell.Crop)
    End Function

    Private Sub DrawCell(g As Graphics, project As MontageProject, cell As Cell, rect As RectangleF,
                         getImage As Func(Of PhotoAsset, Image), options As RenderOptions, attrs As ImageAttributes)
        If rect.Width < 1 OrElse rect.Height < 1 Then Return

        Dim asset = project.FindPhoto(cell.PhotoId)
        Dim image = If(asset IsNot Nothing AndAlso asset.Status = PhotoStatus.Ready, getImage(asset), Nothing)

        Using path = CreateCellPath(rect, CellGeometry.GetCornerRadius(project.Collage, rect))
            If image Is Nothing Then
                If options.EmptyCellColor.HasValue Then
                    Using brush As New SolidBrush(options.EmptyCellColor.Value)
                        g.FillPath(brush, path)
                    End Using
                End If
                Return
            End If

            Dim state = g.Save()
            g.SetClip(path, CombineMode.Intersect)
            Dim src = GetSourceRect(image, cell, rect)
            ' 目標稍微外擴半像素，避免與裁切路徑之間出現縫隙
            Dim dest = Rectangle.FromLTRB(CInt(Math.Floor(rect.Left)), CInt(Math.Floor(rect.Top)), CInt(Math.Ceiling(rect.Right)), CInt(Math.Ceiling(rect.Bottom)))
            Dim sx = src.Width / rect.Width, sy = src.Height / rect.Height
            g.DrawImage(image, dest,
                        src.X - (rect.Left - dest.Left) * sx, src.Y - (rect.Top - dest.Top) * sy,
                        dest.Width * sx, dest.Height * sy, GraphicsUnit.Pixel, attrs)
            g.Restore(state)
        End Using
    End Sub

End Module

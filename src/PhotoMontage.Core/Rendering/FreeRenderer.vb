Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>
''' 自由拼貼的渲染器：背景 → 照片（依圖層由下往上，含陰影與外框）→ 文字。預覽與匯出共用。
''' </summary>
Public Module FreeRenderer

    Private ReadOnly FrameColor As Color = Color.White

    Public Sub Render(g As Graphics, project As MontageProject, bounds As RectangleF,
                      getImage As Func(Of PhotoAsset, Image), Optional options As RenderOptions = Nothing)
        If options Is Nothing Then options = New RenderOptions()
        Dim state = g.Save()
        Try
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = If(options.HighQuality, PixelOffsetMode.HighQuality, PixelOffsetMode.Half)
            g.InterpolationMode = If(options.HighQuality, InterpolationMode.HighQualityBicubic, InterpolationMode.Bilinear)

            Using attrs As New ImageAttributes()
                attrs.SetWrapMode(WrapMode.TileFlipXY)
                CollageRenderer.DrawBackground(g, project, bounds, options, attrs)

                Dim clip = g.Save()
                g.SetClip(bounds)
                For Each item In project.Free.Items
                    DrawItem(g, project, item, bounds, getImage, options, attrs)
                Next
                g.Restore(clip)
            End Using

            If options.DrawTexts Then CollageRenderer.DrawTexts(g, project, bounds)
        Finally
            g.Restore(state)
        End Try
    End Sub

    Private Sub DrawItem(g As Graphics, project As MontageProject, item As FreeItem, bounds As RectangleF,
                         getImage As Func(Of PhotoAsset, Image), options As RenderOptions, attrs As ImageAttributes)
        Dim frame = FreeGeometry.GetFrame(item, bounds)
        Dim size = frame.Size
        If size.Width < 1 OrElse size.Height < 1 Then Return

        Dim asset = project.FindPhoto(item.PhotoId)
        Dim image = If(asset IsNot Nothing AndAlso asset.Status = PhotoStatus.Ready, getImage(asset), Nothing)
        Dim state = g.Save()
        Try
            g.TranslateTransform(frame.Center.X, frame.Center.Y)
            g.RotateTransform(item.Rotation)
            Dim outer As New RectangleF(-size.Width / 2, -size.Height / 2, size.Width, size.Height)

            If item.Shadow Then DrawShadow(g, outer)
            If item.Frame <> FrameStyle.None Then
                Using brush As New SolidBrush(FrameColor)
                    g.FillRectangle(brush, outer)
                End Using
            End If

            Dim inner = FreeGeometry.GetInnerRect(item, size)
            If image Is Nothing Then
                Using brush As New SolidBrush(If(options.EmptyCellColor, Color.FromArgb(150, 150, 150)))
                    g.FillRectangle(brush, inner)
                End Using
            Else
                Dim src = CropMath.GetSourceRect(New SizeF(image.Width, image.Height), inner.Size, item.Crop)
                Dim dest = {New PointF(inner.Left, inner.Top), New PointF(inner.Right, inner.Top), New PointF(inner.Left, inner.Bottom)}
                g.DrawImage(image, dest, src, GraphicsUnit.Pixel, attrs)
            End If
        Finally
            g.Restore(state)
            If image IsNot Nothing Then options.ReleaseImage?.Invoke(asset, image)
        End Try
    End Sub

    ''' <summary>柔和的投影：往右下偏移、由外而內逐層加深。</summary>
    Private Sub DrawShadow(g As Graphics, outer As RectangleF)
        Dim unit = Math.Max(1.0F, Math.Min(outer.Width, outer.Height) * 0.012F)
        Dim offset As New PointF(unit * 1.2F, unit * 2.0F)
        For k = 4 To 1 Step -1
            Dim r = RectangleF.Inflate(outer, unit * k * 0.8F, unit * k * 0.8F)
            r.Offset(offset)
            Using brush As New SolidBrush(Color.FromArgb(16, 0, 0, 0))
                g.FillRectangle(brush, r)
            End Using
        Next
    End Sub

End Module

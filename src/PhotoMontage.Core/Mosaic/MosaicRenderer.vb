Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging

''' <summary>馬賽克的繪製。格子邊界取整數像素，相鄰格子之間不會有縫隙。</summary>
Public Module MosaicRenderer

    Private ReadOnly MissingTileColor As Color = Color.FromArgb(80, 80, 80)

    ''' <summary>第 <paramref name="index"/> 格的像素位置。</summary>
    Public Function GetCellRect(settings As MosaicSettings, bounds As RectangleF, index As Integer) As Rectangle
        Dim c = index Mod settings.Columns, r = index \ settings.Columns
        Dim x0 = CInt(Math.Round(bounds.X + c * bounds.Width / settings.Columns))
        Dim x1 = CInt(Math.Round(bounds.X + (c + 1) * bounds.Width / settings.Columns))
        Dim y0 = CInt(Math.Round(bounds.Y + r * bounds.Height / settings.Rows))
        Dim y1 = CInt(Math.Round(bounds.Y + (r + 1) * bounds.Height / settings.Rows))
        Return Rectangle.FromLTRB(x0, y0, x1, y1)
    End Function

    ''' <summary>點在第幾格；不在畫布內時回傳 -1。</summary>
    Public Function HitTest(settings As MosaicSettings, bounds As RectangleF, pt As PointF) As Integer
        If Not bounds.Contains(pt) OrElse settings.Columns < 1 OrElse settings.Rows < 1 Then Return -1
        Dim c = Math.Min(settings.Columns - 1, CInt(Math.Floor((pt.X - bounds.X) / bounds.Width * settings.Columns)))
        Dim r = Math.Min(settings.Rows - 1, CInt(Math.Floor((pt.Y - bounds.Y) / bounds.Height * settings.Rows)))
        Return r * settings.Columns + c
    End Function

    ''' <summary>格子的長寬比（寬 / 高）。</summary>
    Public Function GetTileAspect(settings As MosaicSettings, bounds As RectangleF) As Double
        Return (bounds.Width / settings.Columns) / (bounds.Height / settings.Rows)
    End Function

    ''' <summary>
    ''' 繪製與 <paramref name="visible"/> 相交的格子。同一張素材的格子一起畫，<paramref name="getTile"/> 每張素材只呼叫一次，
    ''' 畫完後呼叫 <paramref name="releaseTile"/>。
    ''' </summary>
    ''' <param name="cancellationToken">取消時停止繪製並直接返回（不擲出例外），已畫的部分保留。</param>
    Public Sub DrawTiles(g As Graphics, project As MontageProject, bounds As RectangleF, visible As RectangleF,
                         getTile As Func(Of PhotoAsset, Image), Optional releaseTile As Action(Of PhotoAsset, Image) = Nothing,
                         Optional highQuality As Boolean = True,
                         Optional cancellationToken As Threading.CancellationToken = Nothing)
        Dim settings = project.Mosaic
        If Not settings.IsGenerated Then Return

        Dim groups As New Dictionary(Of String, List(Of Rectangle))
        Dim missing As New List(Of Rectangle)
        For i = 0 To settings.CellCount - 1
            Dim rect = GetCellRect(settings, bounds, i)
            If Not rect.IntersectsWith(Rectangle.Ceiling(visible)) Then Continue For
            Dim id = settings.Tiles(i)
            If id Is Nothing Then
                missing.Add(rect)
                Continue For
            End If
            Dim list As List(Of Rectangle) = Nothing
            If Not groups.TryGetValue(id, list) Then
                list = New List(Of Rectangle)
                groups(id) = list
            End If
            list.Add(rect)
        Next

        Dim state = g.Save()
        Try
            g.InterpolationMode = If(highQuality, InterpolationMode.HighQualityBicubic, InterpolationMode.Bilinear)
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.CompositingMode = CompositingMode.SourceCopy
            Dim aspect = GetTileAspect(settings, bounds)

            Using attrs As New ImageAttributes(), gray As New SolidBrush(MissingTileColor)
                attrs.SetWrapMode(WrapMode.TileFlipXY)
                For Each pair In groups
                    If cancellationToken.IsCancellationRequested Then Return
                    Dim asset = project.FindPhoto(pair.Key)
                    Dim image = If(asset IsNot Nothing AndAlso asset.Status = PhotoStatus.Ready, getTile(asset), Nothing)
                    If image Is Nothing Then
                        missing.AddRange(pair.Value)
                        Continue For
                    End If
                    Try
                        Dim src = ImageSampler.CoverRegion(image.Width, image.Height, aspect)
                        For Each rect In pair.Value
                            g.DrawImage(image, rect, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs)
                        Next
                    Finally
                        releaseTile?.Invoke(asset, image)
                    End Try
                Next
                For Each rect In missing
                    g.FillRectangle(gray, rect)
                Next
            End Using
        Finally
            g.Restore(state)
        End Try
    End Sub

    ''' <summary>以 <paramref name="tint"/> 的不透明度疊上主圖（cover 鋪滿畫布）。</summary>
    Public Sub DrawTint(g As Graphics, bounds As RectangleF, target As Image, tint As Single)
        If target Is Nothing OrElse tint <= 0 Then Return
        Dim state = g.Save()
        Try
            g.InterpolationMode = InterpolationMode.HighQualityBilinear
            g.PixelOffsetMode = PixelOffsetMode.Half
            Dim src = ImageSampler.CoverRegion(target.Width, target.Height, bounds.Width / bounds.Height)
            Using attrs As New ImageAttributes()
                attrs.SetWrapMode(WrapMode.TileFlipXY)
                attrs.SetColorMatrix(New ColorMatrix With {.Matrix33 = Math.Min(1.0F, tint)})
                g.DrawImage(target, Rectangle.Round(bounds), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs)
            End Using
        Finally
            g.Restore(state)
        End Try
    End Sub

    ''' <summary>
    ''' 產生預覽影像（不含文字）：素材使用縮圖，逐張轉成 Bitmap、畫完即釋放。可在背景執行緒執行。
    ''' 取消時回傳 Nothing（不擲出例外，預覽經常因設定變更而被取消）。
    ''' </summary>
    Public Function RenderPreview(project As MontageProject, longEdge As Integer,
                                  getThumbnail As Func(Of PhotoAsset, DecodedImage), target As DecodedImage,
                                  cancellationToken As Threading.CancellationToken) As Bitmap
        Dim size = ExportPlanner.ComputeOutputSize(project.CanvasAspect, longEdge)
        Dim bounds As New RectangleF(0, 0, size.Width, size.Height)
        Dim result As New Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb)
        Try
            Using g = Graphics.FromImage(result)
                g.Clear(MissingTileColor)
                DrawTiles(g, project, bounds, bounds,
                    Function(asset)
                        Dim thumb = getThumbnail(asset)
                        Return If(thumb Is Nothing, Nothing, CType(BitmapConversion.ToBitmap(thumb), Image))
                    End Function,
                    Sub(asset, image) image.Dispose(), highQuality:=False, cancellationToken:=cancellationToken)
                If cancellationToken.IsCancellationRequested Then
                    result.Dispose()
                    Return Nothing
                End If
                If target IsNot Nothing AndAlso project.Mosaic.Tint > 0 Then
                    Using bmp = BitmapConversion.ToBitmap(target)
                        DrawTint(g, bounds, bmp, project.Mosaic.Tint)
                    End Using
                End If
            End Using
            Return result
        Catch
            result.Dispose()
            Throw
        End Try
    End Function

End Module

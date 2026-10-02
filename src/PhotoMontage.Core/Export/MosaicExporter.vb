Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading

''' <summary>
''' 匯出馬賽克。PNG 以水平分段繪製並串流寫檔，可輸出遠超過單張 Bitmap 上限的尺寸；
''' JPEG 一次繪製整張（上限 6400 萬像素）。素材影像放在有容量上限的 LRU 快取中，淘汰時即釋放。
''' </summary>
Public Class MosaicExporter

    ''' <summary>格子在輸出中不超過此大小時，直接使用 512 px 縮圖，不重新解碼原圖。</summary>
    Public Const ThumbnailTileLimit As Integer = 384

    ''' <summary>每段 Bitmap 的大小上限（位元組）。</summary>
    Private Const BandBytes As Long = 32L * 1024 * 1024

    Private ReadOnly _importer As PhotoImporter

    Public Sub New(importer As PhotoImporter)
        If importer Is Nothing Then Throw New ArgumentNullException(NameOf(importer))
        _importer = importer
    End Sub

    Public Function Export(project As MontageProject, settings As ExportSettings,
                           progress As IProgress(Of ExportProgress), cancellationToken As CancellationToken) As ExportResult
        If String.IsNullOrWhiteSpace(settings.FilePath) Then Throw New ArgumentException("未指定輸出檔案。", NameOf(settings))
        If Not project.Mosaic.IsGenerated Then Throw New InvalidOperationException("請先產生馬賽克。")

        Dim snapshot As New MontageProject()
        snapshot.Photos.AddRange(project.Photos)
        DesignState.Capture(project).ApplyTo(snapshot)

        Dim size = settings.GetOutputSize(snapshot.CanvasAspect)
        Dim invalid = ExportPlanner.ValidateOutputSize(size, settings.Format, MontageMode.Mosaic)
        If invalid IsNot Nothing Then Throw New InvalidOperationException(invalid)

        Dim bounds As New RectangleF(0, 0, size.Width, size.Height)
        Dim cellSize As New SizeF(bounds.Width / snapshot.Mosaic.Columns, bounds.Height / snapshot.Mosaic.Rows)
        Dim warnings As New List(Of String)
        progress?.Report(New ExportProgress(0, 1, "準備中…"))

        Dim budget = If(Environment.Is64BitProcess, 384L, 96L) * 1024 * 1024
        Using tiles As New TileSource(_importer, cellSize, budget, warnings)
            Dim target As Bitmap = Nothing
            Try
                target = LoadTarget(snapshot, warnings)
                If settings.Format = ExportFormat.Png Then
                    ExportPng(snapshot, settings, size, bounds, tiles, target, progress, cancellationToken)
                Else
                    ExportJpeg(snapshot, settings, size, bounds, tiles, target, progress, cancellationToken)
                End If
            Finally
                target?.Dispose()
            End Try
        End Using

        progress?.Report(New ExportProgress(1, 1, "完成"))
        Return New ExportResult(settings.FilePath, size, warnings)
    End Function

    Private Sub ExportJpeg(project As MontageProject, settings As ExportSettings, size As Size, bounds As RectangleF,
                           tiles As TileSource, target As Bitmap, progress As IProgress(Of ExportProgress), ct As CancellationToken)
        Using output As New Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb)
            output.SetResolution(settings.Dpi, settings.Dpi)
            Using g = Graphics.FromImage(output)
                RenderBand(g, project, bounds, New RectangleF(0, 0, size.Width, size.Height), tiles, target, ct)
            End Using
            ct.ThrowIfCancellationRequested()
            progress?.Report(New ExportProgress(1, 2, "儲存檔案…"))
            ImageFileWriter.SaveBitmap(output, settings)
        End Using
    End Sub

    Private Sub ExportPng(project As MontageProject, settings As ExportSettings, size As Size, bounds As RectangleF,
                          tiles As TileSource, target As Bitmap, progress As IProgress(Of ExportProgress), ct As CancellationToken)
        Dim bandHeight = CInt(Math.Max(16, Math.Min(size.Height, BandBytes \ (CLng(size.Width) * 3))))
        Dim bands = (size.Height + bandHeight - 1) \ bandHeight
        Dim row = New Byte(size.Width * 3 - 1) {}

        ImageFileWriter.WriteAtomically(settings.FilePath,
            Sub(temp)
                Using fs As New FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16),
                      writer As New PngStreamWriter(fs, size.Width, size.Height, settings.Dpi)
                    For b = 0 To bands - 1
                        ct.ThrowIfCancellationRequested()
                        progress?.Report(New ExportProgress(b, bands, $"繪製第 {b + 1} / {bands} 段…"))
                        Dim y0 = b * bandHeight
                        Dim h = Math.Min(bandHeight, size.Height - y0)

                        Using band As New Bitmap(size.Width, h, PixelFormat.Format24bppRgb)
                            Using g = Graphics.FromImage(band)
                                g.TranslateTransform(0, -y0)
                                RenderBand(g, project, bounds, New RectangleF(0, y0, size.Width, h), tiles, target, ct)
                            End Using

                            Dim data = band.LockBits(New Rectangle(0, 0, size.Width, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)
                            Try
                                For y = 0 To h - 1
                                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length)
                                    writer.WriteRowBgr(row, 0)
                                Next
                            Finally
                                band.UnlockBits(data)
                            End Try
                        End Using
                    Next
                    progress?.Report(New ExportProgress(bands, bands, "儲存檔案…"))
                    writer.Finish()
                End Using
            End Sub)
    End Sub

    ''' <summary>繪製畫布上 <paramref name="visible"/> 範圍（以完整畫布座標表示）內的素材、疊色與文字。</summary>
    Private Shared Sub RenderBand(g As Graphics, project As MontageProject, bounds As RectangleF, visible As RectangleF,
                                  tiles As TileSource, target As Bitmap, ct As CancellationToken)
        g.SetClip(visible)
        MosaicRenderer.DrawTiles(g, project, bounds, visible,
            Function(asset)
                ct.ThrowIfCancellationRequested()
                Return tiles.Get(asset)
            End Function,
            Sub(asset, image) tiles.Release(image))
        MosaicRenderer.DrawTint(g, bounds, target, project.Mosaic.Tint)
        For Each layer In project.Texts
            TextLayerRenderer.Draw(g, layer, bounds)
        Next
    End Sub

    Private Function LoadTarget(project As MontageProject, warnings As List(Of String)) As Bitmap
        Dim path = project.Mosaic.TargetPath
        If project.Mosaic.Tint <= 0 OrElse String.IsNullOrEmpty(path) Then Return Nothing
        Try
            Return BitmapConversion.ToBitmap(_importer.DecodeFile(path, 2048))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            warnings.Add($"主圖 {IO.Path.GetFileName(path)} 無法讀取，略過疊色")
            Return Nothing
        End Try
    End Function

    ''' <summary>提供素材影像：小格子用縮圖、大格子解碼原圖，結果放在 LRU 快取中。</summary>
    Private NotInheritable Class TileSource
        Implements IDisposable

        Private ReadOnly _importer As PhotoImporter
        Private ReadOnly _cellSize As SizeF
        Private ReadOnly _cache As LruCache(Of String, Bitmap)
        Private ReadOnly _transient As New HashSet(Of Image)
        Private ReadOnly _warnings As List(Of String)
        Private ReadOnly _warned As New HashSet(Of String)

        Public Sub New(importer As PhotoImporter, cellSize As SizeF, budget As Long, warnings As List(Of String))
            _importer = importer
            _cellSize = cellSize
            _warnings = warnings
            _cache = New LruCache(Of String, Bitmap)(budget, Function(b) CLng(b.Width) * b.Height * 4, Sub(b) b.Dispose())
        End Sub

        Public Function [Get](asset As PhotoAsset) As Image
            Dim cached As Bitmap = Nothing
            If _cache.TryGet(asset.Id, cached) Then Return cached

            Dim decoded = Load(asset)
            If decoded Is Nothing Then Return Nothing
            Dim edge = ExportPlanner.RequiredDecodeEdge(New Size(decoded.Width, decoded.Height), _cellSize, New CropInfo())
            Dim bmp = BitmapConversion.ToBitmap(decoded, edge)
            If Not _cache.Add(asset.Id, bmp) Then _transient.Add(bmp) ' 太大放不進快取：用完即釋放
            Return bmp
        End Function

        Public Sub Release(image As Image)
            If _transient.Remove(image) Then image.Dispose()
        End Sub

        Private Function Load(asset As PhotoAsset) As DecodedImage
            Dim needed = CInt(Math.Ceiling(Math.Max(_cellSize.Width, _cellSize.Height)))
            If needed > ThumbnailTileLimit Then
                Try
                    Return _importer.DecodeFile(asset.FilePath, ExportPlanner.RequiredDecodeEdge(asset.PixelSize, _cellSize, New CropInfo()))
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
                    If _warned.Add(asset.Id) Then _warnings.Add($"{asset.FileName}：原圖無法讀取，改用縮圖（畫質較低）")
                End Try
            End If
            Try
                Return _importer.LoadThumbnail(asset)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
                If _warned.Add(asset.Id & "|none") Then _warnings.Add($"{asset.FileName}：無法讀取，以灰色格子代替")
                Return Nothing
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _cache.Clear()
            For Each image In _transient
                image.Dispose()
            Next
            _transient.Clear()
        End Sub
    End Class

End Class

''' <summary>依作品模式選擇拼貼或馬賽克匯出。</summary>
Public Class MontageExporter
    Private ReadOnly _collage As CollageExporter
    Private ReadOnly _mosaic As MosaicExporter

    Public Sub New(importer As PhotoImporter)
        _collage = New CollageExporter(importer)
        _mosaic = New MosaicExporter(importer)
    End Sub

    Public Function Export(project As MontageProject, settings As ExportSettings,
                           progress As IProgress(Of ExportProgress), cancellationToken As CancellationToken) As ExportResult
        If project.Mode = MontageMode.Mosaic Then Return _mosaic.Export(project, settings, progress, cancellationToken)
        Return _collage.Export(project, settings, progress, cancellationToken)
    End Function
End Class

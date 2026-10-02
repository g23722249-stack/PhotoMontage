Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Threading

''' <summary>匯出進度。</summary>
Public NotInheritable Class ExportProgress
    Public ReadOnly Property Completed As Integer
    Public ReadOnly Property Total As Integer
    Public ReadOnly Property Message As String

    Public Sub New(completed As Integer, total As Integer, message As String)
        Me.Completed = completed
        Me.Total = total
        Me.Message = message
    End Sub
End Class

''' <summary>匯出結果。</summary>
Public NotInheritable Class ExportResult
    Public ReadOnly Property OutputPath As String
    Public ReadOnly Property OutputSize As Size
    ''' <summary>非致命問題，例如原圖找不到而改用縮圖。</summary>
    Public ReadOnly Property Warnings As IReadOnlyList(Of String)

    Public Sub New(outputPath As String, outputSize As Size, warnings As IReadOnlyList(Of String))
        Me.OutputPath = outputPath
        Me.OutputSize = outputSize
        Me.Warnings = warnings
    End Sub
End Class

''' <summary>
''' 以原圖產生最終作品。照片逐格解碼（只解碼到格子需要的大小）、畫完立刻釋放，
''' 同一時間只有一張照片在記憶體中，適合 x86 宿主。可在背景執行緒執行。
''' </summary>
Public Class CollageExporter

    Private ReadOnly _importer As PhotoImporter

    Public Sub New(importer As PhotoImporter)
        If importer Is Nothing Then Throw New ArgumentNullException(NameOf(importer))
        _importer = importer
    End Sub

    ''' <exception cref="OperationCanceledException">已取消。</exception>
    ''' <exception cref="InvalidOperationException">輸出尺寸不合法。</exception>
    ''' <exception cref="IOException">寫檔失敗。</exception>
    ''' <exception cref="OutOfMemoryException">記憶體不足（請降低解析度）。</exception>
    Public Function Export(project As MontageProject, settings As ExportSettings,
                           progress As IProgress(Of ExportProgress), cancellationToken As CancellationToken) As ExportResult
        If String.IsNullOrWhiteSpace(settings.FilePath) Then Throw New ArgumentException("未指定輸出檔案。", NameOf(settings))

        ' 複製一份設計，避免匯出途中 UI 修改專案
        Dim snapshot As New MontageProject With {.Mode = project.Mode}
        snapshot.Photos.AddRange(project.Photos)
        DesignState.Capture(project).ApplyTo(snapshot)

        Dim size = settings.GetOutputSize(snapshot.CanvasAspect)
        Dim invalid = ExportPlanner.ValidateOutputSize(size)
        If invalid IsNot Nothing Then Throw New InvalidOperationException(invalid)

        Dim bounds As New RectangleF(0, 0, size.Width, size.Height)
        Dim edges = PlanDecodeEdges(snapshot, bounds)
        Dim total = snapshot.Collage.Cells.Where(Function(c) snapshot.FindPhoto(c.PhotoId) IsNot Nothing).Count() + 1
        Dim completed = 0
        Dim warnings As New List(Of String)
        progress?.Report(New ExportProgress(0, total, "準備中…"))

        Dim background As Bitmap = Nothing
        Try
            If snapshot.BackgroundImagePath IsNot Nothing Then
                background = LoadBackground(snapshot.BackgroundImagePath, Math.Max(size.Width, size.Height), warnings)
            End If

            Using output As New Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb)
                output.SetResolution(settings.Dpi, settings.Dpi)
                Using g = Graphics.FromImage(output)
                    Dim options As New RenderOptions With {
                        .HighQuality = True,
                        .BackgroundImage = background,
                        .ReleaseImage = Sub(asset, image)
                                            image.Dispose()
                                            completed += 1
                                            progress?.Report(New ExportProgress(completed, total, asset.FileName))
                                        End Sub}
                    CollageRenderer.Render(g, snapshot, bounds,
                        Function(asset)
                            cancellationToken.ThrowIfCancellationRequested()
                            Dim edge = 0
                            edges.TryGetValue(asset.Id, edge)
                            Return LoadPhoto(asset, edge, warnings)
                        End Function, options)
                End Using

                cancellationToken.ThrowIfCancellationRequested()
                progress?.Report(New ExportProgress(completed, total, "儲存檔案…"))
                ImageFileWriter.SaveBitmap(output, settings)
            End Using
        Finally
            background?.Dispose()
        End Try

        progress?.Report(New ExportProgress(total, total, "完成"))
        Return New ExportResult(settings.FilePath, size, warnings)
    End Function

    ''' <summary>每張照片需要解碼的長邊（同一張照片放在多格時取最大）。</summary>
    Friend Shared Function PlanDecodeEdges(project As MontageProject, bounds As RectangleF) As Dictionary(Of String, Integer)
        Dim edges As New Dictionary(Of String, Integer)
        Dim rects = CellGeometry.GetCellRects(project.Collage, bounds)
        For i = 0 To rects.Count - 1
            Dim cell = project.Collage.Cells(i)
            Dim asset = project.FindPhoto(cell.PhotoId)
            If asset Is Nothing Then Continue For
            Dim edge = ExportPlanner.RequiredDecodeEdge(asset.PixelSize, rects(i).Size, cell.Crop)
            Dim current = 0
            If Not edges.TryGetValue(asset.Id, current) OrElse edge > current Then edges(asset.Id) = edge
        Next
        Return edges
    End Function

    ''' <summary>讀原圖；讀不到時改用快取縮圖並記錄警告，兩者都失敗則該格留空。</summary>
    Private Function LoadPhoto(asset As PhotoAsset, edge As Integer, warnings As List(Of String)) As Image
        If asset.Status <> PhotoStatus.Ready Then Return Nothing
        Try
            Return BitmapConversion.ToBitmap(_importer.DecodeFile(asset.FilePath, Math.Max(64, edge)))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            Try
                Dim thumb = _importer.LoadThumbnail(asset)
                SyncLock warnings
                    warnings.Add($"{asset.FileName}：原圖無法讀取，改用縮圖（畫質較低）")
                End SyncLock
                Return BitmapConversion.ToBitmap(thumb)
            Catch ex2 As Exception When TypeOf ex2 Is IOException OrElse TypeOf ex2 Is UnauthorizedAccessException OrElse TypeOf ex2 Is ImageDecodeException
                SyncLock warnings
                    warnings.Add($"{asset.FileName}：無法讀取，該格留空")
                End SyncLock
                Return Nothing
            End Try
        End Try
    End Function

    Private Function LoadBackground(path As String, edge As Integer, warnings As List(Of String)) As Bitmap
        Try
            Return BitmapConversion.ToBitmap(_importer.DecodeFile(path, edge))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            warnings.Add($"背景圖 {IO.Path.GetFileName(path)} 無法讀取，只使用背景色")
            Return Nothing
        End Try
    End Function

End Class

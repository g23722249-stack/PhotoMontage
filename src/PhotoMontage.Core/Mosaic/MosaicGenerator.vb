Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading

''' <summary>一次分析的結果：主圖每格特徵、可用素材與其特徵。用於產生與「換成下一個相近的素材」。</summary>
Public NotInheritable Class MosaicAnalysis
    Public ReadOnly Property Columns As Integer
    Public ReadOnly Property Rows As Integer
    Public ReadOnly Property CellFeatures As IReadOnlyList(Of MosaicFeature)
    Public ReadOnly Property TileAssets As IReadOnlyList(Of PhotoAsset)
    Public ReadOnly Property TileFeatures As IReadOnlyList(Of MosaicFeature)

    Public Sub New(columns As Integer, rows As Integer, cellFeatures As IReadOnlyList(Of MosaicFeature),
                   tileAssets As IReadOnlyList(Of PhotoAsset), tileFeatures As IReadOnlyList(Of MosaicFeature))
        Me.Columns = columns
        Me.Rows = rows
        Me.CellFeatures = cellFeatures
        Me.TileAssets = tileAssets
        Me.TileFeatures = tileFeatures
    End Sub

    Public Function IndexOfTile(photoId As String) As Integer
        For i = 0 To TileAssets.Count - 1
            If TileAssets(i).Id = photoId Then Return i
        Next
        Return -1
    End Function

    ''' <summary>分析結果是否仍適用於目前的設定。</summary>
    Public Function Matches(settings As MosaicSettings) As Boolean
        Return Columns = settings.Columns AndAlso Rows = settings.Rows
    End Function
End Class

''' <summary>
''' 產生馬賽克：解碼主圖 → 切格分析 → 分析素材（使用縮圖快取，結果另外快取）→ 配對。可在背景執行緒執行。
''' </summary>
Public Class MosaicGenerator

    Public Const MinTiles As Integer = 2
    Public Const RecommendedTiles As Integer = 100

    Private ReadOnly _importer As PhotoImporter
    Private ReadOnly _features As New ConcurrentDictionary(Of String, MosaicFeature)

    Public Sub New(importer As PhotoImporter)
        If importer Is Nothing Then Throw New ArgumentNullException(NameOf(importer))
        _importer = importer
    End Sub

    ''' <summary>可用的素材：已匯入成功的照片。</summary>
    Public Shared Function GetTileCandidates(project As MontageProject) As List(Of PhotoAsset)
        Return project.Photos.Where(Function(p) p.Status = PhotoStatus.Ready).ToList()
    End Function

    ''' <exception cref="InvalidOperationException">沒有主圖或素材不足。</exception>
    ''' <exception cref="IOException">主圖讀取失敗。</exception>
    ''' <exception cref="ImageDecodeException">主圖格式不支援。</exception>
    Public Function Analyze(project As MontageProject, progress As IProgress(Of ExportProgress), cancellationToken As CancellationToken) As MosaicAnalysis
        Dim settings = project.Mosaic
        If String.IsNullOrWhiteSpace(settings.TargetPath) Then Throw New InvalidOperationException("請先選擇主圖。")
        Dim candidates = GetTileCandidates(project)
        If candidates.Count < MinTiles Then Throw New InvalidOperationException($"至少需要 {MinTiles} 張素材照片。")

        Dim cols = settings.Columns, rows = settings.Rows
        Dim total = candidates.Count + 1
        progress?.Report(New ExportProgress(0, total, "分析主圖…"))

        Dim edge = Math.Max(256, Math.Min(2048, Math.Max(cols, rows) * 8))
        Dim target = _importer.DecodeFile(settings.TargetPath, edge)
        Dim cells = ImageSampler.AnalyzeGrid(target, cols, rows, project.CanvasAspect)

        Dim tileAspect = (project.CanvasSize.Width / cols) / (project.CanvasSize.Height / CDbl(rows))
        Dim aspectKey = tileAspect.ToString("0.000", Globalization.CultureInfo.InvariantCulture)
        Dim results(candidates.Count - 1) As MosaicFeature
        Dim done = 0
        Dim options As New ParallelOptions With {.CancellationToken = cancellationToken, .MaxDegreeOfParallelism = _importer.MaxParallel}
        Parallel.For(0, candidates.Count, options,
            Sub(i)
                results(i) = GetTileFeature(candidates(i), tileAspect, aspectKey)
                Dim n = Interlocked.Increment(done)
                If n Mod 20 = 0 OrElse n = candidates.Count Then progress?.Report(New ExportProgress(n, total, "分析素材…"))
            End Sub)

        Dim assets As New List(Of PhotoAsset)
        Dim features As New List(Of MosaicFeature)
        For i = 0 To candidates.Count - 1
            If results(i) Is Nothing Then Continue For
            assets.Add(candidates(i))
            features.Add(results(i))
        Next
        If assets.Count < MinTiles Then Throw New InvalidOperationException($"可讀取的素材不足 {MinTiles} 張。")
        Return New MosaicAnalysis(cols, rows, cells, assets, features)
    End Function

    ''' <summary>分析並配對，回傳分析結果與每格的素材 Id。</summary>
    Public Function Generate(project As MontageProject, progress As IProgress(Of ExportProgress), cancellationToken As CancellationToken) As (Analysis As MosaicAnalysis, Tiles As List(Of String))
        Dim analysis = Me.Analyze(project, progress, cancellationToken)
        cancellationToken.ThrowIfCancellationRequested()
        progress?.Report(New ExportProgress(analysis.TileAssets.Count, analysis.TileAssets.Count + 1, "配對中…"))

        Dim settings = project.Mosaic
        Dim assignment = MosaicMatcher.Match(analysis.CellFeatures, analysis.Columns, analysis.TileFeatures, New MosaicMatchOptions With {
            .MaxRepeat = settings.MaxRepeat,
            .AvoidAdjacentDuplicates = settings.AvoidAdjacentDuplicates})
        Dim tiles = assignment.Select(Function(i) analysis.TileAssets(i).Id).ToList()
        Return (analysis, tiles)
    End Function

    ''' <summary>素材特徵（依縮圖快取鍵與格子比例快取）；縮圖讀不到時回傳 Nothing。</summary>
    Private Function GetTileFeature(asset As PhotoAsset, tileAspect As Double, aspectKey As String) As MosaicFeature
        Dim key = If(asset.ThumbnailKey, asset.Id) & "|" & aspectKey
        Dim feature As MosaicFeature = Nothing
        If _features.TryGetValue(key, feature) Then Return feature
        Try
            feature = ImageSampler.TileFeature(_importer.LoadThumbnail(asset), tileAspect)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            Return Nothing
        End Try
        _features(key) = feature
        Return feature
    End Function

End Class

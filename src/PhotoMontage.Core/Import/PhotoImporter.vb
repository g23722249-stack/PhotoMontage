Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading

''' <summary>
''' 照片匯入流程：檢查 → 讀資訊 → 縮小解碼 → 轉正 → 平均色 → 快取。
''' 分成 <see cref="Prepare"/>（快速，可先顯示佔位格）與 <see cref="ProcessAsync"/>（背景解碼）兩段。
''' </summary>
Public Class PhotoImporter

    Private ReadOnly _codec As IImageCodec
    Private ReadOnly _cache As ThumbnailCache
    Private ReadOnly _limits As ImportLimits

    ''' <param name="maxParallel">同時解碼的數量；0 表示自動（x86 為 2，64 位元為 4）。</param>
    Public Sub New(codec As IImageCodec, cache As ThumbnailCache, Optional limits As ImportLimits = Nothing, Optional maxParallel As Integer = 0)
        If codec Is Nothing Then Throw New ArgumentNullException(NameOf(codec))
        If cache Is Nothing Then Throw New ArgumentNullException(NameOf(cache))
        _codec = codec
        _cache = cache
        _limits = If(limits, New ImportLimits())
        Me.MaxParallel = If(maxParallel > 0, maxParallel,
                            Math.Max(1, Math.Min(Environment.ProcessorCount, If(Environment.Is64BitProcess, 4, 2))))
    End Sub

    Public ReadOnly Property MaxParallel As Integer

    Public ReadOnly Property Limits As ImportLimits
        Get
            Return _limits
        End Get
    End Property

    ''' <summary>
    ''' 展開資料夾、正規化路徑、去除重複、檢查副檔名與數量上限。不讀取檔案內容。
    ''' </summary>
    ''' <param name="paths">檔案或資料夾路徑（資料夾會含子資料夾）。</param>
    ''' <param name="existing">專案中已有的照片；失敗的照片不算，可重新加入。</param>
    ''' <param name="maxCount">專案照片總數上限（含既有照片）。</param>
    Public Function Prepare(paths As IEnumerable(Of String), existing As IEnumerable(Of PhotoAsset), maxCount As Integer) As ImportBatch
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim existingCount = 0
        If existing IsNot Nothing Then
            For Each asset In existing
                If asset.Status = PhotoStatus.Failed Then Continue For
                seen.Add(asset.FilePath)
                existingCount += 1
            Next
        End If

        Dim assets As New List(Of PhotoAsset)
        Dim failures As New List(Of ImportFailure)
        Dim remaining = Math.Max(0, maxCount - existingCount)

        For Each file In ExpandPaths(paths, failures)
            If Not seen.Add(file) Then Continue For
            If assets.Count >= remaining Then
                failures.Add(New ImportFailure(file, ImportFailureReason.LimitReached))
            Else
                assets.Add(New PhotoAsset(file))
            End If
        Next
        Return New ImportBatch(assets, failures)
    End Function

    ''' <summary>
    ''' 在背景平行處理照片，每完成一張就經由 <paramref name="progress"/> 回報（含縮圖）。
    ''' 取消時擲出 <see cref="OperationCanceledException"/>；已處理的照片狀態保留，其餘維持 Pending。
    ''' </summary>
    Public Async Function ProcessAsync(batch As ImportBatch, progress As IProgress(Of ImportProgress), cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of ImportFailure))
        If batch Is Nothing Then Throw New ArgumentNullException(NameOf(batch))

        Dim failures As New ConcurrentQueue(Of ImportFailure)
        Dim total = batch.Assets.Count
        Dim completed = 0

        Using gate As New SemaphoreSlim(MaxParallel)
            Dim tasks = batch.Assets.Select(
                Async Function(asset) As Task
                    Await gate.WaitAsync(cancellationToken).ConfigureAwait(False)
                    Try
                        Dim thumbnail As DecodedImage = Nothing
                        Dim failure = Await Task.Run(Function() ProcessOne(asset, thumbnail, cancellationToken), cancellationToken).ConfigureAwait(False)
                        If failure IsNot Nothing Then failures.Enqueue(failure)
                        progress?.Report(New ImportProgress(Interlocked.Increment(completed), total, asset, thumbnail, failure))
                    Finally
                        gate.Release()
                    End Try
                End Function).ToList()

            Await Task.WhenAll(tasks).ConfigureAwait(False)
        End Using
        Return failures.ToList()
    End Function

    ''' <summary>
    ''' 取得照片的縮圖：先找快取，沒有就重新解碼。照片須已匯入成功。
    ''' </summary>
    Public Function LoadThumbnail(asset As PhotoAsset) As DecodedImage
        If asset Is Nothing Then Throw New ArgumentNullException(NameOf(asset))
        If asset.ThumbnailKey IsNot Nothing Then
            Dim cached = _cache.TryGet(asset.ThumbnailKey)
            If cached IsNot Nothing Then Return cached.Image
        End If

        Dim thumbnail As DecodedImage = Nothing
        Dim failure = ProcessOne(asset, thumbnail, CancellationToken.None)
        If failure IsNot Nothing Then Throw New IOException($"{failure}")
        Return thumbnail
    End Function

    ''' <summary>
    ''' 直接解碼任意圖檔到長邊不超過 <paramref name="maxEdge"/>（已轉正），不經過快取。用於背景圖等單張影像。
    ''' </summary>
    ''' <exception cref="IOException">讀檔失敗。</exception>
    ''' <exception cref="ImageDecodeException">格式不支援或檔案損壞。</exception>
    Public Function DecodeFile(path As String, maxEdge As Integer) As DecodedImage
        Using ms As New MemoryStream(ReadShared(path), writable:=False)
            If ImageFormatSniffer.Detect(ms) = ImageFileFormat.Unknown Then Throw New ImageDecodeException("不支援的檔案格式。", False)
            Dim info = _codec.ReadInfo(ms)
            If info.PixelCount > _limits.MaxPixels Then Throw New ImageDecodeException("照片解析度過高。", False)
            ms.Position = 0
            Return ExifOrientations.Apply(_codec.DecodeThumbnail(ms, maxEdge), info.Orientation)
        End Using
    End Function

    ''' <summary>一次讀完並立即關檔，不鎖住原圖（宿主可能同時要搬移或刪除）。</summary>
    Private Shared Function ReadShared(path As String) As Byte()
        Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Dim data = New Byte(CInt(fs.Length) - 1) {}
            fs.ReadExactly(data)
            Return data
        End Using
    End Function

    ''' <summary>處理單張照片。成功回傳 Nothing 並設定 <paramref name="thumbnail"/>。</summary>
    Friend Function ProcessOne(asset As PhotoAsset, ByRef thumbnail As DecodedImage, cancellationToken As CancellationToken) As ImportFailure
        cancellationToken.ThrowIfCancellationRequested()
        thumbnail = Nothing

        Try
            Dim file As New FileInfo(asset.FilePath)
            If Not file.Exists Then Return Fail(asset, ImportFailureReason.NotFound)

            Dim key = ThumbnailCache.ComputeKey(file.FullName, file.Length, file.LastWriteTimeUtc)
            Dim entry = _cache.TryGet(key)
            If entry Is Nothing Then
                Dim failure As ImportFailure = Nothing
                entry = Decode(asset, failure, cancellationToken)
                If failure IsNot Nothing Then Return failure
                _cache.Put(key, entry)
            End If

            asset.FileSize = file.Length
            asset.LastWriteTimeUtc = file.LastWriteTimeUtc
            asset.PixelSize = entry.Info.OrientedSize
            asset.Orientation = entry.Info.Orientation
            asset.DateTaken = entry.Info.DateTaken
            asset.AverageColor = entry.AverageColor
            asset.ThumbnailKey = key
            asset.Status = PhotoStatus.Ready
            asset.FailureReason = Nothing
            thumbnail = entry.Image
            Return Nothing

        Catch ex As Exception When TypeOf ex Is FileNotFoundException OrElse TypeOf ex Is DirectoryNotFoundException
            Return Fail(asset, ImportFailureReason.NotFound)
        Catch ex As UnauthorizedAccessException
            Return Fail(asset, ImportFailureReason.AccessDenied)
        Catch ex As IOException
            Return Fail(asset, ImportFailureReason.FileInUse)
        End Try
    End Function

    Private Function Decode(asset As PhotoAsset, ByRef failure As ImportFailure, cancellationToken As CancellationToken) As CachedThumbnail
        Using ms As New MemoryStream(ReadShared(asset.FilePath), writable:=False)
            Dim format = ImageFormatSniffer.Detect(ms)
            If format = ImageFileFormat.Unknown Then
                failure = Fail(asset, ImportFailureReason.Unsupported)
                Return Nothing
            End If

            Try
                Dim info = _codec.ReadInfo(ms)
                If info.PixelCount > _limits.MaxPixels Then
                    failure = Fail(asset, ImportFailureReason.TooLarge)
                    Return Nothing
                End If
                cancellationToken.ThrowIfCancellationRequested()

                ms.Position = 0
                Dim raw = _codec.DecodeThumbnail(ms, _limits.ThumbnailMaxEdge)
                Return New CachedThumbnail(info, ExifOrientations.Apply(raw, info.Orientation))

            Catch ex As ImageDecodeException When ex.CodecMissing
                failure = Fail(asset, If(format = ImageFileFormat.Heif, ImportFailureReason.HeifCodecMissing, ImportFailureReason.Unsupported))
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                failure = Fail(asset, ImportFailureReason.Corrupt)
            End Try
        End Using
        Return Nothing
    End Function

    Private Shared Function Fail(asset As PhotoAsset, reason As ImportFailureReason) As ImportFailure
        asset.Status = PhotoStatus.Failed
        asset.FailureReason = reason
        Return New ImportFailure(asset.FilePath, reason)
    End Function

    ''' <summary>把輸入路徑展開成完整檔案路徑。資料夾內只取支援的副檔名，依路徑排序。</summary>
    Private Shared Iterator Function ExpandPaths(paths As IEnumerable(Of String), failures As List(Of ImportFailure)) As IEnumerable(Of String)
        If paths Is Nothing Then Return

        For Each p In paths
            If String.IsNullOrWhiteSpace(p) Then Continue For

            Dim full As String
            Try
                full = Path.GetFullPath(p.Trim())
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
                failures.Add(New ImportFailure(p, ImportFailureReason.NotFound))
                Continue For
            End Try

            If IO.Directory.Exists(full) Then
                Dim options As New EnumerationOptions With {.RecurseSubdirectories = True, .IgnoreInaccessible = True}
                Dim files = IO.Directory.EnumerateFiles(full, "*", options).
                    Where(AddressOf ImageFormatSniffer.IsSupportedExtension).
                    OrderBy(Function(f) f, StringComparer.OrdinalIgnoreCase)
                For Each f In files
                    Yield f
                Next
            ElseIf Not IO.File.Exists(full) Then
                failures.Add(New ImportFailure(full, ImportFailureReason.NotFound))
            ElseIf Not ImageFormatSniffer.IsSupportedExtension(full) Then
                failures.Add(New ImportFailure(full, ImportFailureReason.Unsupported))
            Else
                Yield full
            End If
        Next
    End Function

End Class

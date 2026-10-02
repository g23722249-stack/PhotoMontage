Imports System.IO
Imports System.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class PhotoImporterTests

    Private _dir As TempFolder
    Private _codec As FakeCodec
    Private _importer As PhotoImporter

    <TestInitialize>
    Public Sub Setup()
        _dir = New TempFolder()
        _codec = New FakeCodec()
        _importer = New PhotoImporter(_codec, New ThumbnailCache(_codec, _dir.Combine("cache")), maxParallel:=2)
    End Sub

    <TestCleanup>
    Public Sub Cleanup()
        _dir.Dispose()
    End Sub

    Private Function Photo(name As String, Optional width As Integer = 4000, Optional height As Integer = 3000,
                           Optional orientation As ExifOrientation = ExifOrientation.Normal,
                           Optional flags As Byte = 0, Optional header As Byte() = Nothing) As String
        Dim path = _dir.Combine(name)
        Directory.CreateDirectory(IO.Path.GetDirectoryName(path))
        FakeCodec.WriteFile(path, width, height, orientation, flags:=flags, header:=header)
        Return path
    End Function

    Private Function Run(ParamArray paths As String()) As (Batch As ImportBatch, Failures As IReadOnlyList(Of ImportFailure))
        Dim batch = _importer.Prepare(paths, Nothing, 100)
        Dim failures = _importer.ProcessAsync(batch, Nothing, CancellationToken.None).GetAwaiter().GetResult()
        Return (batch, failures)
    End Function

#Region "Prepare"

    <TestMethod>
    Public Sub Prepare_RemovesDuplicatesIgnoringCaseAndRelativePaths()
        Dim a = Photo("a.jpg")
        Dim relative = Path.GetRelativePath(Environment.CurrentDirectory, a)

        Dim batch = _importer.Prepare({a, a.ToUpperInvariant(), relative}, Nothing, 100)

        Assert.AreEqual(1, batch.Assets.Count)
        Assert.AreEqual(Path.GetFullPath(a), batch.Assets(0).FilePath)
    End Sub

    <TestMethod>
    Public Sub Prepare_SkipsPhotosAlreadyInProject_ButAllowsRetryOfFailed()
        Dim a = Photo("a.jpg"), b = Photo("b.jpg")
        Dim existing = {New PhotoAsset(a) With {.Status = PhotoStatus.Ready}, New PhotoAsset(b) With {.Status = PhotoStatus.Failed}}

        Dim batch = _importer.Prepare({a, b}, existing, 100)

        Assert.AreEqual(1, batch.Assets.Count)
        Assert.AreEqual(b, batch.Assets(0).FilePath)
    End Sub

    <TestMethod>
    Public Sub Prepare_ExpandsFoldersRecursivelyInPathOrder()
        Photo(Path.Combine("album", "2.jpg"))
        Photo(Path.Combine("album", "sub", "1.png"))
        Photo(Path.Combine("album", "1.jpg"))
        File.WriteAllText(_dir.Combine("album", "notes.txt"), "x")

        Dim batch = _importer.Prepare({_dir.Combine("album")}, Nothing, 100)

        CollectionAssert.AreEqual({"1.jpg", "2.jpg", "1.png"}, batch.Assets.Select(Function(p) p.FileName).ToArray())
        Assert.AreEqual(0, batch.Failures.Count, "資料夾內的非圖片檔安靜略過")
    End Sub

    <TestMethod>
    Public Sub Prepare_ReportsMissingAndUnsupportedFiles()
        Dim txt = _dir.Combine("readme.txt")
        File.WriteAllText(txt, "x")

        Dim batch = _importer.Prepare({_dir.Combine("missing.jpg"), txt}, Nothing, 100)

        Assert.AreEqual(0, batch.Assets.Count)
        CollectionAssert.AreEquivalent({ImportFailureReason.NotFound, ImportFailureReason.Unsupported}, batch.Failures.Select(Function(f) f.Reason).ToArray())
    End Sub

    <TestMethod>
    Public Sub Prepare_RespectsLimitIncludingExistingPhotos()
        Dim paths = Enumerable.Range(1, 5).Select(Function(i) Photo($"{i}.jpg")).ToArray()
        Dim existing = {New PhotoAsset(Photo("old.jpg")) With {.Status = PhotoStatus.Ready}}

        Dim batch = _importer.Prepare(paths, existing, 4)

        Assert.AreEqual(3, batch.Assets.Count)
        Assert.AreEqual(2, batch.Failures.Where(Function(f) f.Reason = ImportFailureReason.LimitReached).Count())
    End Sub

#End Region

#Region "ProcessAsync"

    <TestMethod>
    Public Sub Process_FillsMetadataAndOrientedThumbnail()
        Dim result = Run(Photo("portrait.jpg", 4000, 3000, ExifOrientation.Rotate90))
        Dim asset = result.Batch.Assets(0)

        Assert.AreEqual(0, result.Failures.Count)
        Assert.AreEqual(PhotoStatus.Ready, asset.Status)
        Assert.AreEqual(3000, asset.PixelSize.Width, "轉正後寬高互換")
        Assert.AreEqual(4000, asset.PixelSize.Height)
        Assert.AreEqual(ExifOrientation.Rotate90, asset.Orientation)
        Assert.AreEqual(New Date(2024, 5, 1, 10, 0, 0), asset.DateTaken)
        Assert.AreEqual(Drawing.Color.FromArgb(&H33, &H66, &H99), asset.AverageColor)
        Assert.IsNotNull(asset.ThumbnailKey)

        Dim thumb = _importer.LoadThumbnail(asset)
        Assert.AreEqual(384, thumb.Width, "縮圖長邊 512 且已轉正")
        Assert.AreEqual(512, thumb.Height)
    End Sub

    <TestMethod>
    Public Sub Process_ReportsEachPhotoWithThumbnail()
        Dim reports As New List(Of ImportProgress)
        Dim batch = _importer.Prepare({Photo("a.jpg"), Photo("b.jpg"), Photo("bad.jpg", flags:=FakeCodec.FlagCorrupt)}, Nothing, 100)

        _importer.ProcessAsync(batch, New SyncProgress(Of ImportProgress)(AddressOf reports.Add), CancellationToken.None).GetAwaiter().GetResult()

        Assert.AreEqual(3, reports.Count)
        CollectionAssert.AreEquivalent({1, 2, 3}, reports.Select(Function(r) r.Completed).ToArray())
        Assert.IsTrue(reports.All(Function(r) r.Total = 3))
        Assert.AreEqual(2, reports.Where(Function(r) r.Thumbnail IsNot Nothing AndAlso r.Failure Is Nothing).Count())
        Assert.AreEqual(1, reports.Where(Function(r) r.Thumbnail Is Nothing AndAlso r.Failure IsNot Nothing).Count())
    End Sub

    <TestMethod>
    Public Sub Process_ClassifiesFailures()
        Dim notImage = _dir.Combine("renamed.jpg")
        File.WriteAllText(notImage, "這其實是文字檔")
        Dim empty = _dir.Combine("empty.jpg")
        File.WriteAllBytes(empty, Array.Empty(Of Byte)())
        Dim deleted = Photo("deleted.jpg")

        Dim batch = _importer.Prepare({
            notImage, empty, deleted,
            Photo("corrupt.jpg", flags:=FakeCodec.FlagCorrupt),
            Photo("iphone.heic", flags:=FakeCodec.FlagCodecMissing, header:=FakeCodec.HeifHeader),
            Photo("odd.jpg", flags:=FakeCodec.FlagCodecMissing),
            Photo("huge.jpg", 20000, 20000)
        }, Nothing, 100)
        File.Delete(deleted) ' Prepare 之後才被刪除
        Dim failures = _importer.ProcessAsync(batch, Nothing, CancellationToken.None).GetAwaiter().GetResult()

        Dim byName = failures.ToDictionary(Function(f) Path.GetFileName(f.FilePath), Function(f) f.Reason)
        Assert.AreEqual(ImportFailureReason.Unsupported, byName("renamed.jpg"))
        Assert.AreEqual(ImportFailureReason.Unsupported, byName("empty.jpg"))
        Assert.AreEqual(ImportFailureReason.NotFound, byName("deleted.jpg"))
        Assert.AreEqual(ImportFailureReason.Corrupt, byName("corrupt.jpg"))
        Assert.AreEqual(ImportFailureReason.HeifCodecMissing, byName("iphone.heic"))
        Assert.AreEqual(ImportFailureReason.Unsupported, byName("odd.jpg"))
        Assert.AreEqual(ImportFailureReason.TooLarge, byName("huge.jpg"))
        Assert.IsTrue(batch.Assets.All(Function(a) a.Status = PhotoStatus.Failed AndAlso a.FailureReason.HasValue))
    End Sub

    <TestMethod>
    Public Sub Process_SecondImportUsesCacheWithoutDecoding()
        Dim a = Photo("a.jpg")
        Run(a)
        Assert.AreEqual(1, _codec.DecodeCount)

        ' 新的快取實例 → 只能從磁碟快取命中
        Dim importer2 As New PhotoImporter(_codec, New ThumbnailCache(_codec, _dir.Combine("cache")))
        Dim batch = importer2.Prepare({a}, Nothing, 100)
        importer2.ProcessAsync(batch, Nothing, CancellationToken.None).GetAwaiter().GetResult()

        Assert.AreEqual(1, _codec.DecodeCount)
        Assert.AreEqual(PhotoStatus.Ready, batch.Assets(0).Status)
        Assert.AreEqual(3000, batch.Assets(0).PixelSize.Height)
    End Sub

    <TestMethod>
    Public Sub Process_ModifiedPhotoIsDecodedAgain()
        Dim a = Photo("a.jpg")
        Run(a)
        FakeCodec.WriteFile(a, 800, 600)
        File.SetLastWriteTimeUtc(a, Date.UtcNow.AddMinutes(1))

        Dim result = Run(a)

        Assert.AreEqual(2, _codec.DecodeCount)
        Assert.AreEqual(800, result.Batch.Assets(0).PixelSize.Width)
    End Sub

    <TestMethod>
    Public Sub Process_DoesNotLockFileAndWorksWhileHostHasItOpen()
        Dim a = Photo("a.jpg")
        Using host As New FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite Or FileShare.Delete)
            Dim result = Run(a)
            Assert.AreEqual(PhotoStatus.Ready, result.Batch.Assets(0).Status)
        End Using
        File.Delete(a) ' 匯入後原圖可被刪除
        Assert.IsFalse(File.Exists(a))
    End Sub

    <TestMethod>
    Public Sub Process_CancellationKeepsUnprocessedPhotosPending()
        Dim paths = Enumerable.Range(1, 20).Select(Function(i) Photo($"{i}.jpg")).ToArray()
        Dim importer As New PhotoImporter(_codec, New ThumbnailCache(_codec, Nothing), maxParallel:=1)
        Dim batch = importer.Prepare(paths, Nothing, 100)
        Using cts As New CancellationTokenSource()
            Dim progress As New SyncProgress(Of ImportProgress)(Sub(p) If p.Completed = 3 Then cts.Cancel())

            ' 依時機可能是 OperationCanceledException 或其子類別 TaskCanceledException
            Dim thrown As Exception = Nothing
            Try
                importer.ProcessAsync(batch, progress, cts.Token).GetAwaiter().GetResult()
            Catch ex As Exception
                thrown = ex
            End Try
            Assert.IsInstanceOfType(thrown, GetType(OperationCanceledException))
        End Using

        Dim ready = batch.Assets.Where(Function(a) a.Status = PhotoStatus.Ready).Count()
        Assert.IsTrue(ready >= 3 AndAlso ready < 20, $"已完成 {ready} 張")
        Assert.AreEqual(20 - ready, batch.Assets.Where(Function(a) a.Status = PhotoStatus.Pending).Count())
    End Sub

    <TestMethod>
    Public Sub Constructor_ChoosesParallelismByProcessBitness()
        Dim importer As New PhotoImporter(_codec, New ThumbnailCache(_codec, Nothing))
        Dim expectedMax = If(Environment.Is64BitProcess, 4, 2)
        Assert.IsTrue(importer.MaxParallel >= 1 AndAlso importer.MaxParallel <= expectedMax)
    End Sub

#End Region

    ''' <summary>同步回報（Progress(Of T) 會非同步 Post，測試不好斷言）。</summary>
    Private NotInheritable Class SyncProgress(Of T)
        Implements IProgress(Of T)

        Private ReadOnly _handler As Action(Of T)
        Private ReadOnly _lock As New Object()

        Public Sub New(handler As Action(Of T))
            _handler = handler
        End Sub

        Public Sub Report(value As T) Implements IProgress(Of T).Report
            SyncLock _lock
                _handler(value)
            End SyncLock
        End Sub
    End Class

End Class

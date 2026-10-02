Imports System.IO
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class LruCacheTests

    <TestMethod>
    Public Sub Add_EvictsLeastRecentlyUsedWhenOverBudget()
        Dim cache As New LruCache(Of String, Integer)(10, Function(v) v)
        cache.Add("a", 4)
        cache.Add("b", 4)
        Dim value As Integer
        Assert.IsTrue(cache.TryGet("a", value)) ' a 變成最近使用
        cache.Add("c", 4)                       ' 超過 10，淘汰 b

        Assert.IsTrue(cache.TryGet("a", value))
        Assert.IsFalse(cache.TryGet("b", value))
        Assert.IsTrue(cache.TryGet("c", value))
        Assert.AreEqual(8L, cache.CurrentSize)
    End Sub

    <TestMethod>
    Public Sub Add_ReplacesExistingKey()
        Dim cache As New LruCache(Of String, Integer)(10, Function(v) v)
        cache.Add("a", 3)
        cache.Add("a", 5)
        Assert.AreEqual(1, cache.Count)
        Assert.AreEqual(5L, cache.CurrentSize)
    End Sub

    <TestMethod>
    Public Sub Add_IgnoresItemLargerThanBudget()
        Dim cache As New LruCache(Of String, Integer)(10, Function(v) v)
        cache.Add("a", 3)
        cache.Add("big", 11)
        Dim value As Integer
        Assert.IsFalse(cache.TryGet("big", value))
        Assert.IsTrue(cache.TryGet("a", value))
    End Sub

End Class

<TestClass>
Public Class ThumbnailCacheTests

    Private Shared Function Entry() As CachedThumbnail
        Return New CachedThumbnail(New ImageInfo(4000, 3000, ExifOrientation.Rotate90, New Date(2023, 7, 8, 9, 10, 11)),
                                   FakeCodec.Solid(4, 3, &HFF102030))
    End Function

    <TestMethod>
    Public Sub ComputeKey_ChangesWhenFileChanges()
        Dim t = New Date(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        Dim k = ThumbnailCache.ComputeKey("C:\Photos\a.jpg", 100, t)

        Assert.AreEqual(k, ThumbnailCache.ComputeKey("c:\photos\A.JPG", 100, t), "路徑不分大小寫")
        Assert.AreNotEqual(k, ThumbnailCache.ComputeKey("C:\Photos\a.jpg", 101, t), "大小改變")
        Assert.AreNotEqual(k, ThumbnailCache.ComputeKey("C:\Photos\a.jpg", 100, t.AddSeconds(1)), "修改時間改變")
        Assert.AreEqual(32, k.Length)
    End Sub

    <TestMethod>
    Public Sub Disk_RoundTripsInfoAndPixels()
        Using dir As New TempFolder()
            Dim codec As New FakeCodec()
            Dim key = ThumbnailCache.ComputeKey("x", 1, Date.UtcNow)
            Dim original = Entry()
            Call New ThumbnailCache(codec, dir.Path).Put(key, original)

            ' 新的實例 = 記憶體是空的，只能從磁碟讀
            Dim loaded = New ThumbnailCache(codec, dir.Path).TryGet(key)

            Assert.IsNotNull(loaded)
            Assert.AreEqual(4000, loaded.Info.Width)
            Assert.AreEqual(ExifOrientation.Rotate90, loaded.Info.Orientation)
            Assert.AreEqual(original.Info.DateTaken, loaded.Info.DateTaken)
            CollectionAssert.AreEqual(original.Image.Pixels, loaded.Image.Pixels)
            Assert.AreEqual(original.AverageColor, loaded.AverageColor)
        End Using
    End Sub

    <TestMethod>
    Public Sub Disk_CorruptFileIsDeletedAndTreatedAsMiss()
        Using dir As New TempFolder()
            Dim codec As New FakeCodec()
            Dim cache As New ThumbnailCache(codec, dir.Path)
            Dim key = ThumbnailCache.ComputeKey("x", 1, Date.UtcNow)
            cache.Put(key, Entry())
            Dim file = cache.GetFilePath(key)
            IO.File.WriteAllBytes(file, {1, 2, 3})

            Assert.IsNull(New ThumbnailCache(codec, dir.Path).TryGet(key))
            Assert.IsFalse(IO.File.Exists(file))
        End Using
    End Sub

    <TestMethod>
    Public Sub MemoryOnly_WhenDirectoryIsNothing()
        Dim cache As New ThumbnailCache(New FakeCodec(), Nothing)
        cache.Put("k", Entry())
        Assert.IsNotNull(cache.TryGet("k"))
        cache.ClearMemory()
        Assert.IsNull(cache.TryGet("k"))
    End Sub

    <TestMethod>
    Public Sub TrimDisk_DeletesOldestFirst()
        Using dir As New TempFolder()
            Dim cache As New ThumbnailCache(New FakeCodec(), dir.Path)
            Dim keys = Enumerable.Range(0, 3).Select(Function(i) ThumbnailCache.ComputeKey($"p{i}", i, Date.UtcNow)).ToList()
            For i = 0 To 2
                cache.Put(keys(i), Entry())
                IO.File.SetLastWriteTimeUtc(cache.GetFilePath(keys(i)), Date.UtcNow.AddHours(i - 10))
            Next
            Dim oneFile = New FileInfo(cache.GetFilePath(keys(0))).Length

            Dim deleted = cache.TrimDisk(oneFile * 2)

            Assert.AreEqual(1, deleted)
            Assert.IsFalse(IO.File.Exists(cache.GetFilePath(keys(0))), "最舊的被刪")
            Assert.IsTrue(IO.File.Exists(cache.GetFilePath(keys(2))))
        End Using
    End Sub

End Class

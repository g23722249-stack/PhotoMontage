Imports System.Drawing
Imports System.IO
Imports System.IO.Compression
Imports System.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class LabColorTests

    <TestMethod>
    Public Sub FromRgb_MatchesReferenceValues()
        Dim white = LabColor.FromRgb(255, 255, 255)
        Assert.AreEqual(100.0F, white.L, 0.05F)
        Assert.AreEqual(0F, white.A, 0.05F)
        Assert.AreEqual(0F, white.B, 0.05F)

        Assert.AreEqual(0F, LabColor.FromRgb(0, 0, 0).L, 0.05F)

        Dim red = LabColor.FromRgb(255, 0, 0)
        Assert.AreEqual(53.24F, red.L, 0.1F)
        Assert.AreEqual(80.09F, red.A, 0.2F)
        Assert.AreEqual(67.2F, red.B, 0.2F)
    End Sub

    <TestMethod>
    Public Sub Distance_RedIsCloserToOrangeThanToBlue()
        Dim red = LabColor.FromRgb(220, 30, 30)
        Assert.IsTrue(LabColor.DistanceSquared(red, LabColor.FromRgb(240, 120, 20)) < LabColor.DistanceSquared(red, LabColor.FromRgb(30, 30, 220)))
    End Sub

    <TestMethod>
    Public Sub FromRgb_HandlesFractionalAverages()
        Dim a = LabColor.FromRgb(100, 100, 100)
        Dim b = LabColor.FromRgb(100.5, 100.5, 100.5)
        Dim c = LabColor.FromRgb(101, 101, 101)
        Assert.IsTrue(a.L < b.L AndAlso b.L < c.L)
    End Sub

End Class

<TestClass>
Public Class ImageSamplerTests

    ''' <summary>8×4 影像：左半紅、右半藍。</summary>
    Private Shared Function SplitImage() As DecodedImage
        Dim img = FakeCodec.Solid(8, 4, &HFFFF0000)
        For y = 0 To 3
            For x = 4 To 7
                Dim i = (y * 8 + x) * 4
                img.Pixels(i) = 255 : img.Pixels(i + 1) = 0 : img.Pixels(i + 2) = 0
            Next
        Next
        Return img
    End Function

    <TestMethod>
    Public Sub CoverRegion_CentersCrop()
        Assert.AreEqual(New RectangleF(500, 0, 3000, 3000), ImageSampler.CoverRegion(4000, 3000, 1))
        Assert.AreEqual(New RectangleF(0, 375, 4000, 2250), ImageSampler.CoverRegion(4000, 3000, 16 / 9.0))
    End Sub

    <TestMethod>
    Public Sub AverageRgb_AveragesRegion()
        Dim avg = ImageSampler.AverageRgb(SplitImage(), New RectangleF(2, 0, 4, 4))
        Assert.AreEqual(127.5, avg.R, 0.01)
        Assert.AreEqual(127.5, avg.B, 0.01)
    End Sub

    <TestMethod>
    Public Sub Feature_QuadrantsCaptureLeftRightDifference()
        Dim f = ImageSampler.Feature(SplitImage(), New RectangleF(0, 0, 8, 4))
        Dim red = LabColor.FromRgb(255, 0, 0), blue = LabColor.FromRgb(0, 0, 255)
        Assert.IsTrue(LabColor.DistanceSquared(f.Quadrants(0), red) < 1, "左上是紅")
        Assert.IsTrue(LabColor.DistanceSquared(f.Quadrants(1), blue) < 1, "右上是藍")
        Assert.IsTrue(LabColor.DistanceSquared(f.Quadrants(2), red) < 1, "左下是紅")
        Assert.AreEqual(Color.FromArgb(128, 0, 128), f.AverageColor)
    End Sub

    <TestMethod>
    Public Sub AnalyzeGrid_ReturnsCellsRowMajor()
        Dim cells = ImageSampler.AnalyzeGrid(SplitImage(), 2, 2, 2.0)
        Assert.AreEqual(4, cells.Length)
        Assert.AreEqual(Color.FromArgb(255, 0, 0), cells(0).AverageColor)
        Assert.AreEqual(Color.FromArgb(0, 0, 255), cells(1).AverageColor)
        Assert.AreEqual(Color.FromArgb(0, 0, 255), cells(3).AverageColor)
    End Sub

End Class

<TestClass>
Public Class LabKdTreeTests

    <TestMethod>
    Public Sub Nearest_MatchesBruteForce()
        Dim rng As New Random(7)
        Dim points = Enumerable.Range(0, 500).Select(Function(i) New LabColor(CSng(rng.NextDouble() * 100), CSng(rng.NextDouble() * 200 - 100), CSng(rng.NextDouble() * 200 - 100))).ToList()
        Dim tree As New LabKdTree(points)

        For q = 0 To 50
            Dim target As New LabColor(CSng(rng.NextDouble() * 100), CSng(rng.NextDouble() * 200 - 100), CSng(rng.NextDouble() * 200 - 100))
            Dim expected = Enumerable.Range(0, points.Count).OrderBy(Function(i) LabColor.DistanceSquared(points(i), target)).Take(10).ToList()
            Dim actual = tree.Nearest(target, 10)
            CollectionAssert.AreEqual(expected.Select(Function(i) LabColor.DistanceSquared(points(i), target)).ToArray(),
                                      actual.Select(Function(i) LabColor.DistanceSquared(points(i), target)).ToArray())
        Next
    End Sub

    <TestMethod>
    Public Sub Nearest_HandlesSmallSets()
        Dim tree As New LabKdTree({New LabColor(50, 0, 0)})
        CollectionAssert.AreEqual({0}, tree.Nearest(New LabColor(0, 0, 0), 5).ToArray())
        Assert.AreEqual(0, New LabKdTree(New List(Of LabColor)).Nearest(New LabColor(0, 0, 0), 3).Count)
    End Sub

End Class

<TestClass>
Public Class MosaicMatcherTests

    Private Shared ReadOnly Palette As Color() = {Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Black, Color.White}

    Private Shared Function Features(ParamArray colors As Color()) As List(Of MosaicFeature)
        Return colors.Select(Function(c) MosaicFeature.Solid(c)).ToList()
    End Function

    <TestMethod>
    Public Sub Match_PicksClosestColor()
        Dim tiles = Features(Palette)
        Dim cells = Features(Color.FromArgb(250, 10, 10), Color.FromArgb(10, 10, 240), Color.FromArgb(5, 5, 5))
        Dim result = MosaicMatcher.Match(cells, 3, tiles, New MosaicMatchOptions With {.AvoidAdjacentDuplicates = False})
        CollectionAssert.AreEqual({0, 2, 4}, result)
    End Sub

    <TestMethod>
    Public Sub Match_UsesQuadrantDetail()
        ' 兩張素材平均色相同（左紅右藍 vs 左藍右紅），格子是左紅右藍
        Dim red = LabColor.FromColor(Color.Red), blue = LabColor.FromColor(Color.Blue)
        Dim avg = LabColor.FromRgb(128, 0, 128)
        Dim leftRed As New MosaicFeature(avg, {red, blue, red, blue}, Color.Purple)
        Dim leftBlue As New MosaicFeature(avg, {blue, red, blue, red}, Color.Purple)
        Dim result = MosaicMatcher.Match({leftRed}, 1, {leftBlue, leftRed}, New MosaicMatchOptions())
        Assert.AreEqual(1, result(0))
    End Sub

    <TestMethod>
    Public Sub Match_AvoidsAdjacentDuplicates()
        ' 每格最多 8 個鄰格，素材 ≥ 9 張時一定能滿足
        Dim tiles = Enumerable.Range(0, 9).Select(Function(i) MosaicFeature.Solid(Color.FromArgb(255 - i * 5, i * 3, i * 3))).ToList()
        Dim cells = Enumerable.Repeat(MosaicFeature.Solid(Color.Red), 100).ToList()
        Dim result = MosaicMatcher.Match(cells, 10, tiles, New MosaicMatchOptions With {.AvoidAdjacentDuplicates = True})

        For i = 0 To 99
            Assert.IsFalse(MosaicMatcher.IsUsedByNeighbor(result(i), i, 10, 100, result), $"第 {i} 格與鄰格重複")
        Next
        Assert.IsTrue(result.Count(Function(t) t = 0) > result.Count(Function(t) t = 8), "仍偏好最相近的素材")
    End Sub

    <TestMethod>
    Public Sub Match_RespectsMaxRepeatAndRelaxesWhenImpossible()
        Dim tiles = Features(Color.Red, Color.Blue, Color.Lime)
        Dim cells = Enumerable.Repeat(MosaicFeature.Solid(Color.Red), 9).ToList()

        Dim limited = MosaicMatcher.Match(cells, 3, tiles, New MosaicMatchOptions With {.MaxRepeat = 3, .AvoidAdjacentDuplicates = False})
        Assert.IsTrue(Enumerable.Range(0, 3).All(Function(t) limited.Count(Function(x) x = t) <= 3))

        ' 9 格、3 張素材、每張最多 1 次 → 自動放寬為每張 3 次
        Dim relaxed = MosaicMatcher.Match(cells, 3, tiles, New MosaicMatchOptions With {.MaxRepeat = 1, .AvoidAdjacentDuplicates = False})
        Assert.IsTrue(relaxed.All(Function(x) x >= 0))
        Assert.IsTrue(Enumerable.Range(0, 3).All(Function(t) relaxed.Count(Function(x) x = t) = 3))
    End Sub

    <TestMethod>
    Public Sub Match_IsDeterministicForSameSeed()
        Dim rng As New Random(3)
        Dim tiles = Enumerable.Range(0, 40).Select(Function(i) MosaicFeature.Solid(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)))).ToList()
        Dim cells = Enumerable.Range(0, 100).Select(Function(i) MosaicFeature.Solid(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)))).ToList()
        Dim options As New MosaicMatchOptions With {.MaxRepeat = 4}
        CollectionAssert.AreEqual(MosaicMatcher.Match(cells, 10, tiles, options), MosaicMatcher.Match(cells, 10, tiles, options))
    End Sub

    <TestMethod>
    Public Sub Match_SingleTileStillFillsEverything()
        Dim result = MosaicMatcher.Match(Features(Color.Red, Color.Blue, Color.Lime, Color.Black), 2, Features(Color.Gray), New MosaicMatchOptions With {.MaxRepeat = 1})
        CollectionAssert.AreEqual({0, 0, 0, 0}, result)
    End Sub

    <TestMethod>
    Public Sub NextAlternative_CyclesThroughSimilarTiles()
        Dim tiles = Features(Color.Red, Color.FromArgb(200, 0, 0), Color.Blue)
        Dim cells = Features(Color.Red)
        Dim assignment = {0}
        Dim nextTile = MosaicMatcher.NextAlternative(0, 0, cells, 1, tiles, assignment)
        Assert.AreEqual(1, nextTile, "次相近的暗紅")
        Assert.AreEqual(2, MosaicMatcher.NextAlternative(0, 1, cells, 1, tiles, assignment))
        Assert.AreEqual(0, MosaicMatcher.NextAlternative(0, 2, cells, 1, tiles, assignment), "到尾端從頭開始")
    End Sub

End Class

<TestClass>
Public Class MosaicModelTests

    <TestMethod>
    Public Sub RowsFor_KeepsTilesSquare()
        Assert.AreEqual(60, MosaicSettings.RowsFor(60, 1))
        Assert.AreEqual(75, MosaicSettings.RowsFor(60, 0.8))
        Assert.AreEqual(34, MosaicSettings.RowsFor(60, 16 / 9.0))
    End Sub

    <TestMethod>
    Public Sub DesignState_RoundTripsMosaicCompactly()
        Dim p As New MontageProject With {.Mode = MontageMode.Mosaic}
        p.Mosaic.TargetPath = "C:\target.jpg"
        p.Mosaic.Columns = 100
        p.Mosaic.Rows = 100
        p.Mosaic.Tint = 0.2F
        p.Mosaic.MaxRepeat = 5
        p.Mosaic.AvoidAdjacentDuplicates = False
        p.Mosaic.Tiles = Enumerable.Range(0, 10000).Select(Function(i) Guid.Empty.ToString("N").Substring(0, 30) & (i Mod 50).ToString("00")).ToList()
        p.Mosaic.Tiles(5) = Nothing

        Dim json = DesignState.Capture(p).ToJson()
        Dim restored As New MontageProject()
        DesignState.FromJson(json).ApplyTo(restored)

        Assert.AreEqual(MontageMode.Mosaic, restored.Mode)
        Assert.AreEqual("C:\target.jpg", restored.Mosaic.TargetPath)
        Assert.AreEqual(0.2F, restored.Mosaic.Tint)
        Assert.IsFalse(restored.Mosaic.AvoidAdjacentDuplicates)
        CollectionAssert.AreEqual(p.Mosaic.Tiles, restored.Mosaic.Tiles)
        Assert.IsTrue(json.Length < 100_000, $"快照 {json.Length} 字元，應以索引壓縮")
    End Sub

    <TestMethod>
    Public Sub LruCache_CallsEvictOnEvictionReplaceAndClear()
        Dim evicted As New List(Of Integer)
        Dim cache As New LruCache(Of String, Integer)(10, Function(v) v, Sub(v) evicted.Add(v))
        cache.Add("a", 6)
        cache.Add("b", 6)          ' 淘汰 a
        cache.Add("b", 5)          ' 取代 b(6)
        Assert.IsFalse(cache.Add("big", 20), "放不進去回傳 False")
        cache.Clear()
        CollectionAssert.AreEqual({6, 6, 5}, evicted)
    End Sub

    <TestMethod>
    Public Sub Renderer_CellRectsTileWithoutGaps()
        Dim settings As New MosaicSettings With {.Columns = 7, .Rows = 3}
        settings.Tiles = Enumerable.Repeat("x", 21).ToList()
        Dim bounds As New RectangleF(10, 20, 100, 50)
        Dim total = 0
        For i = 0 To 20
            Dim r = MosaicRenderer.GetCellRect(settings, bounds, i)
            total += r.Width * r.Height
            If i Mod 7 < 6 Then Assert.AreEqual(r.Right, MosaicRenderer.GetCellRect(settings, bounds, i + 1).Left, "左右相接")
        Next
        Assert.AreEqual(100 * 50, total)
        Assert.AreEqual(0, MosaicRenderer.HitTest(settings, bounds, New PointF(10, 20)))
        Assert.AreEqual(20, MosaicRenderer.HitTest(settings, bounds, New PointF(109.9F, 69.9F)))
        Assert.AreEqual(-1, MosaicRenderer.HitTest(settings, bounds, New PointF(5, 5)))
    End Sub

    <TestMethod>
    Public Sub ValidateOutputSize_MosaicPngAllowsHugeSizes()
        Dim huge As New Size(15000, 15000)
        Assert.IsNull(ExportPlanner.ValidateOutputSize(huge, ExportFormat.Png, MontageMode.Mosaic))
        Assert.IsNotNull(ExportPlanner.ValidateOutputSize(huge, ExportFormat.Jpeg, MontageMode.Mosaic))
        Assert.IsNotNull(ExportPlanner.ValidateOutputSize(huge, ExportFormat.Png, MontageMode.Collage))
        Assert.IsNotNull(ExportPlanner.ValidateOutputSize(New Size(16001, 100), ExportFormat.Png, MontageMode.Mosaic))
    End Sub

End Class

<TestClass>
Public Class MosaicGeneratorTests

    <TestMethod>
    Public Sub Generate_MatchesTilesToTargetHalves()
        Using dir As New TempFolder()
            Dim codec As New FakeCodec()
            Dim importer As New PhotoImporter(codec, New ThumbnailCache(codec, Nothing))
            Dim target = dir.Combine("target.jpg")
            FakeCodec.WriteFile(target, 400, 200, argb:=&HFF0000FF, flags:=FakeCodec.FlagSplitWhite) ' 左藍右白
            Dim paths = {("blue.jpg", &HFF0000FF), ("white.jpg", &HFFFFFFFF), ("red.jpg", &HFFFF0000), ("navy.jpg", &HFF000080)}
            For Each p In paths
                FakeCodec.WriteFile(dir.Combine(p.Item1), 300, 300, argb:=p.Item2)
            Next

            Dim project As New MontageProject With {.Mode = MontageMode.Mosaic, .CanvasSize = New Size(400, 200)}
            Dim batch = importer.Prepare(paths.Select(Function(p) dir.Combine(p.Item1)), Nothing, 100)
            importer.ProcessAsync(batch, Nothing, CancellationToken.None).GetAwaiter().GetResult()
            project.Photos.AddRange(batch.Assets)
            project.Mosaic.TargetPath = target
            project.Mosaic.Columns = 4
            project.Mosaic.Rows = 2
            project.Mosaic.AvoidAdjacentDuplicates = False

            Dim generator As New MosaicGenerator(importer)
            Dim result = generator.Generate(project, Nothing, CancellationToken.None)

            Dim nameOf_ = Function(id As String) project.FindPhoto(id).FileName
            CollectionAssert.AreEqual({"blue.jpg", "blue.jpg", "white.jpg", "white.jpg", "blue.jpg", "blue.jpg", "white.jpg", "white.jpg"},
                                      result.Tiles.Select(nameOf_).ToArray())
            Assert.AreEqual(4, result.Analysis.TileAssets.Count)
            Assert.AreEqual(8, result.Analysis.CellFeatures.Count)
        End Using
    End Sub

    <TestMethod>
    Public Sub Analyze_RequiresTargetAndEnoughTiles()
        Dim codec As New FakeCodec()
        Dim generator As New MosaicGenerator(New PhotoImporter(codec, New ThumbnailCache(codec, Nothing)))
        Dim project As New MontageProject With {.Mode = MontageMode.Mosaic}
        Assert.ThrowsException(Of InvalidOperationException)(Function() generator.Analyze(project, Nothing, CancellationToken.None))
        project.Mosaic.TargetPath = "C:\x.jpg"
        Assert.ThrowsException(Of InvalidOperationException)(Function() generator.Analyze(project, Nothing, CancellationToken.None))
    End Sub

End Class

<TestClass>
Public Class PngStreamWriterTests

    <TestMethod>
    Public Sub WritesValidPngWithExactPixels()
        Dim w = 37, h = 23
        Dim rng As New Random(1)
        Dim pixels = New Byte(w * h * 3 - 1) {}
        rng.NextBytes(pixels)

        Dim bytes As Byte()
        Using ms As New MemoryStream()
            Using writer As New PngStreamWriter(ms, w, h, dpi:=300)
                For y = 0 To h - 1
                    writer.WriteRowRgb(pixels, y * w * 3)
                Next
                writer.Finish()
            End Using
            bytes = ms.ToArray()
        End Using

        Dim png = TestPngReader.Read(bytes)
        Assert.AreEqual(w, png.Width)
        Assert.AreEqual(h, png.Height)
        Assert.AreEqual(300, png.Dpi)
        CollectionAssert.AreEqual(pixels, png.Rgb)
    End Sub

    <TestMethod>
    Public Sub WriteRowBgr_SwapsChannels()
        Using ms As New MemoryStream()
            Using writer As New PngStreamWriter(ms, 2, 1)
                writer.WriteRowBgr({1, 2, 3, 4, 5, 6}, 0)
                writer.Finish()
            End Using
            CollectionAssert.AreEqual(New Byte() {3, 2, 1, 6, 5, 4}, TestPngReader.Read(ms.ToArray()).Rgb)
        End Using
    End Sub

    <TestMethod>
    Public Sub LargeImageSpansManyIdatChunks()
        Dim w = 800, h = 600
        Dim rng As New Random(2)
        Dim row = New Byte(w * 3 - 1) {}
        Using ms As New MemoryStream()
            Using writer As New PngStreamWriter(ms, w, h)
                For y = 0 To h - 1
                    rng.NextBytes(row)
                    writer.WriteRowRgb(row, 0)
                Next
                writer.Finish()
            End Using
            Dim png = TestPngReader.Read(ms.ToArray())
            Assert.IsTrue(png.IdatChunks > 5, $"{png.IdatChunks} 個 IDAT")
            Assert.AreEqual(w * h * 3, png.Rgb.Length)
        End Using
    End Sub

    <TestMethod>
    Public Sub Finish_RequiresAllRows()
        Using ms As New MemoryStream(), writer As New PngStreamWriter(ms, 2, 2)
            writer.WriteRowRgb(New Byte(5) {}, 0)
            Assert.ThrowsException(Of InvalidOperationException)(Sub() writer.Finish())
            writer.WriteRowRgb(New Byte(5) {}, 0)
            Assert.ThrowsException(Of InvalidOperationException)(Sub() writer.WriteRowRgb(New Byte(5) {}, 0))
        End Using
    End Sub

End Class

''' <summary>測試用的極簡 PNG 解碼器：驗證區塊 CRC、解壓 IDAT、還原 None/Sub/Up 濾波。</summary>
Friend Module TestPngReader

    Public Class Result
        Public Width, Height, Dpi, IdatChunks As Integer
        Public Rgb As Byte()
    End Class

    Public Function Read(data As Byte()) As Result
        CollectionAssert.AreEqual({137, 80, 78, 71, 13, 10, 26, 10}.Select(Function(b) CByte(b)).ToArray(), data.Take(8).ToArray(), "PNG 簽章")
        Dim result As New Result()
        Dim idat As New MemoryStream()
        Dim pos = 8
        Dim sawEnd = False
        While pos < data.Length
            Dim length = ReadInt(data, pos)
            Dim type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4)
            Dim crcExpected = CUInt(ReadInt(data, pos + 8 + length) And &HFFFFFFFFL)
            Assert.AreEqual(Crc(data, pos + 4, length + 4), crcExpected, $"{type} 的 CRC")
            Select Case type
                Case "IHDR"
                    result.Width = ReadInt(data, pos + 8)
                    result.Height = ReadInt(data, pos + 12)
                    Assert.AreEqual(8, CInt(data(pos + 16)))
                    Assert.AreEqual(2, CInt(data(pos + 17)))
                Case "pHYs"
                    result.Dpi = CInt(Math.Round(ReadInt(data, pos + 8) * 0.0254))
                Case "IDAT"
                    idat.Write(data, pos + 8, length)
                    result.IdatChunks += 1
                Case "IEND"
                    sawEnd = True
            End Select
            pos += 12 + length
        End While
        Assert.IsTrue(sawEnd, "缺少 IEND")

        idat.Position = 0
        Dim raw As New MemoryStream()
        Using z As New ZLibStream(idat, CompressionMode.Decompress)
            z.CopyTo(raw)
        End Using
        Dim bytes = raw.ToArray()
        Dim stride = result.Width * 3
        Assert.AreEqual((stride + 1) * result.Height, bytes.Length)
        result.Rgb = New Byte(stride * result.Height - 1) {}
        For y = 0 To result.Height - 1
            Dim filter = bytes(y * (stride + 1))
            For x = 0 To stride - 1
                Dim v As Integer = bytes(y * (stride + 1) + 1 + x)
                Dim left = If(x >= 3, CInt(result.Rgb(y * stride + x - 3)), 0)
                Dim up = If(y > 0, CInt(result.Rgb((y - 1) * stride + x)), 0)
                Select Case filter
                    Case 0
                    Case 1 : v += left
                    Case 2 : v += up
                    Case Else : Assert.Fail($"未支援的濾波 {filter}")
                End Select
                result.Rgb(y * stride + x) = CByte(v And &HFF)
            Next
        Next
        Return result
    End Function

    Private Function ReadInt(d As Byte(), i As Integer) As Integer
        Return (CInt(d(i)) << 24) Or (CInt(d(i + 1)) << 16) Or (CInt(d(i + 2)) << 8) Or d(i + 3)
    End Function

    Private Function Crc(d As Byte(), offset As Integer, count As Integer) As UInteger
        Dim c = &HFFFFFFFFUI
        For i = offset To offset + count - 1
            c = c Xor d(i)
            For k = 0 To 7
                c = If((c And 1UI) <> 0, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
        Next
        Return c Xor &HFFFFFFFFUI
    End Function

End Module

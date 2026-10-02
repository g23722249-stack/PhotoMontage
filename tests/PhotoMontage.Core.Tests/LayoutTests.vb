Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class CellGeometryTests

    Private Const Tolerance As Single = 0.01F

    <TestMethod>
    Public Sub GetCellRect_AllVisibleGapsAreEqual()
        Dim bounds As New RectangleF(10, 20, 1000, 500)
        Dim settings As New CollageSettings With {.Gap = 0.02F, .Cells = CollageTemplates.Grid(2, 2).CreateCells()}
        Dim r = CellGeometry.GetCellRects(settings, bounds)
        Dim g = 0.02F * 500 ' 短邊的 2%

        Assert.AreEqual(bounds.Left + g, r(0).Left, Tolerance, "左外緣")
        Assert.AreEqual(bounds.Top + g, r(0).Top, Tolerance, "上外緣")
        Assert.AreEqual(g, r(1).Left - r(0).Right, Tolerance, "左右兩格之間")
        Assert.AreEqual(g, r(2).Top - r(0).Bottom, Tolerance, "上下兩格之間")
        Assert.AreEqual(bounds.Right - g, r(3).Right, Tolerance, "右外緣")
        Assert.AreEqual(bounds.Bottom - g, r(3).Bottom, Tolerance, "下外緣")
        Assert.AreEqual(r(0).Width, r(1).Width, Tolerance, "等分格寬度相同")
    End Sub

    <TestMethod>
    Public Sub GetCellRect_NoGapCoversBounds()
        Dim bounds As New RectangleF(0, 0, 300, 200)
        Dim rect = CellGeometry.GetCellRect(New RectangleF(0, 0, 1, 1), bounds, 0)
        Assert.AreEqual(bounds, rect)
    End Sub

    <TestMethod>
    Public Sub GetCellRect_HugeGapNeverProducesNegativeSize()
        Dim rect = CellGeometry.GetCellRect(New RectangleF(0.4F, 0.4F, 0.2F, 0.2F), New RectangleF(0, 0, 100, 100), 0.5F)
        Assert.IsTrue(rect.Width >= 0 AndAlso rect.Height >= 0)
    End Sub

    <TestMethod>
    Public Sub GetCellAspect_UsesCanvasSize()
        Assert.AreEqual(2.0, CellGeometry.GetCellAspect(New RectangleF(0, 0, 1, 0.5F), New SizeF(100, 100)), 0.0001)
        Assert.AreEqual(1.0, CellGeometry.GetCellAspect(New RectangleF(0, 0, 0.5F, 1), New SizeF(200, 100)), 0.0001)
    End Sub

End Class

<TestClass>
Public Class CropMathTests

    Private Const Tolerance As Single = 0.01F
    Private Shared ReadOnly Landscape As New SizeF(4000, 3000)
    Private Shared ReadOnly SquareCell As New SizeF(300, 300)

    <TestMethod>
    Public Sub GetSourceRect_DefaultIsCenteredCover()
        Dim src = CropMath.GetSourceRect(Landscape, SquareCell, New CropInfo())
        Assert.AreEqual(3000, src.Width, Tolerance, "取滿短邊")
        Assert.AreEqual(3000, src.Height, Tolerance)
        Assert.AreEqual(500, src.X, Tolerance, "水平置中")
        Assert.AreEqual(0, src.Y, Tolerance)
    End Sub

    <TestMethod>
    Public Sub GetSourceRect_OffsetMovesToEdges()
        Dim left = CropMath.GetSourceRect(Landscape, SquareCell, New CropInfo With {.OffsetX = -1})
        Dim right = CropMath.GetSourceRect(Landscape, SquareCell, New CropInfo With {.OffsetX = 1})
        Assert.AreEqual(0, left.X, Tolerance)
        Assert.AreEqual(4000, right.Right, Tolerance)
    End Sub

    <TestMethod>
    Public Sub GetSourceRect_ZoomShrinksSourceAndStaysInside()
        Dim crop As New CropInfo With {.Scale = 2, .OffsetX = 1, .OffsetY = 1}
        Dim src = CropMath.GetSourceRect(Landscape, SquareCell, crop)
        Assert.AreEqual(1500, src.Width, Tolerance)
        Assert.AreEqual(4000, src.Right, Tolerance)
        Assert.AreEqual(3000, src.Bottom, Tolerance)
    End Sub

    <TestMethod>
    Public Sub GetSourceRect_ClampsOutOfRangeValues()
        Dim src = CropMath.GetSourceRect(Landscape, SquareCell, New CropInfo With {.OffsetX = 9, .Scale = 0.1F})
        Assert.AreEqual(3000, src.Width, Tolerance, "倍率最小為 1")
        Assert.AreEqual(4000, src.Right, Tolerance, "偏移最大為 1")
    End Sub

    <TestMethod>
    Public Sub Pan_ImageFollowsMouse()
        ' 往右拖 → 照片往右移 → 看到更左邊 → OffsetX 變小
        Dim crop = CropMath.Pan(New CropInfo(), Landscape, SquareCell, 30, 0)
        Assert.IsTrue(crop.OffsetX < 0)
        Assert.AreEqual(0F, crop.OffsetY, "高度剛好蓋滿時垂直不能移動")

        ' 拖曳的像素量與畫面移動一致：倍率 0.1 → 30px = 照片 300px；可移動範圍 500px
        Assert.AreEqual(-0.6F, crop.OffsetX, Tolerance)
    End Sub

    <TestMethod>
    Public Sub Pan_ClampsAtEdge()
        Dim crop = CropMath.Pan(New CropInfo(), Landscape, SquareCell, 10000, 0)
        Assert.AreEqual(-1.0F, crop.OffsetX)
    End Sub

    <TestMethod>
    Public Sub Zoom_ClampsBetweenOneAndFive()
        Assert.AreEqual(CropMath.MaxScale, CropMath.Zoom(New CropInfo With {.Scale = 4}, 2).Scale)
        Assert.AreEqual(CropMath.MinScale, CropMath.Zoom(New CropInfo With {.Scale = 1.2F}, 0.5F).Scale)
    End Sub

    <TestMethod>
    Public Sub PanAndZoom_DoNotMutateInput()
        Dim original As New CropInfo With {.OffsetX = 0.2F, .Scale = 1.5F}
        CropMath.Pan(original, Landscape, SquareCell, 50, 50)
        CropMath.Zoom(original, 2)
        Assert.AreEqual(0.2F, original.OffsetX)
        Assert.AreEqual(1.5F, original.Scale)
    End Sub

End Class

<TestClass>
Public Class PhotoAssignmentTests

    Private Shared Function Ready(id As String, w As Integer, h As Integer) As PhotoAsset
        Return New PhotoAsset("C:\" & id & ".jpg") With {.Id = id, .Status = PhotoStatus.Ready, .PixelSize = New Size(w, h)}
    End Function

    <TestMethod>
    Public Sub CropLoss_ZeroWhenAspectsMatch()
        Assert.AreEqual(0.0, PhotoAssignment.CropLoss(1.5, 1.5), 0.0001)
        Assert.AreEqual(0.5, PhotoAssignment.CropLoss(2, 1), 0.0001)
        Assert.AreEqual(0.5, PhotoAssignment.CropLoss(1, 2), 0.0001)
    End Sub

    <TestMethod>
    Public Sub AssignAll_PutsPortraitInTallCellAndLandscapeInSquareCells()
        ' 正方形畫布：左半直長格（1:2）＋右邊上下兩個正方格。
        ' 貪婪法會先把直圖放進正方格，最後橫圖被迫放進直長格；最佳解應是直圖進直長格。
        Dim cells = New List(Of Cell) From {
            New Cell With {.Bounds = New RectangleF(0, 0, 0.5F, 1)},
            New Cell With {.Bounds = New RectangleF(0.5F, 0, 0.5F, 0.5F)},
            New Cell With {.Bounds = New RectangleF(0.5F, 0.5F, 0.5F, 0.5F)}}
        Dim photos = {Ready("wide1", 4000, 3000), Ready("tall", 3000, 4000), Ready("wide2", 4000, 3000)}

        PhotoAssignment.AssignAll(cells, photos, New SizeF(1000, 1000))

        Assert.AreEqual("tall", cells(0).PhotoId)
        CollectionAssert.AreEqual({"wide1", "wide2"}, {cells(1).PhotoId, cells(2).PhotoId}, "同樣適合時維持照片順序")
    End Sub

    <TestMethod>
    Public Sub SolveAssignment_FindsMinimumTotalCost()
        Dim cost As Double(,) = {{4, 1, 3}, {2, 0, 5}, {3, 2, 2}}
        CollectionAssert.AreEqual({1, 0, 2}, PhotoAssignment.SolveAssignment(cost)) ' 1 + 2 + 2 = 5

        Dim rect As Double(,) = {{9, 1, 9, 9}, {9, 9, 9, 0}}
        CollectionAssert.AreEqual({1, 3}, PhotoAssignment.SolveAssignment(rect), "照片少於格子")
    End Sub

    <TestMethod>
    Public Sub AssignAll_KeepsPhotoOrderWhenAspectsTie()
        Dim cells = CollageTemplates.Grid(2, 2).CreateCells()
        Dim photos = {Ready("a", 100, 100), Ready("b", 100, 100), Ready("c", 100, 100), Ready("d", 100, 100), Ready("e", 100, 100)}

        PhotoAssignment.AssignAll(cells, photos, New SizeF(100, 100))

        CollectionAssert.AreEqual({"a", "b", "c", "d"}, cells.Select(Function(c) c.PhotoId).ToArray())
    End Sub

    <TestMethod>
    Public Sub FillEmpty_SkipsUsedAndNotReadyPhotos()
        Dim cells = CollageTemplates.Grid(1, 3).CreateCells()
        cells(1).PhotoId = "a"
        Dim pending = New PhotoAsset("C:\p.jpg") With {.Id = "p"}
        Dim failed = New PhotoAsset("C:\f.jpg") With {.Id = "f", .Status = PhotoStatus.Failed}
        Dim photos = {Ready("a", 100, 100), pending, failed, Ready("b", 100, 100)}

        PhotoAssignment.FillEmpty(cells, photos, New SizeF(300, 100))

        Assert.AreEqual("a", cells(1).PhotoId, "已放好的不動")
        CollectionAssert.AreEquivalent({"b", Nothing}, {cells(0).PhotoId, cells(2).PhotoId})
    End Sub

    <TestMethod>
    Public Sub Swap_ExchangesPhotosAndResetsCrop()
        Dim a As New Cell With {.PhotoId = "a", .Crop = New CropInfo With {.Scale = 2}}
        Dim b As New Cell With {.PhotoId = Nothing}

        PhotoAssignment.Swap(a, b)

        Assert.IsNull(a.PhotoId)
        Assert.AreEqual("a", b.PhotoId)
        Assert.AreEqual(1.0F, a.Crop.Scale)
    End Sub

    <TestMethod>
    Public Sub Place_SwapsWhenPhotoAlreadyInAnotherCell()
        Dim cells = CollageTemplates.Grid(1, 3).CreateCells()
        cells(0).PhotoId = "a"
        cells(2).PhotoId = "c"

        PhotoAssignment.Place(cells, 2, "a")
        CollectionAssert.AreEqual({"c", Nothing, "a"}, cells.Select(Function(c) c.PhotoId).ToArray())

        PhotoAssignment.Place(cells, 1, "new")
        Assert.AreEqual("new", cells(1).PhotoId)
    End Sub

End Class

<TestClass>
Public Class JustifiedLayoutTests

    Private Shared Sub AssertTilesCanvas(t As CollageTemplate)
        Dim area = t.Cells.Sum(Function(r) r.Width * r.Height)
        Assert.AreEqual(1.0F, area, 0.001F, "面積總和")
        For i = 0 To t.CellCount - 1
            Dim r = t.Cells(i)
            Assert.IsTrue(r.Left >= -0.0001F AndAlso r.Top >= -0.0001F AndAlso r.Right <= 1.0001F AndAlso r.Bottom <= 1.0001F, $"第 {i} 格超出畫布")
            For j = i + 1 To t.CellCount - 1
                Dim o = RectangleF.Intersect(r, t.Cells(j))
                Assert.IsTrue(o.Width * o.Height < 0.0001F, $"第 {i}、{j} 格重疊")
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub Create_OneCellPerPhotoAndTilesCanvas()
        Dim aspects = {1.5, 0.75, 1.0, 1.5, 1.5, 0.66, 1.33}
        Dim t = JustifiedLayout.Create(aspects, 1.0)

        Assert.AreEqual(CollageTemplates.AutoId, t.Id)
        Assert.AreEqual(aspects.Length, t.CellCount)
        AssertTilesCanvas(t)
    End Sub

    <TestMethod>
    Public Sub Create_EveryPhotoIsCroppedByTheSameSmallAmount()
        Dim aspects = {1.5, 0.75, 1.0, 1.5, 1.5, 0.66, 1.33, 1.5, 0.75}
        Dim canvas = New SizeF(1600, 1200)
        Dim t = JustifiedLayout.Create(aspects, canvas.Width / canvas.Height)

        Dim ratios = Enumerable.Range(0, aspects.Length).
            Select(Function(i) CellGeometry.GetCellAspect(t.Cells(i), canvas) / aspects(i)).ToList()
        Assert.AreEqual(ratios.Min(), ratios.Max(), 0.001, "所有格子與照片的比例差異相同")
        Assert.IsTrue(PhotoAssignment.CropLoss(1, ratios(0)) < 0.25, $"裁切 {PhotoAssignment.CropLoss(1, ratios(0)):P0} 過多")
    End Sub

    <TestMethod>
    Public Sub Create_RowCountFollowsCanvasShape()
        Dim aspects = Enumerable.Repeat(1.0, 12).ToArray()
        Dim rows As Func(Of CollageTemplate, Integer) = Function(t) t.Cells.Select(Function(c) Math.Round(c.Top, 3)).Distinct().Count()

        Assert.AreEqual(3, rows(JustifiedLayout.Create(aspects, 4 / 3.0)), "4:3 → 3 列 × 4")
        Assert.IsTrue(rows(JustifiedLayout.Create(aspects, 9 / 16.0)) > 3, "直式畫布列數較多")
    End Sub

    <TestMethod>
    Public Sub Create_HandlesEdgeCases()
        Assert.AreEqual(0, JustifiedLayout.Create(Array.Empty(Of Double)(), 1).CellCount)

        Dim one = JustifiedLayout.Create({1.5}, 1)
        Assert.AreEqual(New RectangleF(0, 0, 1, 1), one.Cells(0))

        AssertTilesCanvas(JustifiedLayout.Create({0, Double.NaN, -1, 2}, 1))
        AssertTilesCanvas(JustifiedLayout.Create(Enumerable.Repeat(1.33, 100).ToArray(), 0.5))
    End Sub

    <TestMethod>
    Public Sub Partition_KeepsOrderAndBalancesRows()
        Dim rows = JustifiedLayout.Partition({1.0, 1.0, 2.0, 1.0, 1.0}, 2)
        Assert.AreEqual(2, rows.Count)
        CollectionAssert.AreEqual({0, 1, 2, 3, 4}, rows.SelectMany(Function(r) r).ToArray())
    End Sub

End Class

<TestClass>
Public Class CanvasPresetsTests

    <TestMethod>
    Public Sub SizeFor_UsesLongEdge()
        Dim portrait = CanvasPresets.All.First(Function(p) p.RatioWidth = 9)
        Assert.AreEqual(New Size(1152, 2048), portrait.SizeFor(2048))
        Assert.AreEqual(New Size(2048, 2048), CanvasPresets.All(0).SizeFor(2048))
    End Sub

    <TestMethod>
    Public Sub Find_MatchesWithinTolerance()
        Assert.AreEqual(4, CanvasPresets.Find(New Size(1600, 2000)).RatioWidth)
        Assert.IsNull(CanvasPresets.Find(New Size(1000, 123)))
    End Sub

End Class

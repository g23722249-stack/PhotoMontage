Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class CropOrientationTests

    Private Const Tolerance As Single = 0.01F
    Private Shared ReadOnly Photo As New SizeF(400, 200)

    <TestMethod>
    Public Sub OrientedSize_SwapsOnQuarterTurns()
        Assert.AreEqual(New SizeF(200, 400), PhotoOrientation.OrientedSize(Photo, New CropInfo With {.Rotation = 1}))
        Assert.AreEqual(Photo, PhotoOrientation.OrientedSize(Photo, New CropInfo With {.Rotation = 2}))
        Assert.AreEqual(New SizeF(200, 400), PhotoOrientation.OrientedSize(Photo, New CropInfo With {.Rotation = -1}))
    End Sub

    <TestMethod>
    Public Sub ToOriented_RotatesClockwise()
        ' 原圖左上角轉 90° 後到右上角
        Dim crop As New CropInfo With {.Rotation = 1}
        Dim q = PhotoOrientation.ToOriented(New PointF(0, 0), Photo, crop)
        Assert.AreEqual(200, q.X, Tolerance)
        Assert.AreEqual(0, q.Y, Tolerance)
    End Sub

    <TestMethod>
    Public Sub ToOriginal_InvertsToOriented()
        For rotation = 0 To 3
            For Each flip In {False, True}
                Dim crop As New CropInfo With {.Rotation = rotation, .FlipHorizontal = flip}
                Dim p As New PointF(37, 151)
                Dim back = PhotoOrientation.ToOriginal(PhotoOrientation.ToOriented(p, Photo, crop), Photo, crop)
                Assert.AreEqual(p.X, back.X, Tolerance, $"rotation {rotation} flip {flip}")
                Assert.AreEqual(p.Y, back.Y, Tolerance, $"rotation {rotation} flip {flip}")
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub DrawPoints_FlipMirrorsDestination()
        Dim crop As New CropInfo With {.FlipHorizontal = True}
        Dim dest As New RectangleF(0, 0, 100, 50)
        Dim points = PhotoOrientation.GetDrawPoints(Photo, dest, New RectangleF(0, 0, 400, 200), crop, Nothing)
        ' 原圖左上角畫到右上角，右上角畫到左上角
        Assert.AreEqual(100, points(0).X, Tolerance)
        Assert.AreEqual(0, points(1).X, Tolerance)
        Assert.AreEqual(50, points(2).Y, Tolerance)
    End Sub

    <TestMethod>
    Public Sub CropRect_RoundTripsThroughCropInfo()
        Dim rect As New RectangleF(120, 40, 150, 100)
        Dim crop = CropMath.FromCropRect(Photo, rect, New CropInfo With {.Rotation = 2, .FlipHorizontal = True})
        Assert.AreEqual(2, crop.Rotation)
        Assert.IsTrue(crop.FlipHorizontal)
        Dim back = CropMath.ToCropRect(Photo, 1.5, crop)
        Assert.AreEqual(rect.X, back.X, 0.5F)
        Assert.AreEqual(rect.Y, back.Y, 0.5F)
        Assert.AreEqual(rect.Width, back.Width, 0.5F)
        Assert.AreEqual(rect.Height, back.Height, 0.5F)
    End Sub

    <TestMethod>
    Public Sub CropRect_FullCoverIsDefault()
        Dim crop = CropMath.FromCropRect(Photo, New RectangleF(100, 0, 200, 200), Nothing)
        Assert.AreEqual(1.0F, crop.Scale, Tolerance)
        Assert.AreEqual(0F, crop.OffsetX, Tolerance)
    End Sub

    <TestMethod>
    Public Sub CropRect_TooSmallIsLimitedByMaxScale()
        Dim crop = CropMath.FromCropRect(Photo, New RectangleF(0, 0, 10, 10), Nothing)
        Assert.AreEqual(CropMath.MaxScale, crop.Scale, Tolerance)
    End Sub

    <TestMethod>
    Public Sub Swap_KeepsOrientationWithPhoto()
        Dim a As New Cell With {.PhotoId = "a", .Crop = New CropInfo With {.Rotation = 1, .OffsetX = 0.5F}}
        Dim b As New Cell With {.PhotoId = "b"}
        PhotoAssignment.Swap(a, b)
        Assert.AreEqual("a", b.PhotoId)
        Assert.AreEqual(1, b.Crop.Rotation)
        Assert.AreEqual(0F, b.Crop.OffsetX)
        Assert.AreEqual(0, a.Crop.Rotation)
    End Sub

    <TestMethod>
    Public Sub DesignState_KeepsOrientation()
        Dim p As New MontageProject()
        p.Collage.Cells.Add(New Cell With {.Bounds = New RectangleF(0, 0, 1, 1), .PhotoId = "x", .Crop = New CropInfo With {.Rotation = 3, .FlipHorizontal = True}})
        p.Free.Items.Add(New FreeItem With {.PhotoId = "x", .Crop = New CropInfo With {.Rotation = 1}})
        Dim copy As New MontageProject()
        DesignState.FromJson(DesignState.Capture(p).ToJson()).ApplyTo(copy)
        Assert.AreEqual(3, copy.Collage.Cells(0).Crop.Rotation)
        Assert.IsTrue(copy.Collage.Cells(0).Crop.FlipHorizontal)
        Assert.AreEqual(1, copy.Free.Items(0).Crop.Rotation)
    End Sub

End Class

<TestClass>
Public Class CellDividerTests

    Private Shared Function Rects(template As CollageTemplate) As List(Of RectangleF)
        Return template.Cells.ToList()
    End Function

    <TestMethod>
    Public Sub Grid2x2_HasOneFullLineEachWay()
        Dim dividers = CellDividers.FindAll(Rects(CollageTemplates.Grid(2, 2)))
        Assert.AreEqual(2, dividers.Count)
        Dim v = dividers.Single(Function(d) d.Vertical)
        Assert.AreEqual(0.5F, v.Position, 0.001F)
        Assert.AreEqual(0F, v.SpanStart, 0.001F)
        Assert.AreEqual(1.0F, v.SpanEnd, 0.001F)
        Assert.AreEqual(2, v.Before.Count)
        Assert.AreEqual(2, v.After.Count)
    End Sub

    <TestMethod>
    Public Sub CenterLayout_SplitsLinesAtCrossingCells()
        ' 中央大圖：x = 0.25 的分隔線只在中段（上下兩排的格子橫跨過去）
        Dim dividers = CellDividers.FindAll(Rects(CollageTemplates.Find("center-6")))
        Dim left = dividers.Single(Function(d) d.Vertical AndAlso Math.Abs(d.Position - 0.25F) < 0.001F)
        Assert.AreEqual(0.25F, left.SpanStart, 0.001F)
        Assert.AreEqual(0.75F, left.SpanEnd, 0.001F)
        Assert.AreEqual(1, left.Before.Count)
        Assert.AreEqual(1, left.After.Count)
    End Sub

    <TestMethod>
    Public Sub Move_ResizesBothSidesAndKeepsMinimum()
        Dim cells = CollageTemplates.Grid(1, 2).CreateCells()
        Dim divider = CellDividers.FindAll(cells.Select(Function(c) c.Bounds).ToList()).Single()
        Dim pos = CellDividers.Move(cells, divider, 0.7F)
        Assert.AreEqual(0.7F, pos, 0.001F)
        Assert.AreEqual(0.7F, cells(0).Bounds.Width, 0.001F)
        Assert.AreEqual(0.7F, cells(1).Bounds.Left, 0.001F)
        Assert.AreEqual(0.3F, cells(1).Bounds.Width, 0.001F)

        pos = CellDividers.Move(cells, divider, 0.999F)
        Assert.AreEqual(1 - CellDividers.MinCellSize, pos, 0.001F)
    End Sub

    <TestMethod>
    Public Sub Move_NoGapsOrOverlapsAfterward()
        Dim cells = CollageTemplates.Find("big-top-3").CreateCells()
        For Each d In CellDividers.FindAll(cells.Select(Function(c) c.Bounds).ToList())
            CellDividers.Move(cells, d, d.Position + 0.08F)
        Next
        Dim area = cells.Sum(Function(c) c.Bounds.Width * c.Bounds.Height)
        Assert.AreEqual(1.0F, area, 0.001F)
    End Sub

    <TestMethod>
    Public Sub DesignState_KeepsAdjustedFlag()
        Dim p As New MontageProject()
        p.Collage.CellsAdjusted = True
        Dim copy As New MontageProject()
        DesignState.Capture(p).ApplyTo(copy)
        Assert.IsTrue(copy.Collage.CellsAdjusted)
    End Sub

End Class

<TestClass>
Public Class TemplateCatalogTests

    <TestMethod>
    Public Sub BuiltIn_KeepsLegacyIdsAndHasManyLayouts()
        For Each id In {"grid-1x2", "grid-2x1", "grid-1x3", "grid-2x2", "grid-2x3", "grid-3x3", "big-left-2", "big-top-3", "center-6"}
            Assert.IsNotNull(CollageTemplates.Find(id), id)
        Next
        Assert.IsTrue(CollageTemplates.BuiltIn.Count >= 40)
    End Sub

    <TestMethod>
    Public Sub BuiltIn_OrderedByPhotoCount()
        Dim counts = CollageTemplates.BuiltIn.Select(Function(t) t.CellCount).ToList()
        For i = 1 To counts.Count - 1
            Assert.IsTrue(counts(i) >= counts(i - 1), $"第 {i} 個版型的張數比前一個少")
        Next
    End Sub

End Class

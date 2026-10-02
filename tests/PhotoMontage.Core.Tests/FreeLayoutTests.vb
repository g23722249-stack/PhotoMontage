Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class FreeGeometryTests

    Private Const Tolerance As Single = 0.01F
    Private Shared ReadOnly Canvas As New SizeF(1000, 800)

    <TestMethod>
    Public Sub OuterSize_NoFrameFollowsPhotoAspect()
        Dim item As New FreeItem With {.Width = 0.3F, .InnerAspect = 1.5F, .Frame = FrameStyle.None}
        Dim size = FreeGeometry.GetOuterSize(item, Canvas)
        Assert.AreEqual(300, size.Width, Tolerance)
        Assert.AreEqual(200, size.Height, Tolerance)
    End Sub

    <TestMethod>
    Public Sub OuterSize_WhiteFrameAddsEqualBorders()
        Dim item As New FreeItem With {.Width = 0.3F, .InnerAspect = 1.5F, .Frame = FrameStyle.White, .FrameWidth = 0.05F}
        Dim size = FreeGeometry.GetOuterSize(item, Canvas)
        ' 邊框 15；照片 270 × 180；外框 300 × 210
        Assert.AreEqual(210, size.Height, Tolerance)
        Dim inner = FreeGeometry.GetInnerRect(item, size)
        Assert.AreEqual(270, inner.Width, Tolerance)
        Assert.AreEqual(-105 + 15, inner.Top, Tolerance)
    End Sub

    <TestMethod>
    Public Sub OuterSize_PolaroidHasWiderBottom()
        Dim item As New FreeItem With {.Width = 0.3F, .InnerAspect = 1.5F, .Frame = FrameStyle.Polaroid, .FrameWidth = 0.05F}
        Dim size = FreeGeometry.GetOuterSize(item, Canvas)
        Dim ins = FreeGeometry.GetInsets(item, size.Width)
        Assert.AreEqual(15 * FreeGeometry.PolaroidBottomFactor, ins.Bottom, Tolerance)
        Assert.AreEqual(180 + 15 + 52.5F, size.Height, Tolerance)
        Dim inner = FreeGeometry.GetInnerRect(item, size)
        Assert.AreEqual(size.Height / 2 - 52.5F, inner.Bottom, Tolerance)
    End Sub

    <TestMethod>
    Public Sub GetFrame_UsesCanvasRelativeCenter()
        Dim item As New FreeItem With {.CenterX = 0.25F, .CenterY = 0.5F, .Rotation = 12}
        Dim f = FreeGeometry.GetFrame(item, New RectangleF(100, 50, 1000, 800))
        Assert.AreEqual(New PointF(350, 450), f.Center)
        Assert.AreEqual(12.0F, f.Rotation)
    End Sub

    <TestMethod>
    Public Sub HitTest_ReturnsTopmostAndRespectsRotation()
        Dim bottom As New FreeItem With {.Id = "bottom", .CenterX = 0.5F, .CenterY = 0.5F, .Width = 0.4F, .Frame = FrameStyle.None, .InnerAspect = 1}
        Dim top As New FreeItem With {.Id = "top", .CenterX = 0.55F, .CenterY = 0.5F, .Width = 0.2F, .Frame = FrameStyle.None, .InnerAspect = 1}
        Dim items = New List(Of FreeItem) From {bottom, top}
        Dim bounds As New RectangleF(0, 0, 1000, 1000)

        Assert.AreEqual("top", FreeGeometry.HitTest(items, bounds, New PointF(550, 500)).Id)
        Assert.AreEqual("bottom", FreeGeometry.HitTest(items, bounds, New PointF(350, 500)).Id)
        Assert.IsNull(FreeGeometry.HitTest(items, bounds, New PointF(50, 50)))

        ' 旋轉 45 度的正方形，原本的角落附近不再命中
        bottom.Rotation = 45
        Assert.IsNull(FreeGeometry.HitTest(New List(Of FreeItem) From {bottom}, bounds, New PointF(305, 305)))
    End Sub

    <TestMethod>
    Public Sub AxisAlignedBounds_GrowsWithRotation()
        Dim f As New TextFrame(New PointF(0, 0), New SizeF(100, 100), 45)
        Dim r = FreeGeometry.GetAxisAlignedBounds(f)
        Assert.AreEqual(141.42F, r.Width, 0.1F)
        Assert.AreEqual(-70.71F, r.Left, 0.1F)
    End Sub

    <TestMethod>
    Public Sub HitTestCorner_FindsRotatedCorner()
        Dim f As New TextFrame(New PointF(100, 100), New SizeF(80, 40), 90)
        ' 旋轉 90 度後左上角在 (120, 60)
        Assert.AreEqual(0, FreeGeometry.HitTestCorner(f, New PointF(120, 60), 6))
        Assert.AreEqual(-1, FreeGeometry.HitTestCorner(f, New PointF(100, 100), 6))
    End Sub

    <TestMethod>
    Public Sub ScaleWidth_IsProportionalAndClamped()
        Dim c As New PointF(0, 0)
        Assert.AreEqual(0.6F, FreeGeometry.ScaleWidth(0.3F, c, New PointF(100, 0), New PointF(200, 0)), Tolerance)
        Assert.AreEqual(FreeLayoutSettings.MinItemWidth, FreeGeometry.ScaleWidth(0.3F, c, New PointF(100, 0), New PointF(1, 0)))
        Assert.AreEqual(FreeLayoutSettings.MaxItemWidth, FreeGeometry.ScaleWidth(0.3F, c, New PointF(10, 0), New PointF(1000, 0)))
    End Sub

End Class

<TestClass>
Public Class FreeSnappingTests

    Private Shared ReadOnly CanvasRect As New RectangleF(0, 0, 1000, 800)

    <TestMethod>
    Public Sub Snap_AlignsLeftEdgeWithinThreshold()
        Dim other As New RectangleF(100, 100, 200, 150)
        Dim moving As New RectangleF(104, 400, 120, 90)
        Dim r = FreeSnapping.Snap(moving, {other}, CanvasRect, 8)
        Assert.AreEqual(-4.0F, r.Offset.X, 0.01F)
        CollectionAssert.Contains(r.VerticalGuides.ToList(), 100.0F)
    End Sub

    <TestMethod>
    Public Sub Snap_CentersOnCanvas()
        Dim moving As New RectangleF(437, 300, 120, 90) ' 中心 497
        Dim r = FreeSnapping.Snap(moving, Array.Empty(Of RectangleF)(), CanvasRect, 8)
        Assert.AreEqual(3.0F, r.Offset.X, 0.01F)
        CollectionAssert.Contains(r.VerticalGuides.ToList(), 500.0F)
    End Sub

    <TestMethod>
    Public Sub Snap_PicksClosestTargetAndIgnoresFarOnes()
        Dim others = {New RectangleF(200, 0, 50, 50), New RectangleF(0, 202, 50, 50)}
        Dim moving As New RectangleF(600, 205, 100, 100)
        Dim r = FreeSnapping.Snap(moving, others, CanvasRect, 8)
        Assert.AreEqual(0F, r.Offset.X, "水平方向沒有可吸附的目標")
        Assert.AreEqual(-3.0F, r.Offset.Y, 0.01F, "上緣吸到 202")
        Assert.AreEqual(0, r.VerticalGuides.Count)
    End Sub

End Class

<TestClass>
Public Class FreeArrangeTests

    <TestMethod>
    Public Sub Arrange_ReturnsOnePlacementPerPhotoInsideCanvas()
        Dim aspects = Enumerable.Range(0, 11).Select(Function(i) If(i Mod 3 = 0, 0.75, 1.4)).ToList()
        Dim result = FreeArrange.Arrange(aspects, 1.5, 0.8F, 7)
        Assert.AreEqual(11, result.Count)
        Assert.IsTrue(result.All(Function(p) p.CenterX > 0 AndAlso p.CenterX < 1 AndAlso p.CenterY > 0 AndAlso p.CenterY < 1))
        Assert.IsTrue(result.All(Function(p) Math.Abs(p.Rotation) <= 14.01F))
    End Sub

    <TestMethod>
    Public Sub Arrange_IsDeterministic()
        Dim aspects = {1.5, 0.75, 1.0, 1.33}
        Dim a = FreeArrange.Arrange(aspects, 1.5, 0.5F, 42)
        Dim b = FreeArrange.Arrange(aspects, 1.5, 0.5F, 42)
        CollectionAssert.AreEqual(a.Select(Function(p) p.CenterX).ToArray(), b.Select(Function(p) p.CenterX).ToArray())
        CollectionAssert.AreEqual(a.Select(Function(p) p.Rotation).ToArray(), b.Select(Function(p) p.Rotation).ToArray())
    End Sub

    <TestMethod>
    Public Sub Arrange_TidyHasNoRotationAndNoOverlap()
        Dim aspects = Enumerable.Repeat(1.5, 6).ToList()
        Dim canvasAspect = 1.5
        Dim result = FreeArrange.Arrange(aspects, canvasAspect, 0, 1)
        Assert.IsTrue(result.All(Function(p) p.Rotation = 0))

        Dim canvas As New SizeF(1500, 1000)
        Dim rects = result.Select(Function(p)
                                      Dim w = p.Width * canvas.Width, h = w / 1.5F
                                      Return New RectangleF(p.CenterX * canvas.Width - w / 2, p.CenterY * canvas.Height - h / 2, w, h)
                                  End Function).ToList()
        For i = 0 To rects.Count - 1
            For j = i + 1 To rects.Count - 1
                Assert.IsFalse(rects(i).IntersectsWith(rects(j)), $"第 {i}、{j} 張重疊")
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub Apply_LoosenessShufflesLayersButKeepsAllItems()
        Dim settings As New FreeLayoutSettings With {.Looseness = 1}
        For i = 0 To 9
            settings.Items.Add(New FreeItem With {.PhotoId = "p" & i, .InnerAspect = 1.5F})
        Next
        FreeArrange.Apply(settings, 1.5, 3)
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 10).Select(Function(i) "p" & i).ToArray(), settings.Items.Select(Function(i) i.PhotoId).ToArray())
        Assert.IsTrue(settings.Items.Any(Function(i) i.Rotation <> 0))
    End Sub

    <TestMethod>
    Public Sub Arrange_EmptyReturnsEmpty()
        Assert.AreEqual(0, FreeArrange.Arrange(Array.Empty(Of Double)(), 1, 0.5F, 1).Count)
    End Sub

End Class

<TestClass>
Public Class FreeStateTests

    <TestMethod>
    Public Sub DesignState_RoundTripsFreeLayout()
        Dim p As New MontageProject With {.Mode = MontageMode.Free}
        p.Free.Looseness = 0.7F
        p.Free.Items.Add(New FreeItem With {.PhotoId = "a", .CenterX = 0.2F, .CenterY = 0.3F, .Width = 0.4F, .InnerAspect = 0.75F,
                                             .Rotation = -8, .Frame = FrameStyle.Polaroid, .FrameWidth = 0.05F, .Shadow = False,
                                             .Crop = New CropInfo With {.Scale = 1.5F, .OffsetX = 0.2F}})
        Dim json = DesignState.Capture(p).ToJson()
        Dim restored As New MontageProject()
        DesignState.FromJson(json).ApplyTo(restored)

        Assert.AreEqual(MontageMode.Free, restored.Mode)
        Assert.AreEqual(0.7F, restored.Free.Looseness)
        Dim item = restored.Free.Items.Single()
        Assert.AreEqual(p.Free.Items(0).Id, item.Id)
        Assert.AreEqual(FrameStyle.Polaroid, item.Frame)
        Assert.AreEqual(-8.0F, item.Rotation)
        Assert.IsFalse(item.Shadow)
        Assert.AreEqual(1.5F, item.Crop.Scale)
        Assert.AreEqual(json, DesignState.Capture(restored).ToJson())
    End Sub

    <TestMethod>
    Public Sub OldSnapshotsWithoutFreeStillLoad()
        Dim json = "{""Mode"":0,""CanvasWidth"":100,""CanvasHeight"":100,""Cells"":[],""Texts"":[]}"
        Dim restored As New MontageProject()
        DesignState.FromJson(json).ApplyTo(restored)
        Assert.AreEqual(0, restored.Free.Items.Count)
    End Sub

    <TestMethod>
    Public Sub Clone_CopiesEverythingButId()
        Dim a As New FreeItem With {.PhotoId = "x", .Rotation = 5, .Frame = FrameStyle.None, .Crop = New CropInfo With {.Scale = 2}}
        Dim b = a.Clone()
        Assert.AreNotEqual(a.Id, b.Id)
        Assert.AreEqual("x", b.PhotoId)
        Assert.AreEqual(FrameStyle.None, b.Frame)
        b.Crop.Scale = 3
        Assert.AreEqual(2.0F, a.Crop.Scale, "取景是獨立的複本")
    End Sub

    <TestMethod>
    Public Sub PlanDecodeEdges_UsesFreeItemPhotoArea()
        Dim p As New MontageProject With {.Mode = MontageMode.Free}
        p.Photos.Add(New PhotoAsset("C:\a.jpg") With {.Id = "a", .Status = PhotoStatus.Ready, .PixelSize = New Size(8000, 6000)})
        p.Free.Items.Add(New FreeItem With {.PhotoId = "a", .Width = 0.25F, .InnerAspect = 4 / 3.0F, .Frame = FrameStyle.None})
        Dim edges = CollageExporter.PlanDecodeEdges(p, New RectangleF(0, 0, 2000, 1500))
        ' 照片區域 500 × 375 → 長邊約 525（多留 5%）
        Assert.IsTrue(edges("a") >= 500 AndAlso edges("a") <= 540, $"edge = {edges("a")}")
    End Sub

End Class

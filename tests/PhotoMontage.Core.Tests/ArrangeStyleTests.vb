Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class ArrangeStyleTests

    Private Const CanvasAspect As Double = 1.5

    Private Shared Function Aspects(n As Integer) As List(Of Double)
        Dim values = {1.5, 0.75, 1.0, 1.33, 0.8}
        Return Enumerable.Range(0, n).Select(Function(i) values(i Mod values.Length)).ToList()
    End Function

    Private Shared Function AllStyles() As ArrangeStyle()
        Return CType([Enum].GetValues(GetType(ArrangeStyle)), ArrangeStyle())
    End Function

    ''' <summary>照片在畫布像素座標（高 = 1、寬 = 畫布比例）中旋轉後的外接矩形。</summary>
    Private Shared Function Bounds(p As FreeArrange.Placement, aspect As Double) As (L As Double, T As Double, R As Double, B As Double)
        Dim w = p.Width * CanvasAspect, h = w / aspect
        Dim rad = p.Rotation * Math.PI / 180
        Dim ex = w / 2 * Math.Abs(Math.Cos(rad)) + h / 2 * Math.Abs(Math.Sin(rad))
        Dim ey = w / 2 * Math.Abs(Math.Sin(rad)) + h / 2 * Math.Abs(Math.Cos(rad))
        Dim cx = p.CenterX * CanvasAspect, cy = p.CenterY
        Return (cx - ex, cy - ey, cx + ex, cy + ey)
    End Function

    <TestMethod>
    Public Sub EveryStyle_StaysInsideCanvas()
        For Each style In AllStyles()
            For Each n In {1, 2, 5, 12, 30}
                For Each overlap In {True, False}
                    Dim a = Aspects(n)
                    Dim result = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = style, .Looseness = 0.6F, .Overlap = overlap, .Clockwise = True}, 11)
                    Assert.AreEqual(n, result.Placements.Count, $"{style} n={n}")
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, n).ToList(), result.ZOrder, $"{style} n={n}")
                    If style = ArrangeStyle.Scatter AndAlso overlap Then Continue For   ' 原本的散佈演算法以中心點限制位置
                    For i = 0 To n - 1
                        Dim b = Bounds(result.Placements(i), a(i))
                        Assert.IsTrue(b.L >= -0.01 AndAlso b.T >= -0.01 AndAlso b.R <= CanvasAspect + 0.01 AndAlso b.B <= 1.01,
                                      $"{style} n={n} overlap={overlap}：第 {i} 張超出畫布")
                    Next
                Next
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub NoOverlapOption_KeepsPhotosApart()
        For Each style In AllStyles().Where(Function(s) FreeArrangeStyles.SupportsOverlap(s) OrElse s = ArrangeStyle.Grid)
            Dim n = 12
            Dim a = Aspects(n)
            Dim result = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = style, .Looseness = 0.5F, .Overlap = False, .Clockwise = True}, 5)
            For i = 0 To n - 1
                Dim bi = Bounds(result.Placements(i), a(i))
                For j = i + 1 To n - 1
                    Dim bj = Bounds(result.Placements(j), a(j))
                    Dim apart = bi.R <= bj.L + 0.001 OrElse bj.R <= bi.L + 0.001 OrElse bi.B <= bj.T + 0.001 OrElse bj.B <= bi.T + 0.001
                    Assert.IsTrue(apart, $"{style}：第 {i} 與 {j} 張重疊")
                Next
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub OverlapOption_MakesPhotosLarger()
        Dim a = Aspects(10)
        For Each style In {ArrangeStyle.Spiral, ArrangeStyle.Ring, ArrangeStyle.Heart, ArrangeStyle.Diagonal}
            Dim apart = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = style, .Overlap = False}, 1)
            Dim overlapped = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = style, .Overlap = True}, 1)
            ' 整組會重新縮放進畫布，所以比較照片相對於整組範圍的大小：重疊時照片間距相對變小
            Dim spreadApart = apart.Placements.Max(Function(p) p.CenterX) - apart.Placements.Min(Function(p) p.CenterX)
            Dim spreadOverlap = overlapped.Placements.Max(Function(p) p.CenterX) - overlapped.Placements.Min(Function(p) p.CenterX)
            Assert.IsTrue(apart.Placements(1).Width / spreadApart < overlapped.Placements(1).Width / spreadOverlap, style.ToString())
        Next
    End Sub

    <TestMethod>
    Public Sub Spiral_DirectionMirrorsVertically()
        Dim a = Aspects(8)
        Dim cw = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = ArrangeStyle.Spiral, .Clockwise = True}, 3)
        Dim ccw = FreeArrangeStyles.Arrange(a, CanvasAspect, New ArrangeOptions With {.Style = ArrangeStyle.Spiral, .Clockwise = False}, 3)
        For i = 0 To 7
            Assert.AreEqual(cw.Placements(i).CenterX, ccw.Placements(i).CenterX, 0.001F)
            Assert.AreEqual(1 - cw.Placements(i).CenterY, ccw.Placements(i).CenterY, 0.001F)
        Next
    End Sub

    <TestMethod>
    Public Sub SpiralAndRing_PutCenterPhotoOnTop()
        For Each style In {ArrangeStyle.Spiral, ArrangeStyle.Ring}
            Dim result = FreeArrangeStyles.Arrange(Aspects(8), CanvasAspect, New ArrangeOptions With {.Style = style}, 3)
            Assert.AreEqual(0, result.ZOrder.Last(), style.ToString())
            ' 中央那張在畫布中間
            Assert.AreEqual(0.5F, result.Placements(0).CenterX, 0.05F, style.ToString())
            Assert.AreEqual(0.5F, result.Placements(0).CenterY, 0.05F, style.ToString())
        Next
    End Sub

    <TestMethod>
    Public Sub Arrange_IsDeterministic()
        For Each style In AllStyles()
            Dim options As New ArrangeOptions With {.Style = style, .Looseness = 0.7F, .Overlap = True}
            Dim a = FreeArrangeStyles.Arrange(Aspects(9), CanvasAspect, options, 42)
            Dim b = FreeArrangeStyles.Arrange(Aspects(9), CanvasAspect, options, 42)
            For i = 0 To 8
                Assert.AreEqual(a.Placements(i).CenterX, b.Placements(i).CenterX, style.ToString())
                Assert.AreEqual(a.Placements(i).Rotation, b.Placements(i).Rotation, style.ToString())
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub Grid_IsUpright()
        Dim result = FreeArrangeStyles.Arrange(Aspects(7), CanvasAspect, New ArrangeOptions With {.Style = ArrangeStyle.Grid, .Looseness = 1}, 9)
        Assert.IsTrue(result.Placements.All(Function(p) p.Rotation = 0))
    End Sub

    <TestMethod>
    Public Sub Apply_UsesStyleAndKeepsItems()
        Dim settings As New FreeLayoutSettings With {.Style = ArrangeStyle.Ring, .Overlap = False}
        For i = 0 To 5
            settings.Items.Add(New FreeItem With {.PhotoId = $"p{i}"})
        Next
        Dim ids = settings.Items.Select(Function(i) i.Id).ToList()
        FreeArrange.Apply(settings, CanvasAspect, 1)
        CollectionAssert.AreEquivalent(ids, settings.Items.Select(Function(i) i.Id).ToList())
    End Sub

    <TestMethod>
    Public Sub DesignState_KeepsArrangeOptions()
        Dim p As New MontageProject()
        p.Free.Style = ArrangeStyle.Heart
        p.Free.Overlap = False
        p.Free.Clockwise = False
        Dim copy As New MontageProject()
        DesignState.FromJson(DesignState.Capture(p).ToJson()).ApplyTo(copy)
        Assert.AreEqual(ArrangeStyle.Heart, copy.Free.Style)
        Assert.IsFalse(copy.Free.Overlap)
        Assert.IsFalse(copy.Free.Clockwise)
    End Sub

End Class

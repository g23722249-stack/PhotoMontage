Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class PrintPlannerTests

    Private Const Tolerance As Single = 0.01F
    ''' <summary>A4 直向，1/100 吋。</summary>
    Private Shared ReadOnly A4 As New SizeF(827, 1169)

    <TestMethod>
    Public Sub UseLandscape_AutoFollowsCanvas()
        Assert.IsTrue(PrintPlanner.UseLandscape(PrintOrientation.Auto, 1.5))
        Assert.IsFalse(PrintPlanner.UseLandscape(PrintOrientation.Auto, 0.75))
        Assert.IsFalse(PrintPlanner.UseLandscape(PrintOrientation.Auto, 1.0))
        Assert.IsFalse(PrintPlanner.UseLandscape(PrintOrientation.Portrait, 2.0))
        Assert.IsTrue(PrintPlanner.UseLandscape(PrintOrientation.Landscape, 0.5))
    End Sub

    <TestMethod>
    Public Sub ContentArea_SubtractsMarginAndRespectsPrintable()
        Dim area = PrintPlanner.GetContentArea(A4, New RectangleF(0, 0, A4.Width, A4.Height), 50)
        Assert.AreEqual(New RectangleF(50, 50, 727, 1069), area)

        ' 無邊界時仍受印表機的實體邊界限制
        Dim printable As New RectangleF(17, 17, 793, 1135)
        Assert.AreEqual(printable, PrintPlanner.GetContentArea(A4, printable, 0))
    End Sub

    <TestMethod>
    Public Sub ContentArea_EmptyWhenMarginTooLarge()
        Assert.IsTrue(PrintPlanner.GetContentArea(New SizeF(80, 80), RectangleF.Empty, 50).IsEmpty)
    End Sub

    <TestMethod>
    Public Sub Place_FitCentersAndKeepsAspect()
        Dim area As New RectangleF(0, 0, 800, 1000)
        Dim p = PrintPlanner.Place(area, 2.0, PrintFit.Fit)
        Assert.AreEqual(800, p.Destination.Width, Tolerance)
        Assert.AreEqual(400, p.Destination.Height, Tolerance)
        Assert.AreEqual(300, p.Destination.Y, Tolerance)
        Assert.AreEqual(New RectangleF(0, 0, 1, 1), p.Source)
    End Sub

    <TestMethod>
    Public Sub Place_FillCropsCenter()
        Dim area As New RectangleF(10, 10, 800, 800)
        Dim p = PrintPlanner.Place(area, 2.0, PrintFit.Fill)
        Assert.AreEqual(area, p.Destination)
        Assert.AreEqual(0.25F, p.Source.X, Tolerance)
        Assert.AreEqual(0.5F, p.Source.Width, Tolerance)
        Assert.AreEqual(1.0F, p.Source.Height, Tolerance)

        Dim tall = PrintPlanner.Place(area, 0.5, PrintFit.Fill)
        Assert.AreEqual(0.25F, tall.Source.Y, Tolerance)
        Assert.AreEqual(0.5F, tall.Source.Height, Tolerance)
    End Sub

    <TestMethod>
    Public Sub RenderSize_MatchesPaperAtDpi()
        ' 紙上 6 × 4 吋，300 dpi → 1800 × 1200
        Dim p = PrintPlanner.Place(New RectangleF(0, 0, 600, 400), 1.5, PrintFit.Fit)
        Assert.AreEqual(New Size(1800, 1200), PrintPlanner.GetRenderSize(p, 1.5, 600))
        Assert.AreEqual(New Size(900, 600), PrintPlanner.GetRenderSize(p, 1.5, 150))
    End Sub

    <TestMethod>
    Public Sub RenderSize_FillRendersWholeCanvasLarger()
        ' 只印中間一半寬，整張作品要兩倍寬才能讓紙上達到 300 dpi
        Dim p = PrintPlanner.Place(New RectangleF(0, 0, 400, 400), 2.0, PrintFit.Fill)
        Assert.AreEqual(New Size(2400, 1200), PrintPlanner.GetRenderSize(p, 2.0, 300))
    End Sub

    <TestMethod>
    Public Sub RenderSize_CappedAtLongEdgeLimit()
        Dim p = PrintPlanner.Place(New RectangleF(0, 0, 4000, 2000), 2.0, PrintFit.Fit)
        Dim size = PrintPlanner.GetRenderSize(p, 2.0, 300)
        Assert.AreEqual(PrintPlanner.MaxPrintLongEdge, size.Width)
        Assert.AreEqual(PrintPlanner.MaxPrintLongEdge \ 2, size.Height)
    End Sub

End Class

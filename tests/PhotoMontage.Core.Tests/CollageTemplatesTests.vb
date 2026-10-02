Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class CollageTemplatesTests

    Private Const Epsilon As Single = 0.0001F

    <TestMethod>
    Public Sub Grid_CreatesRowsTimesColumnsCells()
        Dim t = CollageTemplates.Grid(2, 3)

        Assert.AreEqual("grid-2x3", t.Id)
        Assert.AreEqual(6, t.CellCount)
        Assert.AreEqual(1.0F / 3, t.Cells(0).Width, Epsilon)
        Assert.AreEqual(0.5F, t.Cells(0).Height, Epsilon)
    End Sub

    <TestMethod>
    Public Sub Grid_RejectsNonPositiveSize()
        Assert.ThrowsException(Of ArgumentOutOfRangeException)(Function() CollageTemplates.Grid(0, 2))
        Assert.ThrowsException(Of ArgumentOutOfRangeException)(Function() CollageTemplates.Grid(2, 0))
    End Sub

    <TestMethod>
    Public Sub BuiltIn_IdsAreUnique()
        Dim ids = CollageTemplates.BuiltIn.Select(Function(t) t.Id).ToList()
        Assert.AreEqual(ids.Count, ids.Distinct().Count())
    End Sub

    <TestMethod>
    Public Sub BuiltIn_CellsStayInsideCanvas()
        For Each t In CollageTemplates.BuiltIn
            For Each r In t.Cells
                Assert.IsTrue(r.Left >= -Epsilon AndAlso r.Top >= -Epsilon, $"{t.Id} 有格子超出左上")
                Assert.IsTrue(r.Right <= 1 + Epsilon AndAlso r.Bottom <= 1 + Epsilon, $"{t.Id} 有格子超出右下")
                Assert.IsTrue(r.Width > 0 AndAlso r.Height > 0, $"{t.Id} 有空的格子")
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub BuiltIn_CellsTileCanvasWithoutOverlap()
        For Each t In CollageTemplates.BuiltIn
            Dim total = t.Cells.Sum(Function(r) r.Width * r.Height)
            Assert.AreEqual(1.0F, total, 0.001F, $"{t.Id} 的格子面積總和不等於整張畫布")

            For i = 0 To t.CellCount - 1
                For j = i + 1 To t.CellCount - 1
                    Dim overlap = RectangleF.Intersect(t.Cells(i), t.Cells(j))
                    Assert.IsTrue(overlap.Width * overlap.Height < Epsilon, $"{t.Id} 的第 {i} 與 {j} 格重疊")
                Next
            Next
        Next
    End Sub

    <TestMethod>
    Public Sub Find_ReturnsTemplateOrNothing()
        Assert.IsNotNull(CollageTemplates.Find("grid-3x3"))
        Assert.IsNull(CollageTemplates.Find("no-such-template"))
    End Sub

    <TestMethod>
    Public Sub CreateCells_CopiesBoundsAndLeavesCellsEmpty()
        Dim cells = CollageTemplates.Grid(2, 2).CreateCells()

        Assert.AreEqual(4, cells.Count)
        Assert.IsTrue(cells.All(Function(c) c.PhotoId Is Nothing AndAlso c.Crop.Scale = 1.0F))
        Assert.AreEqual(New RectangleF(0.5F, 0.5F, 0.5F, 0.5F), cells(3).Bounds)
    End Sub

End Class

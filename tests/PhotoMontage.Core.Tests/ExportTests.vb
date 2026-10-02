Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class ExportPlannerTests

    <TestMethod>
    Public Sub ComputeOutputSize_FollowsCanvasAspect()
        Assert.AreEqual(New Size(2048, 2048), ExportPlanner.ComputeOutputSize(1, 2048))
        Assert.AreEqual(New Size(1080, 1350), ExportPlanner.ComputeOutputSize(4 / 5.0, 1350))
        Assert.AreEqual(New Size(3840, 2160), ExportPlanner.ComputeOutputSize(16 / 9.0, 3840))
        Assert.AreEqual(New Size(2480, 3508), ExportPlanner.ComputeOutputSize(210 / 297.0, 3508), "A4 300 dpi")
        Assert.AreEqual(New Size(100, 100), ExportPlanner.ComputeOutputSize(0, 100), "無效比例視為正方形")
    End Sub

    <TestMethod>
    Public Sub RequiredDecodeEdge_SmallCellNeedsSmallDecode()
        ' 4800 萬像素照片（8000×6000）放進 500×500 的格子：cover 倍率 1/12 → 長邊約 700
        Dim edge = ExportPlanner.RequiredDecodeEdge(New Size(8000, 6000), New SizeF(500, 500), New CropInfo())
        Assert.IsTrue(edge >= 667 AndAlso edge <= 720, $"edge = {edge}")
    End Sub

    <TestMethod>
    Public Sub RequiredDecodeEdge_ZoomNeedsMorePixels()
        Dim normal = ExportPlanner.RequiredDecodeEdge(New Size(8000, 6000), New SizeF(500, 500), New CropInfo())
        Dim zoomed = ExportPlanner.RequiredDecodeEdge(New Size(8000, 6000), New SizeF(500, 500), New CropInfo With {.Scale = 3})
        Assert.IsTrue(zoomed > normal * 2.9, $"{zoomed} vs {normal}")
    End Sub

    <TestMethod>
    Public Sub RequiredDecodeEdge_NeverExceedsOriginal()
        Assert.AreEqual(4000, ExportPlanner.RequiredDecodeEdge(New Size(4000, 3000), New SizeF(6000, 6000), New CropInfo With {.Scale = 5}))
    End Sub

    <TestMethod>
    Public Sub RequiredDecodeEdge_UnknownPhotoSizeUsesCellSize()
        Assert.AreEqual(1600, ExportPlanner.RequiredDecodeEdge(Size.Empty, New SizeF(800, 600), New CropInfo()))
    End Sub

    <TestMethod>
    Public Sub ValidateOutputSize_RejectsAboveLimit()
        Assert.IsNull(ExportPlanner.ValidateOutputSize(New Size(8000, 8000)))
        Assert.IsNotNull(ExportPlanner.ValidateOutputSize(New Size(8001, 8000)))
        Assert.IsNotNull(ExportPlanner.ValidateOutputSize(New Size(0, 10)))
    End Sub

    <TestMethod>
    Public Sub EstimatePeakBytes_IncludesOutputAndLargestPhoto()
        Dim bytes = ExportPlanner.EstimatePeakBytes(New Size(1000, 1000), 500)
        Assert.AreEqual(3_000_000L + 500L * 500 * 8, bytes)
        Assert.IsTrue(ExportPlanner.EstimatePeakBytes(New Size(1000, 1000), 500, hasBackground:=True) > bytes)
    End Sub

    <TestMethod>
    Public Sub FileNames_UseFormatExtension()
        Assert.AreEqual("蒙太奇_20241002_153000.png", ExportPlanner.DefaultFileName(ExportFormat.Png, New Date(2024, 10, 2, 15, 30, 0)))
        Assert.AreEqual("C:\a\b.png", ExportPlanner.EnsureExtension("C:\a\b.jpg", ExportFormat.Png))
        Assert.AreEqual("C:\a\b.JPEG", ExportPlanner.EnsureExtension("C:\a\b.JPEG", ExportFormat.Jpeg))
        Assert.AreEqual("C:\a\b.jpg", ExportPlanner.EnsureExtension("C:\a\b", ExportFormat.Jpeg))
    End Sub

    <TestMethod>
    Public Sub Presets_HaveCustomEntryAndPrintDpi()
        Assert.IsTrue(ExportPreset.All.Any(Function(p) p.IsCustom))
        Assert.IsTrue(ExportPreset.All.Where(Function(p) p.Name.Contains("列印")).All(Function(p) p.Dpi = 300))
    End Sub

End Class

<TestClass>
Public Class CollageExporterPlanTests

    <TestMethod>
    Public Sub PlanDecodeEdges_TakesLargestCellForRepeatedPhoto()
        Dim p As New MontageProject()
        Dim photo As New PhotoAsset("C:\a.jpg") With {.Id = "a", .Status = PhotoStatus.Ready, .PixelSize = New Size(8000, 6000)}
        p.Photos.Add(photo)
        p.Collage.Gap = 0
        p.Collage.Cells = New List(Of Cell) From {
            New Cell With {.Bounds = New RectangleF(0, 0, 0.25F, 1), .PhotoId = "a"},
            New Cell With {.Bounds = New RectangleF(0.25F, 0, 0.75F, 1), .PhotoId = "a"}}

        Dim edges = CollageExporter.PlanDecodeEdges(p, New RectangleF(0, 0, 2000, 1000))

        Dim big = ExportPlanner.RequiredDecodeEdge(photo.PixelSize, New SizeF(1500, 1000), New CropInfo())
        Assert.AreEqual(big, edges("a"))
    End Sub

    <TestMethod>
    Public Sub PlanDecodeEdges_SkipsEmptyAndUnknownCells()
        Dim p As New MontageProject()
        p.Collage.Cells = New List(Of Cell) From {
            New Cell With {.Bounds = New RectangleF(0, 0, 0.5F, 1)},
            New Cell With {.Bounds = New RectangleF(0.5F, 0, 0.5F, 1), .PhotoId = "removed"}}
        Assert.AreEqual(0, CollageExporter.PlanDecodeEdges(p, New RectangleF(0, 0, 100, 100)).Count)
    End Sub

End Class

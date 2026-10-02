Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class DesignStateTests

    Friend Shared Function SampleProject() As MontageProject
        Dim p As New MontageProject With {
            .CanvasSize = New Size(1600, 2000),
            .BackgroundColor = Color.FromArgb(255, 10, 20, 30),
            .BackgroundImagePath = "C:\bg.jpg"
        }
        p.Collage.TemplateId = "grid-2x2"
        p.Collage.Gap = 0.03F
        p.Collage.CornerRadius = 0.2F
        p.Collage.Cells = CollageTemplates.Grid(2, 2).CreateCells()
        p.Collage.Cells(1).PhotoId = "photo1"
        p.Collage.Cells(1).Crop = New CropInfo With {.OffsetX = -0.5F, .OffsetY = 0.25F, .Scale = 2.5F}
        p.Texts.Add(New TextLayer With {
            .Text = "旅行" & vbLf & "2024", .FontFamily = "Arial", .FontSize = 0.1F, .Bold = True, .Italic = True,
            .Color = Color.Gold, .Alignment = StringAlignment.Far, .OutlineWidth = 0.05F, .OutlineColor = Color.Navy,
            .ShadowEnabled = True, .ShadowColor = Color.FromArgb(100, 1, 2, 3), .ShadowOffset = New PointF(0.1F, 0.2F),
            .Position = New PointF(0.3F, 0.7F), .Rotation = -15})
        Return p
    End Function

    <TestMethod>
    Public Sub RoundTrip_PreservesEverything()
        Dim original = SampleProject()
        Dim json = DesignState.Capture(original).ToJson()

        Dim restored As New MontageProject()
        DesignState.FromJson(json).ApplyTo(restored)

        Assert.AreEqual(json, DesignState.Capture(restored).ToJson())
        Assert.AreEqual(New Size(1600, 2000), restored.CanvasSize)
        Assert.AreEqual(Color.FromArgb(255, 10, 20, 30).ToArgb(), restored.BackgroundColor.ToArgb())
        Assert.AreEqual(2.5F, restored.Collage.Cells(1).Crop.Scale)
        Assert.AreEqual("旅行" & vbLf & "2024", restored.Texts(0).Text)
        Assert.AreEqual(StringAlignment.Far, restored.Texts(0).Alignment)
        Assert.AreEqual(original.Texts(0).Id, restored.Texts(0).Id)
    End Sub

    <TestMethod>
    Public Sub ApplyTo_DoesNotTouchPhotoList()
        Dim target As New MontageProject()
        target.Photos.Add(New PhotoAsset("C:\a.jpg"))
        DesignState.Capture(SampleProject()).ApplyTo(target)
        Assert.AreEqual(1, target.Photos.Count)
    End Sub

    <TestMethod>
    Public Sub ApplyTo_CreatesIndependentCopies()
        Dim source = SampleProject()
        Dim state = DesignState.Capture(source)
        Dim a As New MontageProject(), b As New MontageProject()
        state.ApplyTo(a)
        state.ApplyTo(b)
        a.Collage.Cells(0).Crop.Scale = 4
        a.Texts(0).Text = "changed"
        Assert.AreEqual(1.0F, b.Collage.Cells(0).Crop.Scale)
        Assert.AreNotEqual("changed", b.Texts(0).Text)
    End Sub

End Class

<TestClass>
Public Class UndoHistoryTests

    Private _now As Date
    Private _history As UndoHistory
    Private _project As MontageProject

    <TestInitialize>
    Public Sub Setup()
        _now = New Date(2024, 1, 1)
        _history = New UndoHistory(clock:=Function() _now)
        _project = New MontageProject()
    End Sub

    Private Sub Change(Optional key As String = Nothing, Optional gap As Single = -1)
        _history.Record(_project, key)
        _project.Collage.Gap = If(gap >= 0, gap, _project.Collage.Gap + 0.01F)
    End Sub

    <TestMethod>
    Public Sub UndoAndRedo_RestorePreviousStates()
        _project.Collage.Gap = 0
        Change(gap:=0.01F)
        Change(gap:=0.02F)

        Assert.IsTrue(_history.Undo(_project))
        Assert.AreEqual(0.01F, _project.Collage.Gap)
        Assert.IsTrue(_history.Undo(_project))
        Assert.AreEqual(0F, _project.Collage.Gap)
        Assert.IsFalse(_history.Undo(_project))

        Assert.IsTrue(_history.Redo(_project))
        Assert.IsTrue(_history.Redo(_project))
        Assert.AreEqual(0.02F, _project.Collage.Gap)
        Assert.IsFalse(_history.CanRedo)
    End Sub

    <TestMethod>
    Public Sub Record_ClearsRedo()
        Change()
        _history.Undo(_project)
        Assert.IsTrue(_history.CanRedo)
        Change()
        Assert.IsFalse(_history.CanRedo)
    End Sub

    <TestMethod>
    Public Sub Record_CoalescesSameKeyWithinWindow()
        _project.Collage.Gap = 0
        For i = 1 To 10
            Change("slider", gap:=i / 100.0F)
            _now = _now.AddMilliseconds(200)
        Next

        _history.Undo(_project)
        Assert.AreEqual(0F, _project.Collage.Gap, "整段拖曳只算一步")
        Assert.IsFalse(_history.CanUndo)
    End Sub

    <TestMethod>
    Public Sub Record_DoesNotCoalesceAfterPauseOrDifferentKey()
        _project.Collage.Gap = 0
        Change("slider", gap:=0.01F)
        _now = _now.AddSeconds(5)
        Change("slider", gap:=0.02F)
        Change("other", gap:=0.03F)

        _history.Undo(_project)
        Assert.AreEqual(0.02F, _project.Collage.Gap)
        _history.Undo(_project)
        Assert.AreEqual(0.01F, _project.Collage.Gap)
    End Sub

    <TestMethod>
    Public Sub Undo_SkipsRecordsThatChangedNothing()
        _project.Collage.Gap = 0
        Change(gap:=0.05F)
        _history.Record(_project) ' 例如點了一下但沒拖動
        Assert.IsTrue(_history.Undo(_project))
        Assert.AreEqual(0F, _project.Collage.Gap, "一次 Undo 就回到真正改變之前")
    End Sub

    <TestMethod>
    Public Sub Capacity_DropsOldestStates()
        Dim history As New UndoHistory(capacity:=3, clock:=Function() _now)
        _project.Collage.Gap = 0
        For i = 1 To 5
            history.Record(_project)
            _project.Collage.Gap = i / 100.0F
        Next
        Dim undone = 0
        While history.Undo(_project)
            undone += 1
        End While
        Assert.AreEqual(3, undone)
        Assert.AreEqual(0.02F, _project.Collage.Gap)
    End Sub

    <TestMethod>
    Public Sub Changed_IsRaised()
        Dim count = 0
        AddHandler _history.Changed, Sub(s, e) count += 1
        Change()
        _history.Undo(_project)
        Assert.AreEqual(2, count)
    End Sub

End Class

<TestClass>
Public Class TextFrameTests

    Private Const Tolerance As Single = 0.01F

    <TestMethod>
    Public Sub Contains_RespectsRotation()
        Dim frame As New TextFrame(New PointF(100, 100), New SizeF(200, 20), 90)
        Assert.IsTrue(frame.Contains(New PointF(100, 190)), "旋轉 90 度後變直的")
        Assert.IsFalse(frame.Contains(New PointF(190, 100)))
        Assert.IsTrue(frame.Contains(New PointF(115, 100), padding:=6))
    End Sub

    <TestMethod>
    Public Sub GetRotateHandle_FollowsRotation()
        Dim upright As New TextFrame(New PointF(0, 0), New SizeF(100, 40), 0)
        Dim h = upright.GetRotateHandle(10)
        Assert.AreEqual(0F, h.X, Tolerance)
        Assert.AreEqual(-30F, h.Y, Tolerance)

        Dim rotated As New TextFrame(New PointF(0, 0), New SizeF(100, 40), 90)
        h = rotated.GetRotateHandle(10)
        Assert.AreEqual(30F, h.X, Tolerance, "順時針 90 度後控制點在右邊")
        Assert.AreEqual(0F, h.Y, Tolerance)
    End Sub

    <TestMethod>
    Public Sub GetCorners_AreSymmetric()
        Dim c = New TextFrame(New PointF(50, 50), New SizeF(20, 10), 0).GetCorners()
        Assert.AreEqual(New PointF(40, 45), c(0))
        Assert.AreEqual(New PointF(60, 55), c(2))
    End Sub

    <TestMethod>
    Public Sub AngleFromCenter_UpIsZeroClockwisePositive()
        Dim c As New PointF(0, 0)
        Assert.AreEqual(0F, TextFrame.AngleFromCenter(c, New PointF(0, -10)), Tolerance)
        Assert.AreEqual(90F, TextFrame.AngleFromCenter(c, New PointF(10, 0)), Tolerance)
        Assert.AreEqual(-90F, TextFrame.AngleFromCenter(c, New PointF(-10, 0)), Tolerance)
        Assert.AreEqual(180F, Math.Abs(TextFrame.AngleFromCenter(c, New PointF(0, 10))), Tolerance)
    End Sub

    <TestMethod>
    Public Sub NormalizeAngle_SnapsAndWraps()
        Assert.AreEqual(15F, TextFrame.NormalizeAngle(17, 15))
        Assert.AreEqual(-170F, TextFrame.NormalizeAngle(190))
        Assert.AreEqual(180F, TextFrame.NormalizeAngle(-180))
        Assert.AreEqual(0F, TextFrame.NormalizeAngle(358, 15))
    End Sub

End Class

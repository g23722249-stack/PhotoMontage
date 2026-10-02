Imports System.Drawing

''' <summary>對齊吸附的結果：建議的位移與要顯示的參考線（畫布座標）。</summary>
Public NotInheritable Class SnapResult
    Public ReadOnly Property Offset As PointF
    Public ReadOnly Property VerticalGuides As IReadOnlyList(Of Single)
    Public ReadOnly Property HorizontalGuides As IReadOnlyList(Of Single)

    Public Sub New(offset As PointF, verticalGuides As IReadOnlyList(Of Single), horizontalGuides As IReadOnlyList(Of Single))
        Me.Offset = offset
        Me.VerticalGuides = verticalGuides
        Me.HorizontalGuides = horizontalGuides
    End Sub

    Public Shared ReadOnly None As New SnapResult(PointF.Empty, Array.Empty(Of Single)(), Array.Empty(Of Single)())
End Class

''' <summary>移動時吸附到其他照片與畫布的邊緣、中心。</summary>
Public Module FreeSnapping

    ''' <param name="moving">移動中的包圍框（已套用滑鼠位移）。</param>
    ''' <param name="others">其他照片的包圍框。</param>
    ''' <param name="canvas">畫布範圍（其邊緣與中線也是吸附目標）。</param>
    ''' <param name="threshold">吸附距離（像素）。</param>
    Public Function Snap(moving As RectangleF, others As IEnumerable(Of RectangleF), canvas As RectangleF, threshold As Single) As SnapResult
        Dim targets = others.Concat({canvas}).ToList()
        Dim xTargets = targets.SelectMany(Function(r) {r.Left, r.Left + r.Width / 2, r.Right}).ToList()
        Dim yTargets = targets.SelectMany(Function(r) {r.Top, r.Top + r.Height / 2, r.Bottom}).ToList()
        Dim xMoving = {moving.Left, moving.Left + moving.Width / 2, moving.Right}
        Dim yMoving = {moving.Top, moving.Top + moving.Height / 2, moving.Bottom}

        Dim dx = BestOffset(xMoving, xTargets, threshold)
        Dim dy = BestOffset(yMoving, yTargets, threshold)

        Dim vGuides = If(dx.HasValue, Guides(xMoving, dx.Value, xTargets), New List(Of Single))
        Dim hGuides = If(dy.HasValue, Guides(yMoving, dy.Value, yTargets), New List(Of Single))
        Return New SnapResult(New PointF(If(dx, 0F), If(dy, 0F)), vGuides, hGuides)
    End Function

    ''' <summary>所有（候選, 目標）中距離最小且在門檻內的位移；沒有時回傳 Nothing。</summary>
    Private Function BestOffset(candidates As Single(), targets As List(Of Single), threshold As Single) As Single?
        Dim best As Single? = Nothing
        For Each c In candidates
            For Each t In targets
                Dim d = t - c
                If Math.Abs(d) <= threshold AndAlso (Not best.HasValue OrElse Math.Abs(d) < Math.Abs(best.Value)) Then best = d
            Next
        Next
        Return best
    End Function

    ''' <summary>位移後與目標重合的位置（去重複）。</summary>
    Private Function Guides(candidates As Single(), offset As Single, targets As List(Of Single)) As List(Of Single)
        Dim result As New List(Of Single)
        For Each c In candidates
            Dim moved = c + offset
            For Each t In targets
                If Math.Abs(t - moved) < 0.5F AndAlso Not result.Exists(Function(g) Math.Abs(g - t) < 0.5F) Then result.Add(t)
            Next
        Next
        Return result
    End Function

End Module

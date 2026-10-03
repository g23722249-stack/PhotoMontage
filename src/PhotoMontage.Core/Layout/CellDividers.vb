Imports System.Drawing

''' <summary>
''' 格子之間的分隔線：同一條線上、彼此相連的格子邊緣。拖曳分隔線時，
''' 一側格子的右（下）緣與另一側格子的左（上）緣一起移動，間距不變。
''' 座標為 0~1 相對值。
''' </summary>
Public NotInheritable Class CellDivider
    ''' <summary>True 為垂直線（左右拖曳），False 為水平線（上下拖曳）。</summary>
    Public Property Vertical As Boolean
    ''' <summary>線的位置（垂直線為 X，水平線為 Y）。</summary>
    Public Property Position As Single
    ''' <summary>線段在另一個方向上的範圍。</summary>
    Public Property SpanStart As Single
    Public Property SpanEnd As Single
    ''' <summary>線左（上）側的格子索引。</summary>
    Public Property Before As New List(Of Integer)
    ''' <summary>線右（下）側的格子索引。</summary>
    Public Property After As New List(Of Integer)
End Class

Public Module CellDividers

    Private Const Epsilon As Single = 0.002F

    ''' <summary>格子的最小寬高（畫布的比例）。</summary>
    Public Const MinCellSize As Single = 0.05F

    ''' <summary>找出所有可拖曳的分隔線（畫布外緣不算）。</summary>
    Public Function FindAll(cells As IReadOnlyList(Of RectangleF)) As List(Of CellDivider)
        Dim result As New List(Of CellDivider)
        result.AddRange(FindAll(cells, vertical:=True))
        result.AddRange(FindAll(cells, vertical:=False))
        Return result
    End Function

    Private Function FindAll(cells As IReadOnlyList(Of RectangleF), vertical As Boolean) As List(Of CellDivider)
        Dim result As New List(Of CellDivider)
        Dim far = Function(r As RectangleF) If(vertical, r.Right, r.Bottom)
        Dim near = Function(r As RectangleF) If(vertical, r.Left, r.Top)
        Dim spanStart = Function(r As RectangleF) If(vertical, r.Top, r.Left)
        Dim spanEnd = Function(r As RectangleF) If(vertical, r.Bottom, r.Right)

        ' 候選位置：每個格子的右（下）緣，去掉畫布外緣與重複
        Dim positions As New List(Of Single)
        For Each r In cells
            Dim p = far(r)
            If p >= 1 - Epsilon Then Continue For
            If Not positions.Exists(Function(x) Math.Abs(x - p) < Epsilon) Then positions.Add(p)
        Next

        For Each pos In positions
            ' 線上的每段邊緣：(起點, 終點, 格子, 在線前方?)
            Dim edges As New List(Of (Start As Single, [End] As Single, Index As Integer, IsBefore As Boolean))
            For i = 0 To cells.Count - 1
                Dim r = cells(i)
                If Math.Abs(far(r) - pos) < Epsilon Then edges.Add((spanStart(r), spanEnd(r), i, True))
                If Math.Abs(near(r) - pos) < Epsilon Then edges.Add((spanStart(r), spanEnd(r), i, False))
            Next
            edges.Sort(Function(a, b) a.Start.CompareTo(b.Start))

            ' 相接或重疊的邊緣連成同一條分隔線
            Dim current As CellDivider = Nothing
            For Each edge In edges
                If current Is Nothing OrElse edge.Start > current.SpanEnd + Epsilon Then
                    If current IsNot Nothing AndAlso current.Before.Count > 0 AndAlso current.After.Count > 0 Then result.Add(current)
                    current = New CellDivider With {.Vertical = vertical, .Position = pos, .SpanStart = edge.Start, .SpanEnd = edge.End}
                End If
                current.SpanEnd = Math.Max(current.SpanEnd, edge.End)
                If edge.IsBefore Then current.Before.Add(edge.Index) Else current.After.Add(edge.Index)
            Next
            If current IsNot Nothing AndAlso current.Before.Count > 0 AndAlso current.After.Count > 0 Then result.Add(current)
        Next
        Return result
    End Function

    ''' <summary>分隔線可移動的範圍：兩側每個格子都保留 <see cref="MinCellSize"/>。</summary>
    Public Function GetRange(cells As IReadOnlyList(Of RectangleF), divider As CellDivider) As (Min As Single, Max As Single)
        Dim min = 0F, max = 1.0F
        For Each i In divider.Before
            min = Math.Max(min, If(divider.Vertical, cells(i).Left, cells(i).Top) + MinCellSize)
        Next
        For Each i In divider.After
            max = Math.Min(max, If(divider.Vertical, cells(i).Right, cells(i).Bottom) - MinCellSize)
        Next
        Return (min, Math.Max(min, max))
    End Function

    ''' <summary>
    ''' 把分隔線移到 <paramref name="position"/>（限制在可移動範圍內），更新兩側格子；回傳實際位置。
    ''' </summary>
    Public Function Move(cells As IList(Of Cell), divider As CellDivider, position As Single) As Single
        Dim rects = cells.Select(Function(c) c.Bounds).ToList()
        Dim range = GetRange(rects, divider)
        Dim pos = Math.Max(range.Min, Math.Min(range.Max, position))
        For Each i In divider.Before
            Dim r = cells(i).Bounds
            cells(i).Bounds = If(divider.Vertical, RectangleF.FromLTRB(r.Left, r.Top, pos, r.Bottom), RectangleF.FromLTRB(r.Left, r.Top, r.Right, pos))
        Next
        For Each i In divider.After
            Dim r = cells(i).Bounds
            cells(i).Bounds = If(divider.Vertical, RectangleF.FromLTRB(pos, r.Top, r.Right, r.Bottom), RectangleF.FromLTRB(r.Left, pos, r.Right, r.Bottom))
        Next
        divider.Position = pos
        Return pos
    End Function

End Module

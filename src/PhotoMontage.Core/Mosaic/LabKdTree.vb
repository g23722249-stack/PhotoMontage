''' <summary>以 Lab 平均色為鍵的 3 維 k-d tree，用來快速找出色彩最接近的候選素材。</summary>
Public NotInheritable Class LabKdTree

    Private ReadOnly _points As LabColor()
    Private ReadOnly _nodes As Integer()   ' 依樹的順序排列的點索引（隱式平衡樹）

    Public Sub New(points As IReadOnlyList(Of LabColor))
        _points = points.ToArray()
        _nodes = Enumerable.Range(0, _points.Length).ToArray()
        Build(0, _nodes.Length, 0)
    End Sub

    Public ReadOnly Property Count As Integer
        Get
            Return _points.Length
        End Get
    End Property

    Private Shared Function Coord(p As LabColor, axis As Integer) As Single
        Return If(axis = 0, p.L, If(axis = 1, p.A, p.B))
    End Function

    Private Sub Build(lo As Integer, hi As Integer, depth As Integer)
        If hi - lo <= 1 Then Return
        Dim axis = depth Mod 3
        Array.Sort(_nodes, lo, hi - lo, Comparer(Of Integer).Create(
            Function(x, y) Coord(_points(x), axis).CompareTo(Coord(_points(y), axis))))
        Dim mid = (lo + hi) \ 2
        Build(lo, mid, depth + 1)
        Build(mid + 1, hi, depth + 1)
    End Sub

    ''' <summary>距離 <paramref name="target"/> 最近的 k 個點索引（由近到遠）。</summary>
    Public Function Nearest(target As LabColor, k As Integer) As List(Of Integer)
        k = Math.Min(k, _points.Length)
        Dim best As New List(Of (Index As Integer, Dist As Single))(k + 1)
        If k > 0 Then Search(0, _nodes.Length, 0, target, k, best)
        Return best.Select(Function(b) b.Index).ToList()
    End Function

    Private Sub Search(lo As Integer, hi As Integer, depth As Integer, target As LabColor, k As Integer, best As List(Of (Index As Integer, Dist As Single)))
        If lo >= hi Then Return
        Dim mid = (lo + hi) \ 2
        Dim index = _nodes(mid)
        Insert(best, k, index, LabColor.DistanceSquared(_points(index), target))

        Dim axis = depth Mod 3
        Dim diff = Coord(target, axis) - Coord(_points(index), axis)
        Dim nearLo, nearHi, farLo, farHi As Integer
        If diff < 0 Then
            nearLo = lo : nearHi = mid : farLo = mid + 1 : farHi = hi
        Else
            nearLo = mid + 1 : nearHi = hi : farLo = lo : farHi = mid
        End If
        Search(nearLo, nearHi, depth + 1, target, k, best)
        If best.Count < k OrElse diff * diff < best(best.Count - 1).Dist Then
            Search(farLo, farHi, depth + 1, target, k, best)
        End If
    End Sub

    Private Shared Sub Insert(best As List(Of (Index As Integer, Dist As Single)), k As Integer, index As Integer, dist As Single)
        If best.Count = k AndAlso dist >= best(k - 1).Dist Then Return
        Dim pos = best.Count
        While pos > 0 AndAlso best(pos - 1).Dist > dist
            pos -= 1
        End While
        best.Insert(pos, (index, dist))
        If best.Count > k Then best.RemoveAt(k)
    End Sub

End Class

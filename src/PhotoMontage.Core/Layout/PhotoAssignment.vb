Imports System.Drawing

''' <summary>把照片分配到拼貼格子。橫圖優先放橫格、直圖放直格，減少裁切。</summary>
Public Module PhotoAssignment

    ''' <summary>照片以 cover 方式放進格子時被裁掉的面積比例（0 = 完全不裁）。</summary>
    Public Function CropLoss(photoAspect As Double, cellAspect As Double) As Double
        If photoAspect <= 0 OrElse cellAspect <= 0 Then Return 0
        Return 1 - Math.Min(photoAspect / cellAspect, cellAspect / photoAspect)
    End Function

    ''' <summary>清空所有格子後重新分配（依照片順序取前 N 張）。</summary>
    Public Sub AssignAll(cells As IList(Of Cell), photos As IEnumerable(Of PhotoAsset), canvasSize As SizeF)
        For Each c In cells
            c.PhotoId = Nothing
            c.Crop = New CropInfo()
        Next
        FillEmpty(cells, photos, canvasSize)
    End Sub

    ''' <summary>把還沒放進格子的照片（依順序）填入空格，已放好的格子不動。</summary>
    Public Sub FillEmpty(cells As IList(Of Cell), photos As IEnumerable(Of PhotoAsset), canvasSize As SizeF)
        Dim used = New HashSet(Of String)(cells.Where(Function(c) c.PhotoId IsNot Nothing).Select(Function(c) c.PhotoId))
        Dim empty = Enumerable.Range(0, cells.Count).Where(Function(i) cells(i).PhotoId Is Nothing).ToList()
        If empty.Count = 0 Then Return

        Dim candidates = photos.Where(Function(p) p.Status = PhotoStatus.Ready AndAlso Not used.Contains(p.Id)).Take(empty.Count).ToList()
        If candidates.Count = 0 Then Return

        ' 總裁切損失最小的配對（匈牙利演算法）；損失相同時以微小權重偏好「第 k 張照片放第 k 個空格」
        Dim cost(candidates.Count - 1, empty.Count - 1) As Double
        For pi = 0 To candidates.Count - 1
            For k = 0 To empty.Count - 1
                Dim cellAspect = CellGeometry.GetCellAspect(cells(empty(k)).Bounds, canvasSize)
                cost(pi, k) = Math.Round(CropLoss(candidates(pi).AspectRatio, cellAspect), 3) + 0.000001 * Math.Abs(pi - k)
            Next
        Next

        Dim match = SolveAssignment(cost)
        For pi = 0 To candidates.Count - 1
            Dim c = cells(empty(match(pi)))
            c.PhotoId = candidates(pi).Id
            c.Crop = New CropInfo()
        Next
    End Sub

    ''' <summary>
    ''' 最小成本指派（匈牙利演算法，O(n²m)）。cost 為 n × m 且 n ≤ m；回傳每一列配到的欄。
    ''' </summary>
    Friend Function SolveAssignment(cost As Double(,)) As Integer()
        Dim n = cost.GetLength(0), m = cost.GetLength(1)
        If n > m Then Throw New ArgumentException("列數不可多於欄數。", NameOf(cost))

        ' 1-based；u/v 為對偶變數，p(j) 為欄 j 配到的列，way 用來回溯增廣路徑
        Dim u(n) As Double, v(m) As Double
        Dim p(m) As Integer, way(m) As Integer
        For i = 1 To n
            p(0) = i
            Dim j0 = 0
            Dim minv(m) As Double
            Dim used(m) As Boolean
            For j = 1 To m
                minv(j) = Double.MaxValue
            Next
            Do
                used(j0) = True
                Dim i0 = p(j0), delta = Double.MaxValue, j1 = 0
                For j = 1 To m
                    If used(j) Then Continue For
                    Dim cur = cost(i0 - 1, j - 1) - u(i0) - v(j)
                    If cur < minv(j) Then minv(j) = cur : way(j) = j0
                    If minv(j) < delta Then delta = minv(j) : j1 = j
                Next
                For j = 0 To m
                    If used(j) Then
                        u(p(j)) += delta
                        v(j) -= delta
                    Else
                        minv(j) -= delta
                    End If
                Next
                j0 = j1
            Loop While p(j0) <> 0
            Do
                Dim j1 = way(j0)
                p(j0) = p(j1)
                j0 = j1
            Loop While j0 <> 0
        Next

        Dim result(n - 1) As Integer
        For j = 1 To m
            If p(j) > 0 Then result(p(j) - 1) = j - 1
        Next
        Return result
    End Function

    ''' <summary>交換兩格的照片並重設取景；照片的旋轉與翻轉跟著照片走。</summary>
    Public Sub Swap(a As Cell, b As Cell)
        Dim id = a.PhotoId
        a.PhotoId = b.PhotoId
        b.PhotoId = id
        Dim oldA = a.Crop
        a.Crop = New CropInfo With {.Rotation = b.Crop.Rotation, .FlipHorizontal = b.Crop.FlipHorizontal}
        b.Crop = New CropInfo With {.Rotation = oldA.Rotation, .FlipHorizontal = oldA.FlipHorizontal}
    End Sub

    ''' <summary>把照片放進指定格子；照片原本在別格時兩格互換。</summary>
    Public Sub Place(cells As IList(Of Cell), targetIndex As Integer, photoId As String)
        Dim current = -1
        For i = 0 To cells.Count - 1
            If cells(i).PhotoId = photoId Then current = i : Exit For
        Next
        If current = targetIndex Then Return

        If current >= 0 Then
            Swap(cells(current), cells(targetIndex))
        Else
            cells(targetIndex).PhotoId = photoId
            cells(targetIndex).Crop = New CropInfo()
        End If
    End Sub

End Module

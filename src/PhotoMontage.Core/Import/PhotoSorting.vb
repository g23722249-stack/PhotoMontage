''' <summary>照片清單的排序方式。</summary>
Public Module PhotoSorting

    ''' <summary>依檔名自然排序（IMG_2 排在 IMG_10 前面）。</summary>
    Public Function ByFileName(a As PhotoAsset, b As PhotoAsset) As Integer
        Return NaturalCompare(IO.Path.GetFileName(a.FilePath), IO.Path.GetFileName(b.FilePath))
    End Function

    ''' <summary>依拍攝時間；沒有拍攝時間的排最後，同時間再依檔名。</summary>
    Public Function ByDateTaken(a As PhotoAsset, b As PhotoAsset) As Integer
        If a.DateTaken.HasValue AndAlso b.DateTaken.HasValue Then
            Dim c = a.DateTaken.Value.CompareTo(b.DateTaken.Value)
            If c <> 0 Then Return c
        ElseIf a.DateTaken.HasValue Then
            Return -1
        ElseIf b.DateTaken.HasValue Then
            Return 1
        End If
        Return ByFileName(a, b)
    End Function

    ''' <summary>穩定排序（相同鍵值維持原順序）。</summary>
    Public Function StableSort(photos As IEnumerable(Of PhotoAsset), comparison As Comparison(Of PhotoAsset)) As List(Of PhotoAsset)
        Return photos.Select(Function(p, i) (p, i)).
            OrderBy(Function(x) x, Comparer(Of (PhotoAsset, Integer)).Create(
                Function(x, y)
                    Dim c = comparison(x.Item1, y.Item1)
                    Return If(c <> 0, c, x.Item2.CompareTo(y.Item2))
                End Function)).
            Select(Function(x) x.Item1).
            ToList()
    End Function

    ''' <summary>不分大小寫、數字段落依數值比較的字串比較。</summary>
    Public Function NaturalCompare(a As String, b As String) As Integer
        If a Is Nothing Then Return If(b Is Nothing, 0, -1)
        If b Is Nothing Then Return 1

        Dim i = 0, j = 0
        While i < a.Length AndAlso j < b.Length
            If Char.IsDigit(a(i)) AndAlso Char.IsDigit(b(j)) Then
                Dim si = i, sj = j
                While i < a.Length AndAlso Char.IsDigit(a(i)) : i += 1 : End While
                While j < b.Length AndAlso Char.IsDigit(b(j)) : j += 1 : End While
                Dim na = a.Substring(si, i - si).TrimStart("0"c)
                Dim nb = b.Substring(sj, j - sj).TrimStart("0"c)
                If na.Length <> nb.Length Then Return na.Length.CompareTo(nb.Length)
                Dim c = String.CompareOrdinal(na, nb)
                If c <> 0 Then Return c
            Else
                Dim c = String.Compare(a(i).ToString(), b(j).ToString(), StringComparison.CurrentCultureIgnoreCase)
                If c <> 0 Then Return c
                i += 1
                j += 1
            End If
        End While
        Return (a.Length - i).CompareTo(b.Length - j)
    End Function

End Module

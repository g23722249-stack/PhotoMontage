''' <summary>配對選項。</summary>
Public Class MosaicMatchOptions
    ''' <summary>同一張素材最多使用次數；0 表示不限。無法滿足時自動放寬到剛好夠用。</summary>
    Public Property MaxRepeat As Integer

    ''' <summary>
    ''' 相鄰（含斜角）格子不使用同一張素材。素材 ≥ 9 張時保證做到；更少時盡量避免，無法避免才重複。
    ''' </summary>
    Public Property AvoidAdjacentDuplicates As Boolean = True

    ''' <summary>處理順序的亂數種子；相同輸入與種子得到相同結果。</summary>
    Public Property Seed As Integer = 12345

    ''' <summary>每格先以 k-d tree 取出的候選數量。</summary>
    Public Property CandidateCount As Integer = 48
End Class

''' <summary>為主圖的每一格挑選最相近的素材。</summary>
Public Module MosaicMatcher

    ''' <summary>回傳每格使用的素材索引（逐列，長度 = cells.Count）。</summary>
    Public Function Match(cells As IReadOnlyList(Of MosaicFeature), columns As Integer,
                          tiles As IReadOnlyList(Of MosaicFeature), options As MosaicMatchOptions) As Integer()
        If tiles Is Nothing OrElse tiles.Count = 0 Then Throw New ArgumentException("沒有素材。", NameOf(tiles))
        If columns < 1 Then Throw New ArgumentOutOfRangeException(NameOf(columns))
        If options Is Nothing Then options = New MosaicMatchOptions()

        Dim n = cells.Count
        Dim maxRepeat = Integer.MaxValue
        If options.MaxRepeat > 0 Then maxRepeat = Math.Max(options.MaxRepeat, CInt(Math.Ceiling(n / tiles.Count)))

        Dim tree As New LabKdTree(tiles.Select(Function(t) t.Average).ToList())
        Dim usage(tiles.Count - 1) As Integer
        Dim result = Enumerable.Repeat(-1, n).ToArray()
        Dim k = Math.Max(1, Math.Min(options.CandidateCount, tiles.Count))

        ' 打散處理順序：有使用次數限制時，好的素材不會全被畫面上方用掉
        Dim order = Enumerable.Range(0, n).ToArray()
        Dim rng As New Random(options.Seed)
        For i = n - 1 To 1 Step -1
            Dim j = rng.Next(i + 1)
            Dim t = order(i) : order(i) = order(j) : order(j) = t
        Next

        For Each cell In order
            Dim feature = cells(cell)
            Dim best = PickBest(tree.Nearest(feature.Average, k), feature, cell, columns, n, tiles, result, usage, maxRepeat, options.AvoidAdjacentDuplicates)
            If best < 0 Then best = PickBest(Enumerable.Range(0, tiles.Count), feature, cell, columns, n, tiles, result, usage, maxRepeat, options.AvoidAdjacentDuplicates)
            If best < 0 Then best = PickBest(Enumerable.Range(0, tiles.Count), feature, cell, columns, n, tiles, result, usage, maxRepeat, False)
            If best < 0 Then best = PickBest(Enumerable.Range(0, tiles.Count), feature, cell, columns, n, tiles, result, usage, Integer.MaxValue, False)
            result(cell) = best
            usage(best) += 1
        Next
        Return result
    End Function

    ''' <summary>
    ''' 「換成下一個相近的素材」：依相似度排序，回傳排在目前素材之後、且不與鄰格重複的第一個素材（到尾端時從頭開始）。
    ''' </summary>
    Public Function NextAlternative(cell As Integer, current As Integer, cells As IReadOnlyList(Of MosaicFeature), columns As Integer,
                                    tiles As IReadOnlyList(Of MosaicFeature), assignment As IReadOnlyList(Of Integer)) As Integer
        If tiles.Count <= 1 Then Return current
        Dim feature = cells(cell)
        Dim ranked = Enumerable.Range(0, tiles.Count).OrderBy(Function(t) tiles(t).Distance(feature)).ThenBy(Function(t) t).ToList()
        Dim start = ranked.IndexOf(current)
        For step_ = 1 To ranked.Count - 1
            Dim candidate = ranked((start + step_) Mod ranked.Count)
            If Not IsUsedByNeighbor(candidate, cell, columns, assignment.Count, assignment) Then Return candidate
        Next
        Return ranked((start + 1) Mod ranked.Count)
    End Function

    Private Function PickBest(candidates As IEnumerable(Of Integer), feature As MosaicFeature, cell As Integer, columns As Integer, n As Integer,
                              tiles As IReadOnlyList(Of MosaicFeature), result As IReadOnlyList(Of Integer), usage As Integer(),
                              maxRepeat As Integer, avoidAdjacent As Boolean) As Integer
        Dim best = -1
        Dim bestDist = Single.MaxValue
        For Each t In candidates
            If usage(t) >= maxRepeat Then Continue For
            If avoidAdjacent AndAlso IsUsedByNeighbor(t, cell, columns, n, result) Then Continue For
            Dim d = tiles(t).Distance(feature)
            If d < bestDist OrElse (d = bestDist AndAlso t < best) Then
                bestDist = d
                best = t
            End If
        Next
        Return best
    End Function

    ''' <summary>8 個相鄰格子中是否已有人使用這張素材。</summary>
    Friend Function IsUsedByNeighbor(tile As Integer, cell As Integer, columns As Integer, n As Integer, assignment As IReadOnlyList(Of Integer)) As Boolean
        Dim row = cell \ columns, col = cell Mod columns
        Dim rows = (n + columns - 1) \ columns
        For dr = -1 To 1
            For dc = -1 To 1
                If dr = 0 AndAlso dc = 0 Then Continue For
                Dim r = row + dr, c = col + dc
                If r < 0 OrElse c < 0 OrElse r >= rows OrElse c >= columns Then Continue For
                Dim i = r * columns + c
                If i < n AndAlso assignment(i) = tile Then Return True
            Next
        Next
        Return False
    End Function

End Module

Imports System.Drawing

''' <summary>
''' 依照片長寬比自動產生版型（類似 Google 相簿逐列排版）：照片依順序分成數列，
''' 同一列等高、寬度依長寬比分配，再整體縮放填滿畫布，所有照片的裁切比例都相同且很小。
''' </summary>
Public Module JustifiedLayout

    ''' <param name="aspects">照片長寬比（寬 / 高），依顯示順序；第 i 格對應第 i 張。</param>
    ''' <param name="canvasAspect">畫布長寬比（寬 / 高）。</param>
    Public Function Create(aspects As IReadOnlyList(Of Double), canvasAspect As Double) As CollageTemplate
        Dim name = "自動排版"
        If aspects Is Nothing OrElse aspects.Count = 0 Then Return New CollageTemplate(CollageTemplates.AutoId, name, Array.Empty(Of RectangleF)())
        If canvasAspect <= 0 Then canvasAspect = 1

        Dim a = aspects.Select(Function(x) If(x > 0 AndAlso Not Double.IsNaN(x), x, 1.0)).ToArray()
        Dim n = a.Length
        Dim total = a.Sum()
        ' r 列、每列總長寬比約 total/r → 總高度 ≈ r²/total；令其等於 1/canvasAspect
        Dim est = Math.Sqrt(total / canvasAspect)

        Dim bestRows As List(Of Integer()) = Nothing
        Dim bestScore = Double.MaxValue
        For r = Math.Max(1, CInt(Math.Floor(est)) - 2) To Math.Min(n, CInt(Math.Ceiling(est)) + 2)
            Dim rows = Partition(a, r)
            Dim height = rows.Sum(Function(row) 1 / row.Sum(Function(i) a(i)))
            Dim score = Math.Abs(Math.Log(height * canvasAspect))
            If score < bestScore Then
                bestScore = score
                bestRows = rows
            End If
        Next

        Return New CollageTemplate(CollageTemplates.AutoId, name, BuildCells(a, bestRows))
    End Function

    ''' <summary>把照片依順序分成 r 列，使每列總長寬比盡量平均（動態規劃）。</summary>
    Friend Function Partition(a As Double(), r As Integer) As List(Of Integer())
        Dim n = a.Length
        r = Math.Max(1, Math.Min(r, n))
        Dim prefix(n) As Double
        For i = 0 To n - 1
            prefix(i + 1) = prefix(i) + a(i)
        Next
        Dim target = prefix(n) / r

        ' cost(k, i)：前 i 張分成 k 列的最小成本
        Dim cost(r, n) As Double
        Dim split(r, n) As Integer
        For k = 0 To r
            For i = 0 To n
                cost(k, i) = Double.MaxValue
            Next
        Next
        cost(0, 0) = 0
        For k = 1 To r
            For i = k To n - (r - k)
                For j = k - 1 To i - 1
                    If cost(k - 1, j) = Double.MaxValue Then Continue For
                    Dim d = prefix(i) - prefix(j) - target
                    Dim c = cost(k - 1, j) + d * d
                    If c < cost(k, i) Then
                        cost(k, i) = c
                        split(k, i) = j
                    End If
                Next
            Next
        Next

        Dim rows As New List(Of Integer())
        Dim e = n
        For k = r To 1 Step -1
            Dim s = split(k, e)
            rows.Insert(0, Enumerable.Range(s, e - s).ToArray())
            e = s
        Next
        Return rows
    End Function

    Private Function BuildCells(a As Double(), rows As List(Of Integer())) As List(Of RectangleF)
        Dim rowHeights = rows.Select(Function(row) 1 / row.Sum(Function(i) a(i))).ToList()
        Dim totalHeight = rowHeights.Sum()
        Dim cells(a.Length - 1) As RectangleF

        Dim y = 0.0
        For r = 0 To rows.Count - 1
            Dim top = y
            y += rowHeights(r) / totalHeight
            Dim bottom = If(r = rows.Count - 1, 1.0, y)

            Dim rowSum = rows(r).Sum(Function(i) a(i))
            Dim x = 0.0
            For k = 0 To rows(r).Length - 1
                Dim i = rows(r)(k)
                Dim left = x
                x += a(i) / rowSum
                Dim right = If(k = rows(r).Length - 1, 1.0, x)
                cells(i) = RectangleF.FromLTRB(CSng(left), CSng(top), CSng(right), CSng(bottom))
            Next
        Next
        Return cells.ToList()
    End Function

End Module

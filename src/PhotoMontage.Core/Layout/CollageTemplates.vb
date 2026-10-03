Imports System.Drawing

''' <summary>內建拼貼版型。</summary>
Public Module CollageTemplates

    ''' <summary>依照片自動排版的版型 Id（見 <see cref="JustifiedLayout"/>）。</summary>
    Public Const AutoId As String = "auto"

    ''' <summary>rows × columns 等分格。</summary>
    Public Function Grid(rows As Integer, columns As Integer, Optional name As String = Nothing) As CollageTemplate
        If rows < 1 Then Throw New ArgumentOutOfRangeException(NameOf(rows))
        If columns < 1 Then Throw New ArgumentOutOfRangeException(NameOf(columns))

        Dim cells As New List(Of RectangleF)(rows * columns)
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                cells.Add(Span(c / columns, r / rows, (c + 1) / columns, (r + 1) / rows))
            Next
        Next
        Return New CollageTemplate($"grid-{rows}x{columns}", If(name, $"{rows} × {columns}"), cells)
    End Function

    Private ReadOnly _builtIn As IReadOnlyList(Of CollageTemplate) = CreateBuiltIn()

    Public ReadOnly Property BuiltIn As IReadOnlyList(Of CollageTemplate)
        Get
            Return _builtIn
        End Get
    End Property

    ''' <summary>依 Id 取得內建版型；找不到時回傳 Nothing。</summary>
    Public Function Find(id As String) As CollageTemplate
        Return _builtIn.FirstOrDefault(Function(t) t.Id = id)
    End Function

    ''' <summary>
    ''' 內建版型，依照片張數排列。已存在的 Id（grid-1x2、big-left-2、big-top-3、center-6 等）不可更改，
    ''' 舊的作品與復原記錄依 Id 找版型。
    ''' </summary>
    Private Function CreateBuiltIn() As IReadOnlyList(Of CollageTemplate)
        Dim t = Third, tt = 2 * Third
        Return New List(Of CollageTemplate) From {
            Grid(1, 2, "左右並排"),
            Grid(2, 1, "上下並排"),
            Columns("2-big-left", "左大右小", Strip(2, 1), Strip(1, 1)),
            Rows("2-big-top", "上大下小", Strip(2, 1), Strip(1, 1)),
            Grid(1, 3, "三欄"),
            Grid(3, 1, "三列"),
            Custom("big-left-2", "大圖左＋右 2", {Span(0, 0, tt, 1)}, GridIn(tt, 0, 1, 1, 2, 1)),
            Custom("3-big-right", "大圖右＋左 2", {Span(t, 0, 1, 1)}, GridIn(0, 0, t, 1, 2, 1)),
            Rows("3-big-top", "大圖上＋下 2", Strip(1.4, 1), Strip(1, 1, 1)),
            Rows("3-big-bottom", "大圖下＋上 2", Strip(1, 1, 1), Strip(1.4, 1)),
            Grid(2, 2, "田字"),
            Grid(1, 4, "四欄"),
            Grid(4, 1, "四列"),
            Custom("big-top-3", "大圖上＋下 3", {Span(0, 0, 1, tt)}, GridIn(0, tt, 1, 1, 1, 3)),
            Custom("4-big-bottom", "大圖下＋上 3", {Span(0, t, 1, 1)}, GridIn(0, 0, 1, t, 1, 3)),
            Custom("4-big-left", "大圖左＋右 3", {Span(0, 0, tt, 1)}, GridIn(tt, 0, 1, 1, 3, 1)),
            Custom("4-big-right", "大圖右＋左 3", {Span(t, 0, 1, 1)}, GridIn(0, 0, t, 1, 3, 1)),
            Rows("4-stagger", "交錯", Strip(1, 2, 1), Strip(1, 1, 2)),
            Rows("5-rows-2-3", "上 2 下 3", Strip(1, 1, 1), Strip(1, 1, 1, 1)),
            Rows("5-rows-3-2", "上 3 下 2", Strip(1, 1, 1, 1), Strip(1, 1, 1)),
            Columns("5-cols-2-3", "左 2 右 3", Strip(1, 1, 1), Strip(1, 1, 1, 1)),
            Custom("5-big-left", "大圖左＋右 4", {Span(0, 0, 0.5, 1)}, GridIn(0.5, 0, 1, 1, 2, 2)),
            Custom("5-big-top", "大圖上＋下 4", {Span(0, 0, 1, 0.6)}, GridIn(0, 0.6, 1, 1, 1, 4)),
            Custom("5-pinwheel", "風車", {Span(0, 0, tt, t), Span(tt, 0, 1, tt), Span(t, tt, 1, 1), Span(0, t, t, 1), Span(t, t, tt, tt)}),
            Grid(2, 3, "2 × 3"),
            Grid(3, 2, "3 × 2"),
            Custom("6-big-corner", "大圖左上＋周圍 5", {Span(0, 0, tt, tt)}, GridIn(tt, 0, 1, tt, 2, 1), GridIn(0, tt, 1, 1, 1, 3)),
            Custom("6-big-left", "大圖左＋右 5", {Span(0, 0, 0.5, 1)}, GridIn(0.5, 0, 1, 0.5, 1, 2), GridIn(0.5, 0.5, 1, 1, 1, 3)),
            Rows("6-pyramid", "金字塔 1-2-3", Strip(1.3, 1), Strip(1, 1, 1), Strip(1, 1, 1, 1)),
            Custom("center-6", "中央大圖＋周圍 6", {Span(0.25, 0.25, 0.75, 0.75)},
                   GridIn(0, 0, 1, 0.25, 1, 2), GridIn(0, 0.75, 1, 1, 1, 2), {Span(0, 0.25, 0.25, 0.75), Span(0.75, 0.25, 1, 0.75)}),
            Rows("7-rows-3-4", "上 3 下 4", Strip(1, 1, 1, 1), Strip(1, 1, 1, 1, 1)),
            Rows("7-rows-2-3-2", "2-3-2", Strip(1, 1, 1), Strip(1, 1, 1, 1), Strip(1, 1, 1)),
            Custom("7-big-top", "大圖上＋下 6", {Span(0, 0, 1, 0.5)}, GridIn(0, 0.5, 1, 1, 2, 3)),
            Grid(2, 4, "2 × 4"),
            Grid(4, 2, "4 × 2"),
            Rows("8-rows-3-2-3", "3-2-3", Strip(1, 1, 1, 1), Strip(1, 1, 1), Strip(1, 1, 1, 1)),
            Custom("8-big-left", "大圖左＋右 7", {Span(0, 0, 0.4, 1)}, GridIn(0.4, 0, 1, 0.5, 1, 3), GridIn(0.4, 0.5, 1, 1, 1, 4)),
            Grid(3, 3, "九宮格"),
            Custom("9-big-center", "中央大圖＋周圍 8", {Span(0.25, 0.25, 0.75, 0.75)},
                   GridIn(0, 0, 1, 0.25, 1, 3), GridIn(0, 0.75, 1, 1, 1, 3), {Span(0, 0.25, 0.25, 0.75), Span(0.75, 0.25, 1, 0.75)}),
            Custom("9-big-top", "大圖上＋下 8", {Span(0, 0, 1, 0.5)}, GridIn(0, 0.5, 1, 1, 2, 4)),
            Grid(2, 5, "2 × 5"),
            Custom("10-big-left", "大圖左＋右 9", {Span(0, 0, 0.4, 1)}, GridIn(0.4, 0, 1, 1, 3, 3)),
            Rows("10-rows-3-4-3", "3-4-3", Strip(1, 1, 1, 1), Strip(1, 1, 1, 1, 1), Strip(1, 1, 1, 1)),
            Grid(3, 4, "3 × 4"),
            Grid(4, 3, "4 × 3"),
            Custom("13-big-center", "中央大圖＋周圍 12", {Span(0.25, 0.25, 0.75, 0.75)},
                   GridIn(0, 0, 1, 0.25, 1, 4), GridIn(0, 0.75, 1, 1, 1, 4), GridIn(0, 0.25, 0.25, 0.75, 2, 1), GridIn(0.75, 0.25, 1, 0.75, 2, 1)),
            Grid(4, 4, "4 × 4")
        }.AsReadOnly()
    End Function

    Private Const Third As Double = 1 / 3

    ''' <summary>一列（或一欄）：所佔比重，以及其中每格的比重。</summary>
    Private NotInheritable Class StripSpec
        Public Weight As Double
        Public Parts As Double()
    End Class

    Private Function Strip(weight As Double, ParamArray parts As Double()) As StripSpec
        Return New StripSpec With {.Weight = weight, .Parts = parts}
    End Function

    ''' <summary>由上而下的數列，每列再依比重左右切分。</summary>
    Private Function Rows(id As String, name As String, ParamArray strips As StripSpec()) As CollageTemplate
        Return New CollageTemplate(id, name, BuildStrips(strips, horizontal:=True))
    End Function

    ''' <summary>由左而右的數欄，每欄再依比重上下切分。</summary>
    Private Function Columns(id As String, name As String, ParamArray strips As StripSpec()) As CollageTemplate
        Return New CollageTemplate(id, name, BuildStrips(strips, horizontal:=False))
    End Function

    Private Function BuildStrips(strips As StripSpec(), horizontal As Boolean) As List(Of RectangleF)
        Dim cells As New List(Of RectangleF)
        Dim total = strips.Sum(Function(s) s.Weight)
        Dim a = 0.0
        For si = 0 To strips.Length - 1
            Dim b = If(si = strips.Length - 1, 1.0, a + strips(si).Weight / total)
            Dim parts = strips(si).Parts
            Dim partTotal = parts.Sum()
            Dim c = 0.0
            For pi = 0 To parts.Length - 1
                Dim d = If(pi = parts.Length - 1, 1.0, c + parts(pi) / partTotal)
                cells.Add(If(horizontal, Span(c, a, d, b), Span(a, c, b, d)))
                c = d
            Next
            a = b
        Next
        Return cells
    End Function

    ''' <summary>在指定範圍內切出 rows × columns 等分格。</summary>
    Private Function GridIn(left As Double, top As Double, right As Double, bottom As Double, rows As Integer, columns As Integer) As RectangleF()
        Dim cells As New List(Of RectangleF)
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                Dim x0 = left + (right - left) * c / columns
                Dim x1 = If(c = columns - 1, right, left + (right - left) * (c + 1) / columns)
                Dim y0 = top + (bottom - top) * r / rows
                Dim y1 = If(r = rows - 1, bottom, top + (bottom - top) * (r + 1) / rows)
                cells.Add(Span(x0, y0, x1, y1))
            Next
        Next
        Return cells.ToArray()
    End Function

    Private Function Custom(id As String, name As String, ParamArray groups As RectangleF()()) As CollageTemplate
        Return New CollageTemplate(id, name, groups.SelectMany(Function(g) g))
    End Function

    ''' <summary>以左上與右下角建立矩形，避免累加誤差造成縫隙。</summary>
    Private Function Span(left As Double, top As Double, right As Double, bottom As Double) As RectangleF
        Return RectangleF.FromLTRB(CSng(left), CSng(top), CSng(right), CSng(bottom))
    End Function

End Module

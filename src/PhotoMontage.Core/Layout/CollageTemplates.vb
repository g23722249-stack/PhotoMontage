Imports System.Drawing

''' <summary>內建拼貼版型。</summary>
Public Module CollageTemplates

    ''' <summary>rows × columns 等分格。</summary>
    Public Function Grid(rows As Integer, columns As Integer) As CollageTemplate
        If rows < 1 Then Throw New ArgumentOutOfRangeException(NameOf(rows))
        If columns < 1 Then Throw New ArgumentOutOfRangeException(NameOf(columns))

        Dim cells As New List(Of RectangleF)(rows * columns)
        For r = 0 To rows - 1
            For c = 0 To columns - 1
                cells.Add(Span(c / columns, r / rows, (c + 1) / columns, (r + 1) / rows))
            Next
        Next
        Return New CollageTemplate($"grid-{rows}x{columns}", $"{rows} × {columns}", cells)
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

    Private Function CreateBuiltIn() As IReadOnlyList(Of CollageTemplate)
        Return New List(Of CollageTemplate) From {
            Grid(1, 2),
            Grid(2, 1),
            Grid(1, 3),
            Grid(2, 2),
            Grid(2, 3),
            Grid(3, 3),
            New CollageTemplate("big-left-2", "大圖左＋右 2", {
                Span(0, 0, 2 / 3, 1),
                Span(2 / 3, 0, 1, 0.5),
                Span(2 / 3, 0.5, 1, 1)}),
            New CollageTemplate("big-top-3", "大圖上＋下 3", {
                Span(0, 0, 1, 2 / 3),
                Span(0, 2 / 3, 1 / 3, 1),
                Span(1 / 3, 2 / 3, 2 / 3, 1),
                Span(2 / 3, 2 / 3, 1, 1)}),
            New CollageTemplate("center-6", "中央大圖＋周圍 6", {
                Span(0.25, 0.25, 0.75, 0.75),
                Span(0, 0, 0.5, 0.25),
                Span(0.5, 0, 1, 0.25),
                Span(0, 0.75, 0.5, 1),
                Span(0.5, 0.75, 1, 1),
                Span(0, 0.25, 0.25, 0.75),
                Span(0.75, 0.25, 1, 0.75)})
        }.AsReadOnly()
    End Function

    ''' <summary>以左上與右下角建立矩形，避免累加誤差造成縫隙。</summary>
    Private Function Span(left As Double, top As Double, right As Double, bottom As Double) As RectangleF
        Return RectangleF.FromLTRB(CSng(left), CSng(top), CSng(right), CSng(bottom))
    End Function

End Module

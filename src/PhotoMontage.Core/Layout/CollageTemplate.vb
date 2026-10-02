Imports System.Drawing

''' <summary>拼貼版型：一組以 0~1 相對座標定義的格子。</summary>
Public NotInheritable Class CollageTemplate
    Public ReadOnly Property Id As String
    Public ReadOnly Property Name As String
    Public ReadOnly Property Cells As IReadOnlyList(Of RectangleF)

    Public Sub New(id As String, name As String, cells As IEnumerable(Of RectangleF))
        If String.IsNullOrWhiteSpace(id) Then Throw New ArgumentException("版型 Id 不可空白。", NameOf(id))
        Me.Id = id
        Me.Name = name
        Me.Cells = cells.ToList().AsReadOnly()
    End Sub

    Public ReadOnly Property CellCount As Integer
        Get
            Return Cells.Count
        End Get
    End Property

    ''' <summary>依此版型建立空白格子清單。</summary>
    Public Function CreateCells() As List(Of Cell)
        Return Cells.Select(Function(r) New Cell With {.Bounds = r}).ToList()
    End Function
End Class

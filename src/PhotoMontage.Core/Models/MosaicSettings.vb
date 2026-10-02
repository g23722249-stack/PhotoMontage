''' <summary>馬賽克模式的設定與結果。</summary>
Public Class MosaicSettings
    ''' <summary>主圖完整路徑（可以是素材之一，也可以是其他圖檔）。</summary>
    Public Property TargetPath As String

    Public Property Columns As Integer = 60
    Public Property Rows As Integer = 60

    ''' <summary>疊上主圖的強度，0～0.3；能大幅提升遠看時的辨識度。</summary>
    Public Property Tint As Single = 0.15F

    ''' <summary>同一張素材最多使用次數；0 表示不限制。</summary>
    Public Property MaxRepeat As Integer = 0

    ''' <summary>相鄰格子不使用同一張素材。</summary>
    Public Property AvoidAdjacentDuplicates As Boolean = True

    ''' <summary>每格使用的素材照片 Id（逐列，長度 = Columns × Rows）；尚未產生時為空。</summary>
    Public Property Tiles As New List(Of String)

    Public Const MinColumns As Integer = 10
    Public Const MaxColumns As Integer = 150

    ''' <summary>是否已產生且與目前格數一致。</summary>
    Public ReadOnly Property IsGenerated As Boolean
        Get
            Return Columns > 0 AndAlso Rows > 0 AndAlso Tiles.Count = Columns * Rows
        End Get
    End Property

    Public ReadOnly Property CellCount As Integer
        Get
            Return Columns * Rows
        End Get
    End Property

    ''' <summary>格子接近正方形時的列數。</summary>
    Public Shared Function RowsFor(columns As Integer, canvasAspect As Double) As Integer
        If canvasAspect <= 0 Then canvasAspect = 1
        Return Math.Max(1, CInt(Math.Round(columns / canvasAspect)))
    End Function
End Class

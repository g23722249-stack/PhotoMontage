''' <summary>馬賽克模式的設定（第二階段實作）。</summary>
Public Class MosaicSettings
    Public Property TargetPhotoId As String
    Public Property Columns As Integer = 60
    Public Property Rows As Integer = 60

    ''' <summary>每格疊上主圖原色的強度，0~0.3。</summary>
    Public Property Tint As Single = 0.15F

    ''' <summary>同一張素材最多使用次數；0 表示不限制。</summary>
    Public Property MaxRepeat As Integer = 0

    ''' <summary>每格使用的素材照片 Id，依列優先排列，長度 = Columns * Rows。</summary>
    Public Property Tiles As New List(Of String)
End Class

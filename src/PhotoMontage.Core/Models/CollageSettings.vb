Imports System.Drawing

''' <summary>拼貼模式的版面設定。</summary>
Public Class CollageSettings
    Public Property TemplateId As String = "grid-2x2"

    ''' <summary>格子間距，以畫布短邊的比例表示（0~0.1）。</summary>
    Public Property Gap As Single = 0.01F

    ''' <summary>格子圓角，以格子短邊的比例表示（0~0.5）。</summary>
    Public Property CornerRadius As Single = 0F

    Public Property Cells As New List(Of Cell)
End Class

''' <summary>拼貼中的一個格子。</summary>
Public Class Cell
    ''' <summary>格子在畫布上的位置，0~1 相對座標（未扣除間距）。</summary>
    Public Property Bounds As RectangleF

    ''' <summary>放在此格的照片；空格為 Nothing。</summary>
    Public Property PhotoId As String

    Public Property Crop As New CropInfo
End Class

''' <summary>照片在格子內的取景。預設為置中填滿（cover）。</summary>
Public Class CropInfo
    ''' <summary>水平偏移，-1（靠左）~ 1（靠右），0 為置中。</summary>
    Public Property OffsetX As Single

    ''' <summary>垂直偏移，-1（靠上）~ 1（靠下），0 為置中。</summary>
    Public Property OffsetY As Single

    ''' <summary>在 cover 基礎上的額外放大倍率，≥ 1。</summary>
    Public Property Scale As Single = 1.0F
End Class

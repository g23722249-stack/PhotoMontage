Imports System.Drawing

''' <summary>拼貼模式的版面設定。</summary>
Public Class CollageSettings
    ''' <summary>版型 Id；<see cref="CollageTemplates.AutoId"/> 表示依照片自動排版。</summary>
    Public Property TemplateId As String = CollageTemplates.AutoId

    ''' <summary>格子間距，以畫布短邊的比例表示（0~0.1）。</summary>
    Public Property Gap As Single = 0.01F

    ''' <summary>格子圓角，以格子短邊的比例表示（0~0.5）。</summary>
    Public Property CornerRadius As Single = 0F

    Public Property Cells As New List(Of Cell)

    ''' <summary>使用者拖曳分隔線調整過格子大小（重新套用版型或自動排版重排時清除）。</summary>
    Public Property CellsAdjusted As Boolean
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

    ''' <summary>照片順時針旋轉幾個 90°（0~3），在水平翻轉之後套用。偏移與縮放以轉正後的照片為準。</summary>
    Public Property Rotation As Integer

    ''' <summary>水平翻轉（鏡像）。</summary>
    Public Property FlipHorizontal As Boolean

    Public Function Clone() As CropInfo
        Return New CropInfo With {.OffsetX = OffsetX, .OffsetY = OffsetY, .Scale = Scale, .Rotation = Rotation, .FlipHorizontal = FlipHorizontal}
    End Function
End Class

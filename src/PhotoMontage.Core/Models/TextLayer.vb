Imports System.Drawing

''' <summary>疊在作品上的文字圖層。位置與字級都以畫布比例儲存，任何輸出尺寸大小都一致。</summary>
Public Class TextLayer
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Text As String = ""
    Public Property FontFamily As String = "Microsoft JhengHei UI"

    ''' <summary>字級，以畫布高度的比例表示。</summary>
    Public Property FontSize As Single = 0.06F

    Public Property Bold As Boolean
    Public Property Italic As Boolean
    Public Property Color As Color = Color.White
    Public Property Alignment As StringAlignment = StringAlignment.Center

    ''' <summary>外框粗細，以字級的比例表示；0 為無外框。</summary>
    Public Property OutlineWidth As Single
    Public Property OutlineColor As Color = Color.Black

    Public Property ShadowEnabled As Boolean
    Public Property ShadowColor As Color = Color.FromArgb(128, 0, 0, 0)

    ''' <summary>陰影偏移，以字級的比例表示。</summary>
    Public Property ShadowOffset As PointF = New PointF(0.05F, 0.05F)

    ''' <summary>文字中心點，0~1 相對座標。</summary>
    Public Property Position As PointF = New PointF(0.5F, 0.9F)

    ''' <summary>順時針旋轉角度（度）。</summary>
    Public Property Rotation As Single
End Class

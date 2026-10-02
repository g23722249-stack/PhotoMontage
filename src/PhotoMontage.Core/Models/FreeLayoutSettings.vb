''' <summary>自由拼貼中照片的外框樣式。</summary>
Public Enum FrameStyle
    None = 0
    ''' <summary>四邊等寬的白邊。</summary>
    White = 1
    ''' <summary>拍立得：底部留白較寬。</summary>
    Polaroid = 2
End Enum

''' <summary>自由拼貼畫布上的一張照片。位置與大小以畫布比例儲存，任何輸出尺寸都一致。</summary>
Public Class FreeItem
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property PhotoId As String

    ''' <summary>中心點，0~1 相對畫布寬與高。</summary>
    Public Property CenterX As Single = 0.5F
    Public Property CenterY As Single = 0.5F

    ''' <summary>外框（含邊框）寬度，以畫布寬度的比例表示。</summary>
    Public Property Width As Single = 0.3F

    ''' <summary>照片區域的長寬比（寬 / 高）；放上畫布時取照片本身的比例。</summary>
    Public Property InnerAspect As Single = 1.5F

    ''' <summary>順時針角度（度）。</summary>
    Public Property Rotation As Single

    Public Property Frame As FrameStyle = FrameStyle.White

    ''' <summary>邊框寬度，以外框寬度的比例表示（拍立得底部為其 3.5 倍）。</summary>
    Public Property FrameWidth As Single = 0.04F

    Public Property Shadow As Boolean = True

    ''' <summary>照片在框內的取景。</summary>
    Public Property Crop As New CropInfo

    Public Function Clone() As FreeItem
        Return New FreeItem With {
            .PhotoId = PhotoId, .CenterX = CenterX, .CenterY = CenterY, .Width = Width, .InnerAspect = InnerAspect,
            .Rotation = Rotation, .Frame = Frame, .FrameWidth = FrameWidth, .Shadow = Shadow,
            .Crop = New CropInfo With {.OffsetX = Crop.OffsetX, .OffsetY = Crop.OffsetY, .Scale = Crop.Scale}}
    End Function
End Class

''' <summary>自由拼貼的設定。</summary>
Public Class FreeLayoutSettings
    ''' <summary>畫布上的照片，依圖層由下往上排列。</summary>
    Public Property Items As New List(Of FreeItem)

    ''' <summary>「自動散佈」的隨性程度，0（整齊）～ 1（隨性）。</summary>
    Public Property Looseness As Single = 0.5F

    Public Const MinItemWidth As Single = 0.04F
    Public Const MaxItemWidth As Single = 1.5F

    Public Function Find(id As String) As FreeItem
        Return Items.Find(Function(i) i.Id = id)
    End Function
End Class

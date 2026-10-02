Imports System.Drawing

''' <summary>一份蒙太奇作品的完整描述。預覽與匯出都只依這個物件渲染。</summary>
Public Class MontageProject
    Public Property Mode As MontageMode = MontageMode.Collage

    ''' <summary>輸出畫布尺寸（像素）。版面一律以 0~1 相對座標儲存，可用任意尺寸匯出。</summary>
    Public Property CanvasSize As Size = New Size(2048, 2048)

    Public Property BackgroundColor As Color = Color.White

    ''' <summary>背景圖完整路徑（以 cover 方式鋪滿畫布）；Nothing 表示只用背景色。</summary>
    Public Property BackgroundImagePath As String

    Public Property Photos As New List(Of PhotoAsset)

    Public Property Collage As New CollageSettings

    Public Property Mosaic As New MosaicSettings

    Public Property Free As New FreeLayoutSettings

    Public Property Texts As New List(Of TextLayer)

    ''' <summary>畫布長寬比（寬 / 高）。</summary>
    Public ReadOnly Property CanvasAspect As Double
        Get
            If CanvasSize.Height <= 0 Then Return 1.0
            Return CanvasSize.Width / CanvasSize.Height
        End Get
    End Property

    Public Function FindPhoto(photoId As String) As PhotoAsset
        If String.IsNullOrEmpty(photoId) Then Return Nothing
        Return Photos.Find(Function(p) p.Id = photoId)
    End Function
End Class

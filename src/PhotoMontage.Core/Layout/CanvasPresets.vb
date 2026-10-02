Imports System.Drawing

''' <summary>畫布比例選項。</summary>
Public NotInheritable Class CanvasPreset
    Public ReadOnly Property Name As String
    Public ReadOnly Property RatioWidth As Integer
    Public ReadOnly Property RatioHeight As Integer

    Public Sub New(name As String, ratioWidth As Integer, ratioHeight As Integer)
        Me.Name = name
        Me.RatioWidth = ratioWidth
        Me.RatioHeight = ratioHeight
    End Sub

    ''' <summary>長邊為 <paramref name="longEdge"/> 像素時的畫布尺寸。</summary>
    Public Function SizeFor(longEdge As Integer) As Size
        If RatioWidth >= RatioHeight Then
            Return New Size(longEdge, Math.Max(1, CInt(Math.Round(longEdge * RatioHeight / RatioWidth))))
        End If
        Return New Size(Math.Max(1, CInt(Math.Round(longEdge * RatioWidth / RatioHeight))), longEdge)
    End Function

    ''' <summary>尺寸是否符合此比例（容許 1% 誤差）。</summary>
    Public Function Matches(size As Size) As Boolean
        If size.Width <= 0 OrElse size.Height <= 0 Then Return False
        Return Math.Abs(size.Width / size.Height - RatioWidth / RatioHeight) < 0.01 * RatioWidth / RatioHeight
    End Function

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module CanvasPresets

    ''' <summary>預覽與預設輸出的長邊像素。</summary>
    Public Const DefaultLongEdge As Integer = 2048

    Public ReadOnly All As IReadOnlyList(Of CanvasPreset) = New List(Of CanvasPreset) From {
        New CanvasPreset("1:1 正方形", 1, 1),
        New CanvasPreset("4:5 直式（社群貼文）", 4, 5),
        New CanvasPreset("9:16 直式（限時動態）", 9, 16),
        New CanvasPreset("2:3 直式", 2, 3),
        New CanvasPreset("3:2 橫式", 3, 2),
        New CanvasPreset("4:3 橫式", 4, 3),
        New CanvasPreset("16:9 寬螢幕", 16, 9),
        New CanvasPreset("A4 直式", 210, 297),
        New CanvasPreset("A4 橫式", 297, 210)
    }.AsReadOnly()

    ''' <summary>找出符合尺寸的比例；找不到時回傳 Nothing。</summary>
    Public Function Find(size As Size) As CanvasPreset
        Return All.FirstOrDefault(Function(p) p.Matches(size))
    End Function

End Module

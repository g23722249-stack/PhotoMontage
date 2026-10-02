Imports System.Drawing

''' <summary>一個區域（素材照片或主圖的一格）的色彩特徵：平均色＋2×2 子區塊色。</summary>
Public NotInheritable Class MosaicFeature
    Public ReadOnly Property Average As LabColor
    ''' <summary>左上、右上、左下、右下。</summary>
    Public ReadOnly Property Quadrants As LabColor()
    Public ReadOnly Property AverageColor As Color

    Public Sub New(average As LabColor, quadrants As LabColor(), averageColor As Color)
        If quadrants Is Nothing OrElse quadrants.Length <> 4 Then Throw New ArgumentException("需要 4 個子區塊。", NameOf(quadrants))
        Me.Average = average
        Me.Quadrants = quadrants
        Me.AverageColor = averageColor
    End Sub

    ''' <summary>單一顏色的特徵（測試與純色區域用）。</summary>
    Public Shared Function Solid(color As Color) As MosaicFeature
        Dim lab = LabColor.FromColor(color)
        Return New MosaicFeature(lab, {lab, lab, lab, lab}, color)
    End Function

    ''' <summary>兩個特徵的差異：四個子區塊 Lab 距離平方的總和。</summary>
    Public Function Distance(other As MosaicFeature) As Single
        Dim sum = 0F
        For i = 0 To 3
            sum += LabColor.DistanceSquared(Quadrants(i), other.Quadrants(i))
        Next
        Return sum
    End Function
End Class

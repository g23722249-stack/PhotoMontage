Imports System.Drawing

''' <summary>快取中的一筆縮圖：照片資訊＋已轉正的縮圖＋平均色。</summary>
Public NotInheritable Class CachedThumbnail
    Public ReadOnly Property Info As ImageInfo
    Public ReadOnly Property Image As DecodedImage
    Public ReadOnly Property AverageColor As Color

    Public Sub New(info As ImageInfo, image As DecodedImage)
        If info Is Nothing Then Throw New ArgumentNullException(NameOf(info))
        If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))
        Me.Info = info
        Me.Image = image
        AverageColor = image.ComputeAverageColor()
    End Sub

    ''' <summary>估計佔用的記憶體（位元組）。</summary>
    Friend ReadOnly Property ApproximateSize As Long
        Get
            Return Image.Pixels.LongLength + 256
        End Get
    End Property
End Class

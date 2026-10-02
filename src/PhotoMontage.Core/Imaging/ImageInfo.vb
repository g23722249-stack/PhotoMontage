Imports System.Drawing

''' <summary>只讀檔頭就能取得的照片資訊。</summary>
Public NotInheritable Class ImageInfo
    ''' <summary>原始（未轉正）寬度。</summary>
    Public ReadOnly Property Width As Integer
    ''' <summary>原始（未轉正）高度。</summary>
    Public ReadOnly Property Height As Integer
    Public ReadOnly Property Orientation As ExifOrientation
    ''' <summary>EXIF 拍攝時間；沒有時為 Nothing。</summary>
    Public ReadOnly Property DateTaken As Date?

    Public Sub New(width As Integer, height As Integer, orientation As ExifOrientation, dateTaken As Date?)
        Me.Width = width
        Me.Height = height
        Me.Orientation = orientation
        Me.DateTaken = dateTaken
    End Sub

    ''' <summary>轉正後的尺寸。</summary>
    Public ReadOnly Property OrientedSize As Size
        Get
            Return ExifOrientations.GetOrientedSize(Width, Height, Orientation)
        End Get
    End Property

    Public ReadOnly Property PixelCount As Long
        Get
            Return CLng(Width) * Height
        End Get
    End Property
End Class

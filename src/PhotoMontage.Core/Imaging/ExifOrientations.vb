Imports System.Drawing

''' <summary>EXIF 方向的換算與套用。</summary>
Public Module ExifOrientations

    ''' <summary>將 EXIF 數值轉成列舉；無效值視為 Normal。</summary>
    Public Function FromValue(value As Integer) As ExifOrientation
        If value < 1 OrElse value > 8 Then Return ExifOrientation.Normal
        Return CType(value, ExifOrientation)
    End Function

    ''' <summary>轉正後寬高是否互換。</summary>
    Public Function SwapsDimensions(orientation As ExifOrientation) As Boolean
        Return orientation >= ExifOrientation.Transpose
    End Function

    ''' <summary>原始寬高轉正後的尺寸。</summary>
    Public Function GetOrientedSize(width As Integer, height As Integer, orientation As ExifOrientation) As Size
        Return If(SwapsDimensions(orientation), New Size(height, width), New Size(width, height))
    End Function

    ''' <summary>依 EXIF 方向把像素轉正；Normal 時直接回傳原物件。</summary>
    Public Function Apply(image As DecodedImage, orientation As ExifOrientation) As DecodedImage
        If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))
        If orientation = ExifOrientation.Normal Then Return image

        Dim w = image.Width, h = image.Height
        Dim size = GetOrientedSize(w, h, orientation)
        Dim src = image.Pixels
        Dim dst(src.Length - 1) As Byte

        For dy = 0 To size.Height - 1
            For dx = 0 To size.Width - 1
                Dim sx, sy As Integer
                Select Case orientation
                    Case ExifOrientation.FlipHorizontal : sx = w - 1 - dx : sy = dy
                    Case ExifOrientation.Rotate180 : sx = w - 1 - dx : sy = h - 1 - dy
                    Case ExifOrientation.FlipVertical : sx = dx : sy = h - 1 - dy
                    Case ExifOrientation.Transpose : sx = dy : sy = dx
                    Case ExifOrientation.Rotate90 : sx = dy : sy = h - 1 - dx
                    Case ExifOrientation.Transverse : sx = w - 1 - dy : sy = h - 1 - dx
                    Case Else : sx = w - 1 - dy : sy = dx ' Rotate270
                End Select
                Buffer.BlockCopy(src, (sy * w + sx) * 4, dst, (dy * size.Width + dx) * 4, 4)
            Next
        Next
        Return New DecodedImage(size.Width, size.Height, dst)
    End Function

End Module

Imports System.Drawing

''' <summary>
''' 解碼後的像素（BGRA 32 位元、逐列排列、stride = Width * 4）。
''' 純受控記憶體，不依賴 GDI+，可在背景執行緒間自由傳遞。
''' </summary>
Public NotInheritable Class DecodedImage
    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    Public ReadOnly Property Pixels As Byte()

    Public Sub New(width As Integer, height As Integer, pixels As Byte())
        If width <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(width))
        If height <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(height))
        If pixels Is Nothing Then Throw New ArgumentNullException(NameOf(pixels))
        If pixels.LongLength <> CLng(width) * height * 4 Then Throw New ArgumentException("像素資料長度與尺寸不符。", NameOf(pixels))
        Me.Width = width
        Me.Height = height
        Me.Pixels = pixels
    End Sub

    Public ReadOnly Property Stride As Integer
        Get
            Return Width * 4
        End Get
    End Property

    Public Function GetPixel(x As Integer, y As Integer) As Color
        Dim i = (y * Width + x) * 4
        Return Color.FromArgb(Pixels(i + 3), Pixels(i + 2), Pixels(i + 1), Pixels(i))
    End Function

    ''' <summary>以 alpha 加權的平均色；完全透明時回傳白色。</summary>
    Public Function ComputeAverageColor() As Color
        Dim r, g, b, a As Long
        For i = 0 To Pixels.Length - 1 Step 4
            Dim alpha As Integer = Pixels(i + 3)
            b += Pixels(i) * alpha
            g += Pixels(i + 1) * alpha
            r += Pixels(i + 2) * alpha
            a += alpha
        Next
        If a = 0 Then Return Color.White
        Return Color.FromArgb(CInt(r \ a), CInt(g \ a), CInt(b \ a))
    End Function

    Public Function HasTransparency() As Boolean
        For i = 3 To Pixels.Length - 1 Step 4
            If Pixels(i) <> 255 Then Return True
        Next
        Return False
    End Function
End Class

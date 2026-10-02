''' <summary>EXIF Orientation（0x0112）的值，表示原始像素需要如何轉換才會是正確方向。</summary>
Public Enum ExifOrientation
    Normal = 1
    FlipHorizontal = 2
    Rotate180 = 3
    FlipVertical = 4
    ''' <summary>沿左上－右下對角線翻轉。</summary>
    Transpose = 5
    ''' <summary>需順時針旋轉 90 度。</summary>
    Rotate90 = 6
    ''' <summary>沿右上－左下對角線翻轉。</summary>
    Transverse = 7
    ''' <summary>需順時針旋轉 270 度（逆時針 90 度）。</summary>
    Rotate270 = 8
End Enum

''' <summary>匯入的上限。預設值依 x86 宿主（iPhoto.Net）的記憶體預算訂定。</summary>
Public Class ImportLimits
    ''' <summary>單張照片的像素上限（2 億）。</summary>
    Public Property MaxPixels As Long = 200_000_000L

    ''' <summary>縮圖長邊上限（像素）。</summary>
    Public Property ThumbnailMaxEdge As Integer = 512

    Public Property MaxCollagePhotos As Integer = 100

    Public Property MaxMosaicPhotos As Integer = 2000

    ''' <summary>自由拼貼畫布上的照片數上限（照片清單本身沿用拼貼的上限）。</summary>
    Public Property MaxFreeItems As Integer = 30

    Public Function MaxPhotosFor(mode As MontageMode) As Integer
        Return If(mode = MontageMode.Mosaic, MaxMosaicPhotos, MaxCollagePhotos)
    End Function
End Class

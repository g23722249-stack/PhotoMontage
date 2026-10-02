Imports System.IO

''' <summary>
''' 影像解碼／編碼。正式實作為 PhotoMontage.Imaging 的 WicImageCodec；
''' Core 只依賴此介面，以便單元測試替換。實作必須可在多個背景執行緒同時呼叫。
''' </summary>
Public Interface IImageCodec
    ''' <summary>只讀檔頭與中繼資料，不解碼像素。</summary>
    ''' <exception cref="ImageDecodeException">格式不支援或檔案損壞。</exception>
    Function ReadInfo(stream As Stream) As ImageInfo

    ''' <summary>解碼並縮小到長邊不超過 <paramref name="maxEdge"/>（不放大、不套用 EXIF 方向）。</summary>
    ''' <exception cref="ImageDecodeException">格式不支援或檔案損壞。</exception>
    Function DecodeThumbnail(stream As Stream, maxEdge As Integer) As DecodedImage

    ''' <summary>把縮圖編碼成位元組（存磁碟快取用）。</summary>
    Function Encode(image As DecodedImage) As Byte()

    ''' <summary>解回 <see cref="Encode"/> 的結果。</summary>
    ''' <exception cref="ImageDecodeException">資料損壞。</exception>
    Function Decode(data As Byte()) As DecodedImage
End Interface

''' <summary>解碼失敗。</summary>
Public Class ImageDecodeException
    Inherits Exception

    ''' <summary>系統沒有可處理此格式的解碼器（例如未安裝 HEIF 延伸模組）。</summary>
    Public ReadOnly Property CodecMissing As Boolean

    Public Sub New(message As String, codecMissing As Boolean, Optional innerException As Exception = Nothing)
        MyBase.New(message, innerException)
        Me.CodecMissing = codecMissing
    End Sub
End Class

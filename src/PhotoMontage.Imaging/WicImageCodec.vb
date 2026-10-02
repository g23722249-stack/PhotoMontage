Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports PhotoMontage.Core

''' <summary>
''' 以 Windows Imaging Component 解碼。JPEG 可在解碼時直接縮小（DCT scaling），
''' 4800 萬像素照片產生 512px 縮圖只需約 1 MB 記憶體；安裝 HEIF 延伸模組後可讀 HEIC。
''' 所有 WPF 物件都在方法內建立並 Freeze，可由多個背景執行緒同時呼叫。
''' </summary>
Public NotInheritable Class WicImageCodec
    Implements IImageCodec

    ''' <summary>WINCODEC_ERR_COMPONENTNOTFOUND：找不到可處理此格式的解碼器。</summary>
    Private Const ComponentNotFound As Integer = &H88982F50

    Private Const JpegQuality As Integer = 90

    Public Function ReadInfo(stream As Stream) As ImageInfo Implements IImageCodec.ReadInfo
        Return Wrap(
            Function()
                stream.Position = 0
                Dim decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation Or BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None)
                Dim frame = decoder.Frames(0)
                Dim metadata = TryGetMetadata(frame)
                Return New ImageInfo(frame.PixelWidth, frame.PixelHeight, ReadOrientation(metadata), ReadDateTaken(metadata))
            End Function)
    End Function

    Public Function DecodeThumbnail(stream As Stream, maxEdge As Integer) As DecodedImage Implements IImageCodec.DecodeThumbnail
        If maxEdge <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(maxEdge))

        Return Wrap(
            Function()
                stream.Position = 0
                Dim frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation Or BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames(0)
                Dim w = frame.PixelWidth, h = frame.PixelHeight

                stream.Position = 0
                Dim bmp As New BitmapImage()
                bmp.BeginInit()
                bmp.CacheOption = BitmapCacheOption.OnLoad
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile
                bmp.StreamSource = stream
                ' 只設一邊，另一邊依比例；BitmapImage 不套用 EXIF 方向，所以用原始寬高判斷
                If w >= h Then
                    bmp.DecodePixelWidth = Math.Min(maxEdge, w)
                Else
                    bmp.DecodePixelHeight = Math.Min(maxEdge, h)
                End If
                bmp.EndInit()
                bmp.Freeze()
                Return ToDecodedImage(bmp)
            End Function)
    End Function

    Public Function Encode(image As DecodedImage) As Byte() Implements IImageCodec.Encode
        If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))

        Dim source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, Nothing, image.Pixels, image.Stride)
        Dim encoder As BitmapEncoder
        If image.HasTransparency() Then
            encoder = New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(source))
        Else
            encoder = New JpegBitmapEncoder() With {.QualityLevel = JpegQuality}
            encoder.Frames.Add(BitmapFrame.Create(New FormatConvertedBitmap(source, PixelFormats.Bgr24, Nothing, 0)))
        End If

        Using ms As New MemoryStream()
            encoder.Save(ms)
            Return ms.ToArray()
        End Using
    End Function

    Public Function Decode(data As Byte()) As DecodedImage Implements IImageCodec.Decode
        If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))

        Return Wrap(
            Function()
                Using ms As New MemoryStream(data, writable:=False)
                    Dim decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad)
                    Return ToDecodedImage(decoder.Frames(0))
                End Using
            End Function)
    End Function

    Private Shared Function ToDecodedImage(source As BitmapSource) As DecodedImage
        Dim converted As BitmapSource = source
        If source.Format <> PixelFormats.Bgra32 Then converted = New FormatConvertedBitmap(source, PixelFormats.Bgra32, Nothing, 0)

        Dim w = converted.PixelWidth, h = converted.PixelHeight
        Dim pixels = New Byte(w * h * 4 - 1) {}
        converted.CopyPixels(pixels, w * 4, 0)
        Return New DecodedImage(w, h, pixels)
    End Function

    Private Shared Function TryGetMetadata(frame As BitmapFrame) As BitmapMetadata
        Try
            Return TryCast(frame.Metadata, BitmapMetadata)
        Catch ex As NotSupportedException
            Return Nothing ' 例如 BMP 沒有中繼資料
        End Try
    End Function

    Private Shared Function ReadOrientation(metadata As BitmapMetadata) As ExifOrientation
        If metadata Is Nothing Then Return ExifOrientation.Normal
        Try
            Dim value = metadata.GetQuery("System.Photo.Orientation")
            If value Is Nothing Then Return ExifOrientation.Normal
            Return ExifOrientations.FromValue(Convert.ToInt32(value, CultureInfo.InvariantCulture))
        Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is InvalidOperationException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is FormatException OrElse
                                   TypeOf ex Is InvalidCastException OrElse TypeOf ex Is OverflowException
            Return ExifOrientation.Normal
        End Try
    End Function

    Private Shared Function ReadDateTaken(metadata As BitmapMetadata) As Date?
        If metadata Is Nothing Then Return Nothing
        Try
            ' BitmapMetadata.DateTaken 以目前文化格式化
            Dim text = metadata.DateTaken
            If String.IsNullOrEmpty(text) Then Return Nothing
            Dim value As Date
            If Date.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, value) Then Return value
            If Date.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, value) Then Return value
            Return Nothing
        Catch ex As Exception When TypeOf ex Is NotSupportedException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

    ''' <summary>把 WIC/WPF 的各種例外統一成 <see cref="ImageDecodeException"/>。</summary>
    Private Shared Function Wrap(Of T)(action As Func(Of T)) As T
        Try
            Return action()
        Catch ex As NotSupportedException
            Throw New ImageDecodeException("找不到可處理此格式的解碼器。", True, ex)
        Catch ex As COMException When ex.HResult = ComponentNotFound
            Throw New ImageDecodeException("找不到可處理此格式的解碼器。", True, ex)
        Catch ex As Exception When TypeOf ex Is FileFormatException OrElse TypeOf ex Is COMException OrElse
                                   TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException OrElse
                                   TypeOf ex Is OverflowException OrElse TypeOf ex Is EndOfStreamException
            Throw New ImageDecodeException("影像資料損壞或無法解碼。", False, ex)
        End Try
    End Function

End Class

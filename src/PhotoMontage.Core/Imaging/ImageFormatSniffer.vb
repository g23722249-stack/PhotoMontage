Imports System.IO

''' <summary>依檔頭判斷的影像格式。</summary>
Public Enum ImageFileFormat
    Unknown = 0
    Jpeg
    Png
    Gif
    Bmp
    Tiff
    ''' <summary>HEIC / HEIF（iPhone 照片）。</summary>
    Heif
    WebP
End Enum

''' <summary>以副檔名與檔頭（magic number）判斷影像格式。</summary>
Public Module ImageFormatSniffer

    Public ReadOnly SupportedExtensions As IReadOnlyCollection(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".webp"
    }

    Private ReadOnly HeifBrands As New HashSet(Of String)(StringComparer.Ordinal) From {
        "heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"
    }

    ''' <summary>給開檔對話框用的篩選字串。</summary>
    Public ReadOnly Property DialogFilter As String
        Get
            Dim patterns = String.Join(";", SupportedExtensions.Select(Function(e) "*" & e))
            Return $"圖片|{patterns}|所有檔案|*.*"
        End Get
    End Property

    Public Function IsSupportedExtension(path As String) As Boolean
        If String.IsNullOrEmpty(path) Then Return False
        Return SupportedExtensions.Contains(IO.Path.GetExtension(path))
    End Function

    ''' <summary>讀取串流開頭判斷格式，並把位置還原。</summary>
    Public Function Detect(stream As Stream) As ImageFileFormat
        Dim original = stream.Position
        Dim header(15) As Byte
        Dim read = 0
        Do While read < header.Length
            Dim n = stream.Read(header, read, header.Length - read)
            If n = 0 Then Exit Do
            read += n
        Loop
        stream.Position = original
        Return Detect(header, read)
    End Function

    Public Function Detect(header As Byte(), length As Integer) As ImageFileFormat
        If length >= 3 AndAlso header(0) = &HFF AndAlso header(1) = &HD8 AndAlso header(2) = &HFF Then Return ImageFileFormat.Jpeg
        If length >= 8 AndAlso StartsWith(header, {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA}) Then Return ImageFileFormat.Png
        If length >= 6 AndAlso (Ascii(header, 0, 6) = "GIF87a" OrElse Ascii(header, 0, 6) = "GIF89a") Then Return ImageFileFormat.Gif
        If length >= 2 AndAlso Ascii(header, 0, 2) = "BM" Then Return ImageFileFormat.Bmp
        If length >= 4 AndAlso (StartsWith(header, {&H49, &H49, &H2A, 0}) OrElse StartsWith(header, {&H4D, &H4D, 0, &H2A})) Then Return ImageFileFormat.Tiff
        If length >= 12 AndAlso Ascii(header, 4, 4) = "ftyp" AndAlso HeifBrands.Contains(Ascii(header, 8, 4)) Then Return ImageFileFormat.Heif
        If length >= 12 AndAlso Ascii(header, 0, 4) = "RIFF" AndAlso Ascii(header, 8, 4) = "WEBP" Then Return ImageFileFormat.WebP
        Return ImageFileFormat.Unknown
    End Function

    Private Function StartsWith(data As Byte(), prefix As Integer()) As Boolean
        For i = 0 To prefix.Length - 1
            If data(i) <> prefix(i) Then Return False
        Next
        Return True
    End Function

    Private Function Ascii(data As Byte(), offset As Integer, count As Integer) As String
        Return Text.Encoding.ASCII.GetString(data, offset, count)
    End Function

End Module

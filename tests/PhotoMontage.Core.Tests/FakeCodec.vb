Imports System.IO
Imports System.Threading
Imports PhotoMontage.Core

''' <summary>
''' 測試用解碼器。檔案格式：任意檔頭（決定 sniff 結果）＋ "FAKE" ＋ 寬(4) 高(4) 方向(1) 顏色ARGB(4) 旗標(1)。
''' </summary>
Friend Class FakeCodec
    Implements IImageCodec

    Public Const FlagCorrupt As Byte = 1
    Public Const FlagCodecMissing As Byte = 2
    ''' <summary>左半邊為指定顏色、右半邊為白色。</summary>
    Public Const FlagSplitWhite As Byte = 4

    Private Shared ReadOnly Marker As Byte() = Text.Encoding.ASCII.GetBytes("FAKE")
    Private _decodeCount As Integer

    Public ReadOnly Property DecodeCount As Integer
        Get
            Return _decodeCount
        End Get
    End Property

    Public Shared ReadOnly JpegHeader As Byte() = {&HFF, &HD8, &HFF, &HE0}
    Public Shared ReadOnly HeifHeader As Byte() = {0, 0, 0, &H18, &H66, &H74, &H79, &H70, &H68, &H65, &H69, &H63} ' ....ftypheic

    Public Shared Sub WriteFile(path As String, width As Integer, height As Integer,
                                Optional orientation As ExifOrientation = ExifOrientation.Normal,
                                Optional argb As Integer = &HFF336699, Optional flags As Byte = 0,
                                Optional header As Byte() = Nothing)
        Using fs = File.Create(path), w As New BinaryWriter(fs)
            w.Write(If(header, JpegHeader))
            w.Write(Marker)
            w.Write(width)
            w.Write(height)
            w.Write(CByte(orientation))
            w.Write(argb)
            w.Write(flags)
        End Using
    End Sub

    Private Shared Function Parse(stream As Stream) As (W As Integer, H As Integer, O As ExifOrientation, Argb As Integer, Flags As Byte)
        stream.Position = 0
        Dim data = New BinaryReader(stream).ReadBytes(CInt(stream.Length))
        Dim i = IndexOf(data, Marker)
        If i < 0 Then Throw New ImageDecodeException("no marker", False)
        Using r As New BinaryReader(New MemoryStream(data, i + 4, data.Length - i - 4))
            Return (r.ReadInt32(), r.ReadInt32(), CType(r.ReadByte(), ExifOrientation), r.ReadInt32(), r.ReadByte())
        End Using
    End Function

    Public Function ReadInfo(stream As Stream) As ImageInfo Implements IImageCodec.ReadInfo
        Dim f = Parse(stream)
        If (f.Flags And FlagCodecMissing) <> 0 Then Throw New ImageDecodeException("missing", True)
        Return New ImageInfo(f.W, f.H, f.O, New Date(2024, 5, 1, 10, 0, 0))
    End Function

    Public Function DecodeThumbnail(stream As Stream, maxEdge As Integer) As DecodedImage Implements IImageCodec.DecodeThumbnail
        Interlocked.Increment(_decodeCount)
        Dim f = Parse(stream)
        If (f.Flags And FlagCorrupt) <> 0 Then Throw New ImageDecodeException("corrupt", False)
        Dim scale = Math.Min(1.0, maxEdge / Math.Max(f.W, f.H))
        Dim img = Solid(Math.Max(1, CInt(f.W * scale)), Math.Max(1, CInt(f.H * scale)), f.Argb)
        If (f.Flags And FlagSplitWhite) <> 0 Then
            For y = 0 To img.Height - 1
                For x = img.Width \ 2 To img.Width - 1
                    Dim i = (y * img.Width + x) * 4
                    img.Pixels(i) = 255 : img.Pixels(i + 1) = 255 : img.Pixels(i + 2) = 255 : img.Pixels(i + 3) = 255
                Next
            Next
        End If
        Return img
    End Function

    Public Function Encode(image As DecodedImage) As Byte() Implements IImageCodec.Encode
        Using ms As New MemoryStream(), w As New BinaryWriter(ms)
            w.Write(image.Width)
            w.Write(image.Height)
            w.Write(image.Pixels)
            w.Flush()
            Return ms.ToArray()
        End Using
    End Function

    Public Function Decode(data As Byte()) As DecodedImage Implements IImageCodec.Decode
        Try
            Using r As New BinaryReader(New MemoryStream(data))
                Dim w = r.ReadInt32(), h = r.ReadInt32()
                Return New DecodedImage(w, h, r.ReadBytes(w * h * 4))
            End Using
        Catch ex As Exception When TypeOf ex Is EndOfStreamException OrElse TypeOf ex Is ArgumentException
            Throw New ImageDecodeException("bad cache", False, ex)
        End Try
    End Function

    Public Shared Function Solid(width As Integer, height As Integer, argb As Integer) As DecodedImage
        Dim px(width * height * 4 - 1) As Byte
        For i = 0 To px.Length - 1 Step 4
            px(i) = CByte(argb And &HFF)
            px(i + 1) = CByte((argb >> 8) And &HFF)
            px(i + 2) = CByte((argb >> 16) And &HFF)
            px(i + 3) = CByte((argb >> 24) And &HFF)
        Next
        Return New DecodedImage(width, height, px)
    End Function

    Private Shared Function IndexOf(data As Byte(), pattern As Byte()) As Integer
        For i = 0 To data.Length - pattern.Length
            Dim ok = True
            For j = 0 To pattern.Length - 1
                If data(i + j) <> pattern(j) Then ok = False : Exit For
            Next
            If ok Then Return i
        Next
        Return -1
    End Function
End Class

''' <summary>每個測試一個暫存資料夾，結束時刪除。</summary>
Friend NotInheritable Class TempFolder
    Implements IDisposable

    Public ReadOnly Property Path As String = IO.Path.Combine(IO.Path.GetTempPath(), "pm-tests-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(Path)
    End Sub

    Public Function Combine(ParamArray parts As String()) As String
        Return IO.Path.Combine({Path}.Concat(parts).ToArray())
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(Path, recursive:=True)
        Catch ex As IOException
        End Try
    End Sub
End Class

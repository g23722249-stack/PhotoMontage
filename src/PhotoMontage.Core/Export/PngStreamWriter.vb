Imports System.IO
Imports System.IO.Compression
Imports System.Text

''' <summary>
''' 逐列寫出 24 位元 RGB 的 PNG，不需要把整張影像放進記憶體。用於超大馬賽克的分段匯出。
''' 每列使用 Sub 濾波，資料以 zlib 壓縮後分成多個 IDAT 區塊。
''' </summary>
Public NotInheritable Class PngStreamWriter
    Implements IDisposable

    Private Shared ReadOnly Signature As Byte() = {137, 80, 78, 71, 13, 10, 26, 10}

    Private ReadOnly _output As Stream
    Private ReadOnly _width As Integer
    Private ReadOnly _height As Integer
    Private ReadOnly _idat As IdatStream
    Private ReadOnly _zlib As ZLibStream
    Private ReadOnly _row As Byte()
    Private _rowsWritten As Integer
    Private _finished As Boolean

    ''' <param name="dpi">寫入 pHYs 的解析度；0 表示不寫。</param>
    Public Sub New(output As Stream, width As Integer, height As Integer, Optional dpi As Integer = 0)
        If output Is Nothing Then Throw New ArgumentNullException(NameOf(output))
        If width < 1 Then Throw New ArgumentOutOfRangeException(NameOf(width))
        If height < 1 Then Throw New ArgumentOutOfRangeException(NameOf(height))
        _output = output
        _width = width
        _height = height
        _row = New Byte(width * 3) {} ' 1 個濾波位元組 + RGB

        output.Write(Signature, 0, Signature.Length)
        Using ihdr As New MemoryStream()
            WriteInt32(ihdr, width)
            WriteInt32(ihdr, height)
            ihdr.WriteByte(8)  ' 每色 8 位元
            ihdr.WriteByte(2)  ' RGB
            ihdr.WriteByte(0)  ' deflate
            ihdr.WriteByte(0)  ' 標準濾波
            ihdr.WriteByte(0)  ' 不交錯
            WriteChunk(output, "IHDR", ihdr.ToArray())
        End Using
        If dpi > 0 Then
            Dim ppm = CInt(Math.Round(dpi / 0.0254))
            Using phys As New MemoryStream()
                WriteInt32(phys, ppm)
                WriteInt32(phys, ppm)
                phys.WriteByte(1) ' 單位：公尺
                WriteChunk(output, "pHYs", phys.ToArray())
            End Using
        End If

        _idat = New IdatStream(output)
        _zlib = New ZLibStream(_idat, CompressionLevel.Fastest, leaveOpen:=True)
    End Sub

    Public ReadOnly Property RowsWritten As Integer
        Get
            Return _rowsWritten
        End Get
    End Property

    ''' <summary>寫入一列 RGB（長度至少 width × 3）。</summary>
    Public Sub WriteRowRgb(rgb As Byte(), offset As Integer)
        WriteRow(rgb, offset, bgr:=False)
    End Sub

    ''' <summary>寫入一列 BGR（GDI+ 24 位元格式）。</summary>
    Public Sub WriteRowBgr(bgr As Byte(), offset As Integer)
        WriteRow(bgr, offset, bgr:=True)
    End Sub

    Private Sub WriteRow(data As Byte(), offset As Integer, bgr As Boolean)
        If _finished Then Throw New InvalidOperationException("已完成寫入。")
        If _rowsWritten >= _height Then Throw New InvalidOperationException("列數超過影像高度。")
        If data.Length - offset < _width * 3 Then Throw New ArgumentException("資料長度不足一列。", NameOf(data))

        _row(0) = 1 ' Sub 濾波：每個位元組減去左邊同色版的位元組
        For x = 0 To _width - 1
            Dim s = offset + x * 3
            Dim r = data(If(bgr, s + 2, s)), g = data(s + 1), b = data(If(bgr, s, s + 2))
            Dim d = 1 + x * 3
            If x = 0 Then
                _row(d) = r : _row(d + 1) = g : _row(d + 2) = b
            Else
                Dim p = offset + (x - 1) * 3
                Dim pr = data(If(bgr, p + 2, p)), pg = data(p + 1), pb = data(If(bgr, p, p + 2))
                _row(d) = CByte((CInt(r) - pr) And &HFF)
                _row(d + 1) = CByte((CInt(g) - pg) And &HFF)
                _row(d + 2) = CByte((CInt(b) - pb) And &HFF)
            End If
        Next
        _zlib.Write(_row, 0, _row.Length)
        _rowsWritten += 1
    End Sub

    ''' <summary>完成檔案（寫入 IEND）。列數必須等於影像高度。</summary>
    Public Sub Finish()
        If _finished Then Return
        If _rowsWritten <> _height Then Throw New InvalidOperationException($"只寫入了 {_rowsWritten} / {_height} 列。")
        _zlib.Dispose()
        _idat.FlushChunk()
        WriteChunk(_output, "IEND", Array.Empty(Of Byte)())
        _output.Flush()
        _finished = True
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _zlib.Dispose()
    End Sub

    Private Shared Sub WriteInt32(s As Stream, value As Integer)
        s.WriteByte(CByte((value >> 24) And &HFF))
        s.WriteByte(CByte((value >> 16) And &HFF))
        s.WriteByte(CByte((value >> 8) And &HFF))
        s.WriteByte(CByte(value And &HFF))
    End Sub

    Friend Shared Sub WriteChunk(output As Stream, type As String, data As Byte())
        WriteChunk(output, type, data, data.Length)
    End Sub

    Friend Shared Sub WriteChunk(output As Stream, type As String, data As Byte(), length As Integer)
        Dim typeBytes = Encoding.ASCII.GetBytes(type)
        WriteInt32(output, length)
        output.Write(typeBytes, 0, 4)
        output.Write(data, 0, length)
        Dim crc = Crc32.Update(Crc32.Update(&HFFFFFFFFUI, typeBytes, 0, 4), data, 0, length) Xor &HFFFFFFFFUI
        WriteInt32(output, CInt(crc And &H7FFFFFFFUI) Or If((crc And &H80000000UI) <> 0, Integer.MinValue, 0))
    End Sub

    ''' <summary>把壓縮資料切成 64 KB 的 IDAT 區塊寫出。</summary>
    Private NotInheritable Class IdatStream
        Inherits Stream

        Private Const ChunkSize As Integer = 65536
        Private ReadOnly _output As Stream
        Private ReadOnly _buffer(ChunkSize - 1) As Byte
        Private _count As Integer

        Public Sub New(output As Stream)
            _output = output
        End Sub

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            While count > 0
                Dim n = Math.Min(count, ChunkSize - _count)
                Array.Copy(buffer, offset, _buffer, _count, n)
                _count += n
                offset += n
                count -= n
                If _count = ChunkSize Then FlushChunk()
            End While
        End Sub

        Public Sub FlushChunk()
            If _count = 0 Then Return
            WriteChunk(_output, "IDAT", _buffer, _count)
            _count = 0
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean = False
        Public Overrides ReadOnly Property CanSeek As Boolean = False
        Public Overrides ReadOnly Property CanWrite As Boolean = True
        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property
        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property
        Public Overrides Sub Flush()
        End Sub
        Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function
        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function
        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub
    End Class
End Class

''' <summary>PNG 使用的 CRC-32（多項式 0xEDB88320）。</summary>
Friend Module Crc32
    Private ReadOnly Table As UInteger() = BuildTable()

    Private Function BuildTable() As UInteger()
        Dim t(255) As UInteger
        For n = 0UI To 255UI
            Dim c = n
            For k = 0 To 7
                c = If((c And 1UI) <> 0, &HEDB88320UI Xor (c >> 1), c >> 1)
            Next
            t(CInt(n)) = c
        Next
        Return t
    End Function

    Public Function Update(crc As UInteger, data As Byte(), offset As Integer, count As Integer) As UInteger
        For i = offset To offset + count - 1
            crc = Table(CInt((crc Xor data(i)) And &HFFUI)) Xor (crc >> 8)
        Next
        Return crc
    End Function
End Module

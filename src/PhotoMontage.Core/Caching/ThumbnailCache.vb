Imports System.IO
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' 縮圖的兩層快取：記憶體（LRU）＋磁碟。磁碟快取是盡力而為，讀寫失敗只會當作沒有快取。
''' 執行緒安全，可多個編輯器共用。
''' </summary>
Public Class ThumbnailCache

    Public Const DefaultMemoryBudget As Long = 200L * 1024 * 1024
    Public Const DefaultDiskBudget As Long = 500L * 1024 * 1024

    Private Const FileExtension As String = ".thumb"
    Private Const Magic As Integer = &H43544D50 ' "PMTC"
    Private Const FormatVersion As Integer = 1

    Private ReadOnly _codec As IImageCodec
    Private ReadOnly _directory As String
    Private ReadOnly _memory As LruCache(Of String, CachedThumbnail)

    ''' <param name="directory">磁碟快取資料夾；Nothing 表示只用記憶體。</param>
    Public Sub New(codec As IImageCodec, directory As String, Optional memoryBudget As Long = DefaultMemoryBudget)
        If codec Is Nothing Then Throw New ArgumentNullException(NameOf(codec))
        _codec = codec
        _directory = directory
        _memory = New LruCache(Of String, CachedThumbnail)(memoryBudget, Function(t) t.ApproximateSize)
    End Sub

    ''' <summary>%LOCALAPPDATA%\PhotoMontage\cache</summary>
    Public Shared ReadOnly Property DefaultDirectory As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoMontage", "cache")
        End Get
    End Property

    Public ReadOnly Property Directory As String
        Get
            Return _directory
        End Get
    End Property

    ''' <summary>以完整路徑、檔案大小、最後修改時間產生快取鍵；照片被修改後鍵就會不同。</summary>
    Public Shared Function ComputeKey(fullPath As String, fileSize As Long, lastWriteTimeUtc As Date) As String
        Dim text = $"{fullPath.ToUpperInvariant()}|{fileSize}|{lastWriteTimeUtc.Ticks}"
        Dim hash = SHA256.HashData(Encoding.UTF8.GetBytes(text))
        Return Convert.ToHexString(hash, 0, 16).ToLowerInvariant()
    End Function

    ''' <summary>先找記憶體，再找磁碟；都沒有時回傳 Nothing。</summary>
    Public Function TryGet(key As String) As CachedThumbnail
        Dim entry As CachedThumbnail = Nothing
        If _memory.TryGet(key, entry) Then Return entry

        entry = ReadFromDisk(key)
        If entry IsNot Nothing Then _memory.Add(key, entry)
        Return entry
    End Function

    Public Sub Put(key As String, entry As CachedThumbnail)
        If entry Is Nothing Then Throw New ArgumentNullException(NameOf(entry))
        _memory.Add(key, entry)
        WriteToDisk(key, entry)
    End Sub

    ''' <summary>只清除記憶體中的快取。</summary>
    Public Sub ClearMemory()
        _memory.Clear()
    End Sub

    ''' <summary>磁碟快取超過 <paramref name="maxBytes"/> 時，從最久沒用到的開始刪除。回傳刪除的檔案數。</summary>
    Public Function TrimDisk(Optional maxBytes As Long = DefaultDiskBudget) As Integer
        If _directory Is Nothing OrElse Not IO.Directory.Exists(_directory) Then Return 0

        Dim files As List(Of FileInfo)
        Try
            files = New DirectoryInfo(_directory).
                EnumerateFiles("*" & FileExtension, SearchOption.AllDirectories).
                OrderByDescending(Function(f) f.LastWriteTimeUtc).
                ToList()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return 0
        End Try

        Dim total As Long = 0
        Dim deleted = 0
        For Each f In files
            total += f.Length
            If total <= maxBytes Then Continue For
            Try
                f.Delete()
                deleted += 1
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                ' 正被其他行程使用，下次再刪
            End Try
        Next
        Return deleted
    End Function

    Friend Function GetFilePath(key As String) As String
        Return Path.Combine(_directory, key.Substring(0, 2), key & FileExtension)
    End Function

    Private Function ReadFromDisk(key As String) As CachedThumbnail
        If _directory Is Nothing Then Return Nothing
        Dim file = GetFilePath(key)
        If Not IO.File.Exists(file) Then Return Nothing

        Try
            Dim entry As CachedThumbnail
            Using fs As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read Or FileShare.Delete),
                  reader As New BinaryReader(fs)
                If reader.ReadInt32() <> Magic OrElse reader.ReadInt32() <> FormatVersion Then Throw New InvalidDataException()
                Dim width = reader.ReadInt32()
                Dim height = reader.ReadInt32()
                Dim orientation = ExifOrientations.FromValue(reader.ReadInt32())
                Dim ticks = reader.ReadInt64()
                Dim length = reader.ReadInt32()
                If width <= 0 OrElse height <= 0 OrElse length <= 0 OrElse length > fs.Length Then Throw New InvalidDataException()
                Dim payload = reader.ReadBytes(length)
                If payload.Length <> length Then Throw New InvalidDataException()

                Dim taken As Date? = If(ticks = 0, CType(Nothing, Date?), New Date(ticks))
                entry = New CachedThumbnail(New ImageInfo(width, height, orientation, taken), _codec.Decode(payload))
            End Using
            Touch(file)
            Return entry
        Catch ex As Exception When TypeOf ex Is InvalidDataException OrElse TypeOf ex Is EndOfStreamException OrElse
                                   TypeOf ex Is ImageDecodeException OrElse TypeOf ex Is ArgumentException
            TryDelete(file) ' 損壞或被截斷的快取檔
            Return Nothing
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return Nothing
        End Try
    End Function

    Private Sub WriteToDisk(key As String, entry As CachedThumbnail)
        If _directory Is Nothing Then Return
        Dim file = GetFilePath(key)
        Dim temp = file & "." & Guid.NewGuid().ToString("N") & ".tmp"

        Try
            Dim payload = _codec.Encode(entry.Image)
            IO.Directory.CreateDirectory(Path.GetDirectoryName(file))
            Using fs As New FileStream(temp, FileMode.CreateNew, FileAccess.Write),
                  writer As New BinaryWriter(fs)
                writer.Write(Magic)
                writer.Write(FormatVersion)
                writer.Write(entry.Info.Width)
                writer.Write(entry.Info.Height)
                writer.Write(CInt(entry.Info.Orientation))
                writer.Write(If(entry.Info.DateTaken.HasValue, entry.Info.DateTaken.Value.Ticks, 0L))
                writer.Write(payload.Length)
                writer.Write(payload)
            End Using
            IO.File.Move(temp, file, overwrite:=True)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            TryDelete(temp)
        End Try
    End Sub

    Private Shared Sub Touch(file As String)
        Try
            IO.File.SetLastWriteTimeUtc(file, Date.UtcNow)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

    Private Shared Sub TryDelete(file As String)
        Try
            IO.File.Delete(file)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

End Class

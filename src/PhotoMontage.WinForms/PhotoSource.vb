''' <summary>
''' 「選取照片」畫面（<see cref="PhotoPickerDialog"/>）的資料來源：一棵可勾選的樹，節點有自己的照片。
''' iPhoto 提供相簿、攝影集、人物與 Dock；單獨執行時是磁碟上的資料夾（<see cref="FolderPhotoSource"/>）。
''' 由 <see cref="MontageOptions.PhotoSource"/> 傳入。
''' </summary>
Public Interface IPhotoSource
    ''' <summary>來源名稱，用在按鈕與視窗標題（例如「iPhoto」）。</summary>
    ReadOnly Property Title As String

    ''' <summary>最上層的節點。</summary>
    Function Roots() As IReadOnlyList(Of PhotoSourceNode)

    ''' <summary>開啟時先顯示的節點：由最上層往下各層的 <see cref="PhotoSourceNode.Key"/>；Nothing 或找不到時不選。</summary>
    ReadOnly Property StartPath As IReadOnlyList(Of String)
End Interface

''' <summary>來源樹的一個節點（相簿、資料夾、人物…）。照片與子節點在第一次用到時才讀取。</summary>
Public Class PhotoSourceNode
    Public Sub New(key As String, text As String)
        Me.Key = key
        Me.Text = text
    End Sub

    ''' <summary>同一層中不重複的識別字（例如相簿的路徑）。</summary>
    Public ReadOnly Property Key As String

    ''' <summary>顯示的名稱。</summary>
    Public Property Text As String

    ''' <summary>
    ''' 這個節點的照片（完整路徑，依顯示順序）。Nothing 表示它只是分組、本身沒有照片：
    ''' 勾選它等於勾選所有子節點。
    ''' </summary>
    Public Property Photos As Func(Of IReadOnlyList(Of String))

    ''' <summary>子節點；Nothing 表示沒有。第一次展開時才呼叫。</summary>
    Public Property Children As Func(Of IReadOnlyList(Of PhotoSourceNode))

    ''' <summary>可以整個勾選（例如整個相片庫太大時設為 False，只能勾裡面的相簿）。</summary>
    Public Property Checkable As Boolean = True

    ''' <summary>作品可以存到這裡時的資料夾（相簿、磁碟資料夾）；Nothing 表示不能。</summary>
    Public Property Folder As String

    ''' <summary>一開始就展開。</summary>
    Public Property Expanded As Boolean
End Class

''' <summary>磁碟上的資料夾：圖片、桌面與各磁碟機。勾選資料夾 = 裡面的照片（不含子資料夾）。</summary>
Public Class FolderPhotoSource
    Implements IPhotoSource

    Private ReadOnly _start As IReadOnlyList(Of String)

    ''' <param name="startFolder">開啟時先顯示的資料夾；Nothing 時不選。</param>
    Public Sub New(Optional startFolder As String = Nothing)
        If Not String.IsNullOrEmpty(startFolder) AndAlso IO.Directory.Exists(startFolder) Then
            ' 由磁碟機往下：C:\、C:\相片、C:\相片\2024 …
            Dim chain As New List(Of String)
            Dim d = New IO.DirectoryInfo(startFolder)
            While d IsNot Nothing
                chain.Insert(0, d.FullName)
                d = d.Parent
            End While
            _start = chain
        End If
    End Sub

    Public ReadOnly Property Title As String Implements IPhotoSource.Title
        Get
            Return "資料夾"
        End Get
    End Property

    Public ReadOnly Property StartPath As IReadOnlyList(Of String) Implements IPhotoSource.StartPath
        Get
            Return _start
        End Get
    End Property

    Public Function Roots() As IReadOnlyList(Of PhotoSourceNode) Implements IPhotoSource.Roots
        Dim list As New List(Of PhotoSourceNode)
        For Each special In {Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.Desktop}
            Dim path = Environment.GetFolderPath(special)
            If IO.Directory.Exists(path) Then list.Add(FolderNode(path, IO.Path.GetFileName(path), "★" & path))
        Next
        For Each drive In IO.DriveInfo.GetDrives()
            If Not drive.IsReady Then Continue For
            Dim label = drive.Name.TrimEnd("\"c)
            Try
                If Not String.IsNullOrEmpty(drive.VolumeLabel) Then label = $"{drive.VolumeLabel} ({label})"
            Catch ex As IO.IOException
            End Try
            Dim node = FolderNode(drive.RootDirectory.FullName, label, drive.RootDirectory.FullName)
            node.Checkable = False   ' 磁碟機根目錄通常不是照片
            list.Add(node)
        Next
        Return list
    End Function

    Private Shared Function FolderNode(path As String, text As String, key As String) As PhotoSourceNode
        Return New PhotoSourceNode(key, text) With {
            .Folder = path,
            .Photos = Function() PhotosIn(path),
            .Children = Function() SubFolders(path)}
    End Function

    Private Shared Function SubFolders(path As String) As IReadOnlyList(Of PhotoSourceNode)
        Dim list As New List(Of PhotoSourceNode)
        Try
            For Each d In New IO.DirectoryInfo(path).EnumerateDirectories().OrderBy(Function(x) x.Name, StringComparer.CurrentCultureIgnoreCase)
                If (d.Attributes And (IO.FileAttributes.Hidden Or IO.FileAttributes.System)) <> 0 Then Continue For
                list.Add(FolderNode(d.FullName, d.Name, d.FullName))
            Next
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
        End Try
        Return list
    End Function

    Private Shared Function PhotosIn(path As String) As IReadOnlyList(Of String)
        Try
            Return IO.Directory.EnumerateFiles(path).
                Where(Function(f) Core.ImageFormatSniffer.IsSupportedExtension(f)).
                OrderBy(Function(f) IO.Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase).ToList()
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
            Return Array.Empty(Of String)()
        End Try
    End Function
End Class

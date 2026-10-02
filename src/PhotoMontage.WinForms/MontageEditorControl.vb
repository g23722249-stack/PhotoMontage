Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 蒙太奇編輯器本體。可放進宿主自己的視窗，或由 <see cref="MontageEditor.ShowDialog"/> 以對話框開啟。
''' </summary>
''' <remarks>M0：只有照片清單與版型清單的骨架，畫布與匯出於 M2/M4 實作。</remarks>
Public Class MontageEditorControl
    Inherits UserControl

    Private Shared ReadOnly SupportedExtensions As String() = {".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"}

    Private ReadOnly _project As New MontageProject()
    Private _options As New MontageOptions()

    Private ReadOnly _photoList As ListBox
    Private ReadOnly _templateList As ListBox
    Private ReadOnly _canvas As Panel
    Private ReadOnly _addButton As Button
    Private ReadOnly _exportButton As Button

    ''' <summary>作品匯出成功後觸發。</summary>
    Public Event Exported As EventHandler(Of MontageExportedEventArgs)

    Public Sub New()
        AutoScaleMode = AutoScaleMode.Dpi

        _photoList = New ListBox() With {.Dock = DockStyle.Fill, .IntegralHeight = False, .AllowDrop = True}
        AddHandler _photoList.DragEnter, AddressOf OnPhotoDragEnter
        AddHandler _photoList.DragDrop, AddressOf OnPhotoDragDrop

        _templateList = New ListBox() With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DisplayMember = NameOf(CollageTemplate.Name)}
        For Each t In CollageTemplates.BuiltIn
            _templateList.Items.Add(t)
        Next
        AddHandler _templateList.SelectedIndexChanged, AddressOf OnTemplateChanged

        _canvas = New Panel() With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(48, 48, 48)}

        _addButton = New Button() With {.Text = "加入照片…", .Dock = DockStyle.Bottom, .Height = 32}
        AddHandler _addButton.Click, AddressOf OnAddPhotosClick

        _exportButton = New Button() With {.Text = "匯出…", .Dock = DockStyle.Bottom, .Height = 32, .Enabled = False}

        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 240, .Padding = New Padding(6)}
        left.Controls.Add(_photoList)
        left.Controls.Add(_addButton)

        Dim right As New Panel() With {.Dock = DockStyle.Right, .Width = 200, .Padding = New Padding(6)}
        right.Controls.Add(_templateList)
        right.Controls.Add(_exportButton)

        Controls.Add(_canvas)
        Controls.Add(right)
        Controls.Add(left)
    End Sub

    ''' <summary>目前編輯中的專案。</summary>
    Public ReadOnly Property Project As MontageProject
        Get
            Return _project
        End Get
    End Property

    Friend Sub Initialize(options As MontageOptions)
        _options = options
        _project.Mode = options.Mode
        LoadPhotos(options.InitialPhotos)

        Dim initial = CollageTemplates.Find(_project.Collage.TemplateId)
        If initial IsNot Nothing Then _templateList.SelectedItem = initial
    End Sub

    ''' <summary>加入照片。不支援的檔案與重複的路徑會被略過。</summary>
    Public Sub LoadPhotos(filePaths As IEnumerable(Of String))
        If filePaths Is Nothing Then Return

        For Each path In filePaths
            If Not IsSupported(path) Then Continue For
            If _project.Photos.Exists(Function(p) String.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase)) Then Continue For

            _project.Photos.Add(New PhotoAsset(path))
            _photoList.Items.Add(IO.Path.GetFileName(path))
        Next
    End Sub

    ''' <summary>通知宿主作品已匯出。</summary>
    Protected Overridable Sub OnExported(e As MontageExportedEventArgs)
        RaiseEvent Exported(Me, e)
    End Sub

    Private Shared Function IsSupported(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then Return False
        Return SupportedExtensions.Contains(IO.Path.GetExtension(path).ToLowerInvariant())
    End Function

    Private Sub OnTemplateChanged(sender As Object, e As EventArgs)
        Dim template = TryCast(_templateList.SelectedItem, CollageTemplate)
        If template Is Nothing Then Return

        _project.Collage.TemplateId = template.Id
        _project.Collage.Cells = template.CreateCells()
        _canvas.Invalidate()
    End Sub

    Private Sub OnAddPhotosClick(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog()
            dlg.Title = "選擇照片"
            dlg.Multiselect = True
            dlg.Filter = "圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有檔案|*.*"
            If dlg.ShowDialog(Me) = DialogResult.OK Then LoadPhotos(dlg.FileNames)
        End Using
    End Sub

    Private Sub OnPhotoDragEnter(sender As Object, e As DragEventArgs)
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
    End Sub

    Private Sub OnPhotoDragDrop(sender As Object, e As DragEventArgs)
        Dim files = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        LoadPhotos(files)
    End Sub
End Class

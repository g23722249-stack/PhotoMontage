Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 蒙太奇編輯器本體。可放進宿主自己的視窗，或由 <see cref="MontageEditor.ShowDialog"/> 以對話框開啟。
''' </summary>
''' <remarks>M1：照片匯入與縮圖清單；畫布與匯出於 M2/M4 實作。所有公開成員都必須在 UI 執行緒呼叫。</remarks>
Public Class MontageEditorControl
    Inherits UserControl

    Private ReadOnly _project As New MontageProject()
    Private ReadOnly _importer As PhotoImporter = ImportServices.CreateImporter()
    Private ReadOnly _pendingImports As New Queue(Of IReadOnlyList(Of String))
    Private ReadOnly _failures As New List(Of ImportFailure)
    Private _importCts As New CancellationTokenSource()
    Private _importing As Boolean
    Private _options As New MontageOptions()

    Private ReadOnly _strip As PhotoStrip
    Private ReadOnly _templateList As ListBox
    Private ReadOnly _canvas As Panel
    Private ReadOnly _exportButton As Button
    Private ReadOnly _progressPanel As Panel
    Private ReadOnly _progressBar As ProgressBar
    Private ReadOnly _progressLabel As Label
    Private ReadOnly _failureLink As LinkLabel

    ''' <summary>作品匯出成功後觸發。</summary>
    Public Event Exported As EventHandler(Of MontageExportedEventArgs)

    Public Sub New()
        AutoScaleMode = AutoScaleMode.Dpi

        _strip = New PhotoStrip() With {.Dock = DockStyle.Fill}
        AddHandler _strip.FilesDropped, Sub(s, e) AddPhotos(e.Paths)
        AddHandler _strip.ItemsRemoved, AddressOf OnPhotosRemoved
        AddHandler _strip.OrderChanged, Sub(s, e) SyncPhotoOrder()

        Dim addPhotosButton As New Button() With {.Text = "加入照片…", .Dock = DockStyle.Top, .Height = 32}
        AddHandler addPhotosButton.Click, AddressOf OnAddPhotosClick
        Dim addFolderButton As New Button() With {.Text = "加入資料夾…", .Dock = DockStyle.Top, .Height = 32}
        AddHandler addFolderButton.Click, AddressOf OnAddFolderClick

        _progressBar = New ProgressBar() With {.Dock = DockStyle.Top, .Height = 16}
        _progressLabel = New Label() With {.Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft}
        Dim cancel As New LinkLabel() With {.Text = "取消", .Dock = DockStyle.Right, .AutoSize = True, .TextAlign = ContentAlignment.MiddleRight}
        AddHandler cancel.LinkClicked, Sub(s, e) CancelImport()
        Dim progressRow As New Panel() With {.Dock = DockStyle.Fill}
        progressRow.Controls.Add(_progressLabel)
        progressRow.Controls.Add(cancel)
        _progressPanel = New Panel() With {.Dock = DockStyle.Bottom, .Height = 40, .Visible = False}
        _progressPanel.Controls.Add(progressRow)
        _progressPanel.Controls.Add(_progressBar)

        _failureLink = New LinkLabel() With {.Dock = DockStyle.Bottom, .Height = 24, .Visible = False, .LinkColor = Color.IndianRed}
        AddHandler _failureLink.LinkClicked, AddressOf OnFailureLinkClicked

        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 240, .Padding = New Padding(6)}
        left.Controls.Add(_strip)
        left.Controls.Add(addFolderButton)
        left.Controls.Add(addPhotosButton)
        left.Controls.Add(_failureLink)
        left.Controls.Add(_progressPanel)

        _templateList = New ListBox() With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DisplayMember = NameOf(CollageTemplate.Name)}
        For Each t In CollageTemplates.BuiltIn
            _templateList.Items.Add(t)
        Next
        AddHandler _templateList.SelectedIndexChanged, AddressOf OnTemplateChanged

        _canvas = New Panel() With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(48, 48, 48)}
        _exportButton = New Button() With {.Text = "匯出…", .Dock = DockStyle.Bottom, .Height = 32, .Enabled = False}

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

    ''' <summary>是否正在匯入照片。</summary>
    Public ReadOnly Property IsImporting As Boolean
        Get
            Return _importing
        End Get
    End Property

    Friend Sub Initialize(options As MontageOptions)
        _options = options
        _project.Mode = options.Mode

        Dim initial = CollageTemplates.Find(_project.Collage.TemplateId)
        If initial IsNot Nothing Then _templateList.SelectedItem = initial
    End Sub

    ''' <summary>
    ''' 加入照片或資料夾（含子資料夾）。立即返回，照片在背景讀取；
    ''' 重複的路徑會被略過，無法讀取的照片會統一列在清單下方。
    ''' </summary>
    Public Sub AddPhotos(paths As IEnumerable(Of String))
        If paths Is Nothing Then Return
        Dim list = paths.ToList()
        If list.Count = 0 Then Return

        _pendingImports.Enqueue(list)
        If Not _importing Then RunImportsAsync()
    End Sub

    ''' <summary>取消進行中與排隊中的匯入。已讀取完成的照片會保留。</summary>
    Public Sub CancelImport()
        _pendingImports.Clear()
        If _importing Then _importCts.Cancel()
    End Sub

    ''' <summary>通知宿主作品已匯出。</summary>
    Protected Overridable Sub OnExported(e As MontageExportedEventArgs)
        RaiseEvent Exported(Me, e)
    End Sub

#Region "匯入"

    Private Async Sub RunImportsAsync()
        _importing = True
        Try
            While _pendingImports.Count > 0 AndAlso Not IsDisposed
                Await ImportOneBatchAsync(_pendingImports.Dequeue())
            End While
        Catch ex As Exception When Not IsDisposed
            MessageBox.Show(Me, "匯入照片時發生錯誤：" & ex.Message, "蒙太奇相片", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            _importing = False
            If Not IsDisposed Then
                _progressPanel.Visible = False
                ResetCancellation()
            End If
        End Try
    End Sub

    Private Async Function ImportOneBatchAsync(paths As IReadOnlyList(Of String)) As Task
        Dim existing = _project.Photos.ToList()
        Dim maxCount = _importer.Limits.MaxPhotosFor(_project.Mode)
        Dim batch = Await Task.Run(Function() _importer.Prepare(paths, existing, maxCount))
        If IsDisposed Then Return

        ' 之前失敗的同一張照片，重新加入時取代舊項目
        Dim newPaths = New HashSet(Of String)(batch.Assets.Select(Function(a) a.FilePath), StringComparer.OrdinalIgnoreCase)
        Dim replaced = _project.Photos.Where(Function(p) p.Status = PhotoStatus.Failed AndAlso newPaths.Contains(p.FilePath)).ToList()
        If replaced.Count > 0 Then RemovePhotos(replaced)

        _project.Photos.AddRange(batch.Assets)
        _strip.AddRange(batch.Assets)
        AddFailures(batch.Failures)
        If batch.Assets.Count = 0 Then Return

        _progressBar.Maximum = batch.Assets.Count
        _progressBar.Value = 0
        _progressLabel.Text = $"讀取中 0 / {batch.Assets.Count}"
        _progressPanel.Visible = True

        Try
            Await _importer.ProcessAsync(batch, New Progress(Of ImportProgress)(AddressOf OnImportProgress), _importCts.Token)
        Catch ex As OperationCanceledException
            ' 移除還沒處理的照片，並換一個新的 token 給之後的匯入
            If IsDisposed Then Return
            RemovePhotos(batch.Assets.Where(Function(a) a.Status = PhotoStatus.Pending).ToList())
            ResetCancellation()
        End Try
    End Function

    Private Sub ResetCancellation()
        If Not _importCts.IsCancellationRequested Then Return
        _importCts.Dispose()
        _importCts = New CancellationTokenSource()
    End Sub

    Private Sub OnImportProgress(p As ImportProgress)
        If IsDisposed Then Return

        _progressBar.Value = Math.Min(p.Completed, _progressBar.Maximum)
        _progressLabel.Text = $"讀取中 {p.Completed} / {p.Total}"

        If p.Failure IsNot Nothing Then
            AddFailures({p.Failure})
            _strip.RefreshItem(p.Asset)
        ElseIf p.Thumbnail IsNot Nothing Then
            _strip.SetTile(p.Asset, BitmapConversion.ToBitmap(p.Thumbnail, _strip.TileSize))
        End If
    End Sub

    Private Sub AddFailures(failures As IEnumerable(Of ImportFailure))
        _failures.AddRange(failures)
        UpdateFailureLink()
    End Sub

    Private Sub UpdateFailureLink()
        _failureLink.Visible = _failures.Count > 0
        _failureLink.Text = $"{_failures.Count} 張無法匯入（查看）"
    End Sub

    Private Sub OnFailureLinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Const MaxLines = 30
        Dim lines = _failures.Take(MaxLines).Select(Function(f) f.ToString()).ToList()
        If _failures.Count > MaxLines Then lines.Add($"…其他 {_failures.Count - MaxLines} 張")

        Dim answer = MessageBox.Show(Me, String.Join(Environment.NewLine, lines) & Environment.NewLine & Environment.NewLine & "清除這份清單？",
                                     "無法匯入的照片", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
        If answer = DialogResult.Yes Then
            _failures.Clear()
            UpdateFailureLink()
        End If
    End Sub

    Private Sub OnAddPhotosClick(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog()
            dlg.Title = "選擇照片"
            dlg.Multiselect = True
            dlg.Filter = ImageFormatSniffer.DialogFilter
            If dlg.ShowDialog(Me) = DialogResult.OK Then AddPhotos(dlg.FileNames)
        End Using
    End Sub

    Private Sub OnAddFolderClick(sender As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog()
            dlg.Description = "選擇照片資料夾（含子資料夾）"
            dlg.UseDescriptionForTitle = True
            If dlg.ShowDialog(Me) = DialogResult.OK Then AddPhotos({dlg.SelectedPath})
        End Using
    End Sub

#End Region

#Region "照片清單同步"

    Private Sub OnPhotosRemoved(sender As Object, e As PhotosEventArgs)
        RemoveFromProject(e.Photos)
    End Sub

    Private Sub RemovePhotos(photos As IReadOnlyList(Of PhotoAsset))
        If photos.Count = 0 Then Return
        _strip.RemoveRange(photos)
        RemoveFromProject(photos)
    End Sub

    Private Sub RemoveFromProject(photos As IReadOnlyList(Of PhotoAsset))
        Dim ids = New HashSet(Of String)(photos.Select(Function(p) p.Id))
        _project.Photos.RemoveAll(Function(p) ids.Contains(p.Id))
        For Each cell In _project.Collage.Cells
            If cell.PhotoId IsNot Nothing AndAlso ids.Contains(cell.PhotoId) Then cell.PhotoId = Nothing
        Next
        _canvas.Invalidate()
    End Sub

    Private Sub SyncPhotoOrder()
        _project.Photos.Clear()
        _project.Photos.AddRange(_strip.Assets)
    End Sub

#End Region

    Private Sub OnTemplateChanged(sender As Object, e As EventArgs)
        Dim template = TryCast(_templateList.SelectedItem, CollageTemplate)
        If template Is Nothing Then Return

        _project.Collage.TemplateId = template.Id
        _project.Collage.Cells = template.CreateCells()
        _canvas.Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _pendingImports.Clear()
            _importCts.Cancel()
            _importCts.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

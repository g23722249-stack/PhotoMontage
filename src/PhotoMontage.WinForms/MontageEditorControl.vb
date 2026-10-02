Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 蒙太奇編輯器本體。可放進宿主自己的視窗，或由 <see cref="MontageEditor.ShowDialog"/> 以對話框開啟。
''' </summary>
''' <remarks>所有公開成員都必須在 UI 執行緒呼叫。</remarks>
Public Class MontageEditorControl
    Inherits UserControl

    Private ReadOnly _project As New MontageProject()
    Private ReadOnly _importer As PhotoImporter = ImportServices.CreateImporter()
    Private ReadOnly _pendingImports As New Queue(Of IReadOnlyList(Of String))
    Private ReadOnly _failures As New List(Of ImportFailure)
    Private _importCts As New CancellationTokenSource()
    Private _importing As Boolean
    Private _options As New MontageOptions()

    ''' <summary>放在格子裡的照片的預覽影像（由縮圖轉成的 GDI+ Bitmap），依照片 Id。</summary>
    Private ReadOnly _previewImages As New Dictionary(Of String, Bitmap)
    ''' <summary>照片清單變動後延遲更新版面，避免大量匯入時每張都重排。</summary>
    Private ReadOnly _layoutTimer As New System.Windows.Forms.Timer() With {.Interval = 250}
    Private _suppressTemplateEvents As Boolean
    Private ReadOnly _history As New UndoHistory()

    ''' <summary>背景圖的預覽影像（長邊 1600 px），以及它對應的路徑。</summary>
    Private _backgroundPath As String
    Private _backgroundImage As Bitmap
    Private Const BackgroundPreviewEdge As Integer = 1600

    Private ReadOnly _strip As PhotoStrip
    Private ReadOnly _templateList As ListBox
    Private ReadOnly _ratioCombo As ComboBox
    Private ReadOnly _canvas As CollageCanvas
    Private ReadOnly _exportButton As Button
    Private ReadOnly _stylePanel As StylePanel
    Private ReadOnly _textPanel As TextPanel
    Private ReadOnly _tabs As TabControl
    Private ReadOnly _textTab As TabPage
    Private ReadOnly _undoButton As ToolStripButton
    Private ReadOnly _redoButton As ToolStripButton
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
        AddHandler _strip.ItemActivated, AddressOf OnStripItemActivated
        AddHandler _layoutTimer.Tick, AddressOf OnLayoutTimerTick

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

        _ratioCombo = New ComboBox() With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
        For Each preset In CanvasPresets.All
            _ratioCombo.Items.Add(preset)
        Next
        AddHandler _ratioCombo.SelectedIndexChanged, AddressOf OnRatioChanged

        _templateList = New ListBox() With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DisplayMember = NameOf(CollageTemplate.Name)}
        _templateList.Items.Add(New CollageTemplate(CollageTemplates.AutoId, "自動排版（依照片）", Array.Empty(Of RectangleF)()))
        For Each t In CollageTemplates.BuiltIn
            _templateList.Items.Add(t)
        Next
        AddHandler _templateList.SelectedIndexChanged, AddressOf OnTemplateChanged

        Dim autoAssign As New Button() With {.Text = "重新自動分配", .Dock = DockStyle.Bottom, .Height = 32}
        AddHandler autoAssign.Click, Sub(s, e)
                                         RecordUndo()
                                         ApplyLayout(reassign:=True)
                                     End Sub

        _canvas = New CollageCanvas() With {
            .Dock = DockStyle.Fill, .Project = _project,
            .ImageProvider = AddressOf GetPreviewImage, .BackgroundImageProvider = AddressOf GetBackgroundImage}
        AddHandler _canvas.ChangeStarting, Sub(s, e) RecordUndo(e.Key)
        AddHandler _canvas.CellsChanged, Sub(s, e) OnCanvasCellsChanged()
        AddHandler _canvas.TextsChanged, Sub(s, e) OnCanvasTextsChanged()
        AddHandler _canvas.TextSelectionChanged, Sub(s, e) OnTextSelectionChanged()
        AddHandler _canvas.TextEditRequested, Sub(s, e)
                                                  _tabs.SelectedTab = _textTab
                                                  _textPanel.FocusText()
                                              End Sub
        AddHandler _canvas.FilesDropped, Sub(s, e) AddPhotos(e.Paths)

        _exportButton = New Button() With {.Text = "匯出…", .Dock = DockStyle.Bottom, .Height = 36, .Enabled = False}
        AddHandler _exportButton.Click, Sub(s, e) ShowExportDialog()

        ' 「版面」頁
        Dim ratioLabel As New Label() With {.Text = "畫布比例", .Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 2)}
        Dim templateLabel As New Label() With {.Text = "版型", .Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 8, 0, 2)}
        Dim layoutTab As New TabPage("版面") With {.Padding = New Padding(6)}
        layoutTab.Controls.Add(_templateList)
        layoutTab.Controls.Add(templateLabel)
        layoutTab.Controls.Add(_ratioCombo)
        layoutTab.Controls.Add(ratioLabel)
        layoutTab.Controls.Add(autoAssign)

        ' 「樣式」頁
        _stylePanel = New StylePanel() With {.Dock = DockStyle.Fill}
        AddHandler _stylePanel.ChangeStarting, Sub(s, e) RecordUndo(e.Key)
        AddHandler _stylePanel.DesignChanged, Sub(s, e) _canvas.Invalidate()
        AddHandler _stylePanel.BackgroundImageRequested, Sub(s, e) SetBackgroundImage(e.Paths(0))
        Dim styleTab As New TabPage("樣式")
        styleTab.Controls.Add(_stylePanel)

        ' 「文字」頁
        _textPanel = New TextPanel() With {.Dock = DockStyle.Fill}
        AddHandler _textPanel.ChangeStarting, Sub(s, e) RecordUndo(e.Key)
        AddHandler _textPanel.TextChangedByUser, Sub(s, e) _canvas.Invalidate()
        AddHandler _textPanel.AddRequested, Sub(s, e) AddText()
        AddHandler _textPanel.DeleteRequested, Sub(s, e) DeleteSelectedText()
        _textTab = New TabPage("文字")
        _textTab.Controls.Add(_textPanel)

        _tabs = New TabControl() With {.Dock = DockStyle.Fill}
        _tabs.TabPages.AddRange({layoutTab, styleTab, _textTab})

        Dim right As New Panel() With {.Dock = DockStyle.Right, .Width = 250, .Padding = New Padding(4)}
        right.Controls.Add(_tabs)
        right.Controls.Add(_exportButton)

        ' 工具列
        _undoButton = New ToolStripButton("復原") With {.Enabled = False, .ToolTipText = "復原（Ctrl+Z）"}
        AddHandler _undoButton.Click, Sub(s, e) Undo()
        _redoButton = New ToolStripButton("重做") With {.Enabled = False, .ToolTipText = "重做（Ctrl+Y）"}
        AddHandler _redoButton.Click, Sub(s, e) Redo()
        Dim addTextButton As New ToolStripButton("新增文字")
        AddHandler addTextButton.Click, Sub(s, e) AddText()
        Dim toolbar As New ToolStrip() With {.GripStyle = ToolStripGripStyle.Hidden, .Dock = DockStyle.Top}
        toolbar.Items.AddRange({_undoButton, _redoButton, New ToolStripSeparator(), addTextButton})
        AddHandler _history.Changed, Sub(s, e) UpdateUndoButtons()

        Controls.Add(_canvas)
        Controls.Add(right)
        Controls.Add(left)
        Controls.Add(toolbar)
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

        _suppressTemplateEvents = True
        _ratioCombo.SelectedItem = If(CanvasPresets.Find(_project.CanvasSize), CanvasPresets.All(0))
        _templateList.SelectedItem = _templateList.Items.Cast(Of CollageTemplate)().
            FirstOrDefault(Function(t) t.Id = _project.Collage.TemplateId)
        _suppressTemplateEvents = False
        _stylePanel.Bind(_project)
        ApplyLayout(reassign:=True)
        _history.Clear()
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
            ScheduleLayout()
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
        OnCellsChanged()
        ScheduleLayout()
    End Sub

    Private Sub SyncPhotoOrder()
        _project.Photos.Clear()
        _project.Photos.AddRange(_strip.Assets)
        ' 自動排版依照片順序；固定版型不動使用者已排好的位置
        If IsAutoLayout Then ScheduleLayout()
    End Sub

    ''' <summary>雙擊縮圖：放進第一個空格；已在畫布上則不動。</summary>
    Private Sub OnStripItemActivated(sender As Object, e As PhotosEventArgs)
        Dim asset = e.Photos(0)
        If asset.Status <> PhotoStatus.Ready OrElse IsAutoLayout Then Return
        Dim cells = _project.Collage.Cells
        If cells.Exists(Function(c) c.PhotoId = asset.Id) Then Return
        Dim target = cells.FindIndex(Function(c) c.PhotoId Is Nothing)
        If target < 0 Then Return
        PhotoAssignment.Place(cells, target, asset.Id)
        OnCellsChanged()
    End Sub

#End Region

#Region "版面"

    Private ReadOnly Property IsAutoLayout As Boolean
        Get
            Return _project.Collage.TemplateId = CollageTemplates.AutoId
        End Get
    End Property

    Private ReadOnly Property CanvasSizeF As SizeF
        Get
            Return New SizeF(_project.CanvasSize.Width, _project.CanvasSize.Height)
        End Get
    End Property

    Private Sub OnTemplateChanged(sender As Object, e As EventArgs)
        If _suppressTemplateEvents Then Return
        Dim template = TryCast(_templateList.SelectedItem, CollageTemplate)
        If template Is Nothing Then Return

        RecordUndo()
        _project.Collage.TemplateId = template.Id
        ApplyLayout(reassign:=True)
        _canvas.ResetInteraction()
    End Sub

    Private Sub OnRatioChanged(sender As Object, e As EventArgs)
        If _suppressTemplateEvents Then Return
        Dim preset = TryCast(_ratioCombo.SelectedItem, CanvasPreset)
        If preset Is Nothing Then Return

        RecordUndo()
        _project.CanvasSize = preset.SizeFor(CanvasPresets.DefaultLongEdge)
        ' 格子形狀改變：自動排版重排；固定版型保留位置，只重設取景
        If IsAutoLayout Then
            ApplyLayout(reassign:=True)
        Else
            For Each c In _project.Collage.Cells
                c.Crop = New CropInfo()
            Next
            _canvas.ResetInteraction()
            OnCellsChanged()
        End If
    End Sub

    ''' <summary>節流：最多每 250 ms 更新一次版面，大量匯入時畫布也會陸續出現照片。</summary>
    Private Sub ScheduleLayout()
        If Not _layoutTimer.Enabled Then _layoutTimer.Start()
    End Sub

    ''' <summary>
    ''' 使用者在畫布上換位或放入照片。自動排版的格子是依照片順序產生的，
    ''' 所以把新的位置順序寫回照片清單，再依新順序重排。
    ''' </summary>
    Private Sub OnCanvasCellsChanged()
        If IsAutoLayout AndAlso SyncPhotoOrderFromCells() Then
            ApplyLayout(reassign:=True)
        Else
            OnCellsChanged()
        End If
    End Sub

    ''' <summary>
    ''' 自動排版時，把格子的照片順序寫回照片清單（換位、復原都會改變格子順序）。有改變時回傳 True。
    ''' </summary>
    Private Function SyncPhotoOrderFromCells() As Boolean
        Dim cellOrder = _project.Collage.Cells.Select(Function(c) c.PhotoId).ToList()
        Dim readyOrder = _project.Photos.Where(Function(p) p.Status = PhotoStatus.Ready).Select(Function(p) p.Id).ToList()
        If cellOrder.SequenceEqual(readyOrder) Then Return False

        Dim byId = _project.Photos.ToDictionary(Function(p) p.Id)
        Dim placed = cellOrder.Where(Function(id) id IsNot Nothing AndAlso byId.ContainsKey(id)).Select(Function(id) byId(id)).ToList()
        _strip.SetOrder(placed)
        _project.Photos.Clear()
        _project.Photos.AddRange(_strip.Assets)
        Return True
    End Function

    Private Sub OnLayoutTimerTick(sender As Object, e As EventArgs)
        _layoutTimer.Stop()
        If IsDisposed Then Return
        ' 自動排版：照片變了就整個重排；固定版型：只把新照片補進空格
        ApplyLayout(reassign:=IsAutoLayout)
    End Sub

    ''' <summary>依目前版型更新格子。<paramref name="reassign"/> 為 True 時重建格子並重新分配所有照片。</summary>
    Private Sub ApplyLayout(reassign As Boolean)
        _layoutTimer.Stop()
        Dim settings = _project.Collage

        If IsAutoLayout Then
            ' 保留每張照片的取景（相對值，格子形狀改變後仍然有效）
            Dim crops = settings.Cells.Where(Function(c) c.PhotoId IsNot Nothing).
                GroupBy(Function(c) c.PhotoId).ToDictionary(Function(g) g.Key, Function(g) g.First().Crop)
            Dim ready = _project.Photos.Where(Function(p) p.Status = PhotoStatus.Ready).ToList()
            Dim template = JustifiedLayout.Create(ready.Select(Function(p) p.AspectRatio).ToList(), _project.CanvasAspect)
            settings.Cells = template.CreateCells()
            For i = 0 To ready.Count - 1
                settings.Cells(i).PhotoId = ready(i).Id
                Dim crop As CropInfo = Nothing
                If crops.TryGetValue(ready(i).Id, crop) Then settings.Cells(i).Crop = crop
            Next
        ElseIf reassign Then
            Dim template = CollageTemplates.Find(settings.TemplateId)
            If template Is Nothing Then Return
            settings.Cells = template.CreateCells()
            PhotoAssignment.AssignAll(settings.Cells, _project.Photos, CanvasSizeF)
        Else
            PhotoAssignment.FillEmpty(settings.Cells, _project.Photos, CanvasSizeF)
        End If

        _canvas.AllowClear = Not IsAutoLayout
        If reassign AndAlso Not IsAutoLayout Then _canvas.ResetInteraction() Else _canvas.ClampInteraction()
        OnCellsChanged()
    End Sub

    ''' <summary>格子內容改變後：更新縮圖勾選標記、釋放不再使用的預覽影像、重繪。</summary>
    Private Sub OnCellsChanged()
        Dim used = New HashSet(Of String)(_project.Collage.Cells.Where(Function(c) c.PhotoId IsNot Nothing).Select(Function(c) c.PhotoId))
        For Each id In _previewImages.Keys.Where(Function(k) Not used.Contains(k)).ToList()
            _previewImages(id).Dispose()
            _previewImages.Remove(id)
        Next
        _strip.UsedPhotoIds = used
        _exportButton.Enabled = used.Count > 0
        _canvas.Invalidate()
    End Sub

    ''' <summary>畫布要用的預覽影像：由縮圖快取轉成 Bitmap，放在格子裡的期間保留。</summary>
    Private Function GetPreviewImage(asset As PhotoAsset) As Image
        Dim bmp As Bitmap = Nothing
        If _previewImages.TryGetValue(asset.Id, bmp) Then Return bmp

        Try
            bmp = BitmapConversion.ToBitmap(_importer.LoadThumbnail(asset))
        Catch ex As IO.IOException
            Return Nothing ' 原圖已被移走且快取也沒有
        End Try
        _previewImages(asset.Id) = bmp
        Return bmp
    End Function

#End Region

#Region "匯出"

    ''' <summary>開啟匯出對話框。匯出成功時觸發 <see cref="Exported"/> 並回傳檔案路徑；取消時回傳 Nothing。</summary>
    Public Function ShowExportDialog() As String
        If Not _project.Collage.Cells.Exists(Function(c) c.PhotoId IsNot Nothing) Then Return Nothing

        If _importing AndAlso MessageBox.Show(Me, "還有照片正在讀取中，讀取中的照片不會出現在作品裡。仍要匯出嗎？", "匯出",
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return Nothing

        Dim result As ExportResult
        Using dlg As New ExportDialog(_project, New CollageExporter(_importer), _options.DefaultExportFolder)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.Result Is Nothing Then Return Nothing
            result = dlg.Result
        End Using

        If result.Warnings.Count > 0 Then
            MessageBox.Show(Me, "作品已匯出，但有以下問題：" & vbLf & vbLf & String.Join(vbLf, result.Warnings.Take(20)),
                            "匯出", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        If _options.ShowExportCompletedMessage Then ShowExportCompleted(result.OutputPath)

        OnExported(New MontageExportedEventArgs(result.OutputPath))
        Return result.OutputPath
    End Function

    Private Sub ShowExportCompleted(path As String)
        Dim answer = MessageBox.Show(Me, $"已儲存：{vbLf}{path}{vbLf}{vbLf}要開啟檔案所在的資料夾嗎？", "匯出完成",
                                     MessageBoxButtons.YesNo, MessageBoxIcon.Information)
        If answer <> DialogResult.Yes Then Return
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select," & ChrW(34) & path & ChrW(34)) With {.UseShellExecute = True})
        Catch ex As System.ComponentModel.Win32Exception
        End Try
    End Sub

#End Region

#Region "復原／重做"

    Private Sub RecordUndo(Optional key As String = Nothing)
        _history.Record(_project, key)
    End Sub

    Public Sub Undo()
        If _history.Undo(_project) Then AfterHistoryRestore()
    End Sub

    Public Sub Redo()
        If _history.Redo(_project) Then AfterHistoryRestore()
    End Sub

    Private Sub UpdateUndoButtons()
        _undoButton.Enabled = _history.CanUndo
        _redoButton.Enabled = _history.CanRedo
    End Sub

    ''' <summary>復原／重做套用快照後，把畫面同步回專案狀態。</summary>
    Private Sub AfterHistoryRestore()
        _suppressTemplateEvents = True
        _ratioCombo.SelectedItem = If(CanvasPresets.Find(_project.CanvasSize), _ratioCombo.SelectedItem)
        _templateList.SelectedItem = _templateList.Items.Cast(Of CollageTemplate)().
            FirstOrDefault(Function(t) t.Id = _project.Collage.TemplateId)
        _suppressTemplateEvents = False

        If IsAutoLayout Then
            ' 先依快照中的格子順序調整照片順序，再依目前的照片重建格子（快照中的取景會被保留）
            SyncPhotoOrderFromCells()
            ApplyLayout(reassign:=True)
        Else
            _canvas.AllowClear = True
            _canvas.ClampInteraction()
            OnCellsChanged()
        End If
        _stylePanel.RefreshFromProject()
        OnTextSelectionChanged()
    End Sub

    ''' <summary>Ctrl+Z／Ctrl+Y；輸入框有焦點時交給輸入框自己的復原。</summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If TypeOf FocusedControl() IsNot TextBoxBase Then
            Select Case keyData
                Case Keys.Control Or Keys.Z
                    Undo()
                    Return True
                Case Keys.Control Or Keys.Y, Keys.Control Or Keys.Shift Or Keys.Z
                    Redo()
                    Return True
            End Select
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Function FocusedControl() As Control
        Dim c As Control = Me
        Do
            Dim container = TryCast(c, ContainerControl)
            If container Is Nothing OrElse container.ActiveControl Is Nothing Then Return c
            c = container.ActiveControl
        Loop
    End Function

#End Region

#Region "文字"

    Private Sub AddText()
        RecordUndo()
        Dim offset = (_project.Texts.Count Mod 5) * 0.08F
        Dim layer As New TextLayer With {
            .Text = "輸入文字",
            .FontSize = 0.08F,
            .Bold = True,
            .OutlineWidth = 0.04F,
            .Position = New PointF(0.5F, Math.Min(0.9F, 0.5F + offset))}
        _project.Texts.Add(layer)
        _canvas.SelectedTextId = layer.Id
        OnTextSelectionChanged()
        _tabs.SelectedTab = _textTab
        _textPanel.FocusText()
    End Sub

    Private Sub DeleteSelectedText()
        Dim layer = _canvas.SelectedText
        If layer Is Nothing Then Return
        RecordUndo()
        _project.Texts.Remove(layer)
        _canvas.SelectedTextId = Nothing
        OnTextSelectionChanged()
    End Sub

    Private Sub OnTextSelectionChanged()
        Dim layer = _canvas.SelectedText
        _textPanel.Bind(layer)
        If layer IsNot Nothing Then _tabs.SelectedTab = _textTab
        _canvas.Invalidate()
    End Sub

    Private Sub OnCanvasTextsChanged()
        If _canvas.SelectedText Is Nothing Then
            _textPanel.Bind(Nothing)
        Else
            _textPanel.RefreshFromLayer()
        End If
    End Sub

#End Region

#Region "背景圖"

    Private Sub SetBackgroundImage(path As String)
        Dim bmp As Bitmap
        Try
            bmp = BitmapConversion.ToBitmap(_importer.DecodeFile(path, BackgroundPreviewEdge))
        Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            MessageBox.Show(Me, "無法讀取這張圖片：" & ex.Message, "背景圖", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        RecordUndo()
        _backgroundImage?.Dispose()
        _backgroundImage = bmp
        _backgroundPath = path
        _project.BackgroundImagePath = path
        _stylePanel.RefreshFromProject()
        _canvas.Invalidate()
    End Sub

    ''' <summary>畫布要用的背景圖；路徑改變（例如復原）時重新讀取，讀不到就當作沒有背景圖。</summary>
    Private Function GetBackgroundImage() As Image
        Dim path = _project.BackgroundImagePath
        If path = _backgroundPath Then Return _backgroundImage

        _backgroundImage?.Dispose()
        _backgroundImage = Nothing
        _backgroundPath = path
        If path IsNot Nothing Then
            Try
                _backgroundImage = BitmapConversion.ToBitmap(_importer.DecodeFile(path, BackgroundPreviewEdge))
            Catch ex As Exception When TypeOf ex Is IO.IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ImageDecodeException
            End Try
        End If
        Return _backgroundImage
    End Function

#End Region

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _pendingImports.Clear()
            _importCts.Cancel()
            _importCts.Dispose()
            _layoutTimer.Dispose()
            For Each bmp In _previewImages.Values
                bmp.Dispose()
            Next
            _previewImages.Clear()
            _backgroundImage?.Dispose()
            _backgroundImage = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

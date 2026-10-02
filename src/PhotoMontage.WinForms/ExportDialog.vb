Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>匯出設定與進度對話框（Aqua 視窗）。匯出在背景執行，可取消。</summary>
Friend Class ExportDialog
    Inherits Aqua.AquaForm

    ''' <summary>同一個行程內記住上次的設定。</summary>
    Private Shared _lastPresetIndex As Integer = 1
    Private Shared _lastCustomEdge As Integer = 3000
    Private Shared _lastFormat As ExportFormat = ExportFormat.Jpeg
    Private Shared _lastQuality As Integer = 92
    Private Shared _lastFolder As String

    Private ReadOnly _project As MontageProject
    Private ReadOnly _exporter As MontageExporter
    Private _cts As CancellationTokenSource
    Private _initializing As Boolean = True

    ''' <summary>順序與 <see cref="ExportPreset.All"/> 相同。</summary>
    Private ReadOnly _preset As Aqua.DropDownList
    ''' <summary>自訂長邊，以 100 像素為單位。</summary>
    Private ReadOnly _customEdge As LabeledSlider
    Private ReadOnly _sizeLabel As System.Windows.Forms.Label
    ''' <summary>0 = JPEG、1 = PNG。</summary>
    Private ReadOnly _format As SegmentedChoice
    Private ReadOnly _quality As LabeledSlider
    Private ReadOnly _path As Aqua.TextBox
    Private ReadOnly _browse As PillButton
    Private ReadOnly _progress As Aqua.ProgressBar
    Private ReadOnly _status As System.Windows.Forms.Label
    Private ReadOnly _exportButton As PillButton
    Private ReadOnly _cancelButton As PillButton

    ''' <summary>匯出成功後的結果；未匯出時為 Nothing。</summary>
    Public Property Result As ExportResult

    Public Sub New(project As MontageProject, exporter As MontageExporter, defaultFolder As String,
                   Optional accent As Aqua.ColorConstants = Aqua.ColorConstants.Blue)
        _project = project
        _exporter = exporter

        Text = "匯出作品"
        TitleFont = New Font("Microsoft JhengHei UI", 10.0F, FontStyle.Bold)
        MinButton = False
        MaxButton = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(480, 440)

        Dim layout As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoSize = True, .BackColor = Color.Transparent}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        _preset = New Aqua.DropDownList() With {.Dock = DockStyle.Fill}
        For Each p In ExportPreset.All
            _preset.AddItem(p.Name, p.Name)
        Next
        AddHandler _preset.SelectedChanged, Sub(s, e) UpdateSize()
        AddRow(layout, "解析度", _preset)

        _customEdge = New LabeledSlider(ExportPlanner.MinLongEdge \ 100 + 1, ExportPlanner.MaxLongEdge \ 100, Function(v) $"{v * 100}") With {.Dock = DockStyle.Fill}
        AddHandler _customEdge.ValueChanged, Sub(s, e) UpdateSize()
        AddRow(layout, "自訂長邊", _customEdge)

        _sizeLabel = New System.Windows.Forms.Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(360, 0), .BackColor = Color.Transparent}
        AddRow(layout, "輸出尺寸", _sizeLabel)

        _format = New SegmentedChoice("JPEG（檔案小）", "PNG（無損）") With {.Width = 240}
        AddHandler _format.SelectedChanged, Sub(s, e) OnFormatChanged()
        AddRow(layout, "格式", _format)

        _quality = New LabeledSlider(50, 100, Function(v) v.ToString()) With {.Dock = DockStyle.Fill}
        AddRow(layout, "JPEG 品質", _quality)

        _path = New Aqua.TextBox() With {.Dock = DockStyle.Fill}
        _browse = New PillButton() With {.Text = "瀏覽…", .Width = 80, .Dock = DockStyle.Right}
        AddHandler _browse.Click, AddressOf OnBrowse
        Dim pathRow As New System.Windows.Forms.Panel() With {.Dock = DockStyle.Fill, .Height = PillButton.DefaultHeight, .BackColor = Color.Transparent}
        pathRow.Controls.Add(_path)
        pathRow.Controls.Add(New System.Windows.Forms.Panel() With {.Dock = DockStyle.Right, .Width = 6, .BackColor = Color.Transparent})
        pathRow.Controls.Add(_browse)
        AddRow(layout, "儲存位置", pathRow)

        _progress = New Aqua.ProgressBar() With {.Dock = DockStyle.Fill, .Visible = False}
        _status = New System.Windows.Forms.Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(360, 0), .BackColor = Color.Transparent}
        AddRow(layout, "", _progress)
        AddRow(layout, "", _status)

        _exportButton = New PillButton() With {.Text = "匯出", .Width = 90}
        AddHandler _exportButton.Click, AddressOf OnExportClick
        _cancelButton = New PillButton() With {.Text = "取消", .Width = 90}
        AddHandler _cancelButton.Click, AddressOf OnCancelClick
        Dim buttons As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .BackColor = Color.Transparent}
        buttons.Controls.Add(_cancelButton)
        buttons.Controls.Add(_exportButton)

        Controls.Add(layout)
        Controls.Add(buttons)

        ' 還原上次設定
        _preset.SelectedIndex = Math.Min(_lastPresetIndex, ExportPreset.All.Count - 1)
        _customEdge.Value = _lastCustomEdge \ 100
        _format.SelectedIndex = If(_lastFormat = ExportFormat.Png, 1, 0)
        _quality.Value = _lastQuality
        Dim folder = If(_lastFolder, If(String.IsNullOrWhiteSpace(defaultFolder), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), defaultFolder))
        _path.Text = Path.Combine(folder, ExportPlanner.DefaultFileName(SelectedFormat, Date.Now))
        _initializing = False
        UpdateSize()
        OnFormatChanged()

        AquaTheme.Apply(Me, accent)
    End Sub

    ''' <summary>內容放在 Aqua 標題列下方。在自動縮放之後設定，確保是實際像素。</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Padding = New Padding(12, MontageEditorForm.AquaTitleBarHeight + 10, 12, 12)
    End Sub

    Private Shared Sub AddRow(layout As TableLayoutPanel, label As String, control As Control)
        Dim row = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New System.Windows.Forms.Label() With {.Text = label, .AutoSize = True, .Padding = New Padding(0, 6, 0, 0), .BackColor = Color.Transparent}, 0, row)
        layout.Controls.Add(control, 1, row)
    End Sub

    Private ReadOnly Property SelectedPreset As ExportPreset
        Get
            Dim i = _preset.SelectedIndex
            Return If(i >= 0 AndAlso i < ExportPreset.All.Count, ExportPreset.All(i), Nothing)
        End Get
    End Property

    Private ReadOnly Property SelectedFormat As ExportFormat
        Get
            Return If(_format.SelectedIndex = 1, ExportFormat.Png, ExportFormat.Jpeg)
        End Get
    End Property

    Private Function BuildSettings() As ExportSettings
        Dim preset = SelectedPreset
        Return New ExportSettings With {
            .LongEdge = If(preset.IsCustom, _customEdge.Value * 100, preset.LongEdge),
            .Dpi = preset.Dpi,
            .Format = SelectedFormat,
            .JpegQuality = _quality.Value,
            .FilePath = ExportPlanner.EnsureExtension(If(_path.Text, "").Trim(), SelectedFormat)}
    End Function

    Private Sub UpdateSize()
        If _initializing OrElse SelectedPreset Is Nothing Then Return
        _customEdge.Enabled = SelectedPreset.IsCustom AndAlso _cts Is Nothing
        Dim settings = BuildSettings()
        Dim size = settings.GetOutputSize(_project.CanvasAspect)
        Dim mosaic = _project.Mode = MontageMode.Mosaic
        Dim invalid = ExportPlanner.ValidateOutputSize(size, settings.Format, _project.Mode)

        Dim text = $"{size.Width} × {size.Height} 像素（{size.Width * CLng(size.Height) / 1_000_000.0:0.#} 百萬像素）"
        If mosaic Then text &= $"{vbLf}每格約 {size.Width / Math.Max(1, _project.Mosaic.Columns):0} 像素"
        If settings.Dpi >= 300 Then text &= $"{vbLf}列印約 {size.Width / settings.Dpi * 2.54:0.#} × {size.Height / settings.Dpi * 2.54:0.#} 公分"
        If invalid IsNot Nothing Then
            text &= vbLf & invalid
        ElseIf Not (mosaic AndAlso settings.Format = ExportFormat.Png) AndAlso Not Environment.Is64BitProcess AndAlso CLng(size.Width) * size.Height > 30_000_000L Then
            text &= vbLf & "解析度很高，在 32 位元模式下可能記憶體不足。"
        End If
        _sizeLabel.Text = text
        _sizeLabel.ForeColor = If(invalid IsNot Nothing, Color.Firebrick, SystemColors.ControlText)
        _exportButton.Enabled = invalid Is Nothing AndAlso _cts Is Nothing
    End Sub

    Private Sub OnFormatChanged()
        If _initializing Then Return
        _quality.Enabled = SelectedFormat = ExportFormat.Jpeg AndAlso _cts Is Nothing
        If Not String.IsNullOrEmpty(_path.Text) Then _path.Text = ExportPlanner.EnsureExtension(_path.Text, SelectedFormat)
        UpdateSize()
    End Sub

    Private Sub OnBrowse(sender As Object, e As EventArgs)
        Using dlg As New SaveFileDialog()
            dlg.Title = "儲存作品"
            dlg.Filter = "JPEG 圖片|*.jpg;*.jpeg|PNG 圖片|*.png"
            dlg.FilterIndex = If(SelectedFormat = ExportFormat.Png, 2, 1)
            dlg.FileName = Path.GetFileName(_path.Text)
            Try
                dlg.InitialDirectory = Path.GetDirectoryName(_path.Text)
            Catch ex As ArgumentException
            End Try
            dlg.OverwritePrompt = True
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _path.Text = dlg.FileName
            _format.SelectedIndex = If(dlg.FilterIndex = 2, 1, 0)
        End Using
    End Sub

    Private Async Sub OnExportClick(sender As Object, e As EventArgs)
        If _cts IsNot Nothing Then Return
        Dim settings = BuildSettings()
        If String.IsNullOrWhiteSpace(settings.FilePath) OrElse Not Path.IsPathFullyQualified(settings.FilePath) Then
            MessageBox.Show(Me, "請選擇儲存位置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If File.Exists(settings.FilePath) AndAlso
           MessageBox.Show(Me, $"「{Path.GetFileName(settings.FilePath)}」已存在，要取代嗎？", Text,
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        RememberSettings(settings)
        _cts = New CancellationTokenSource()
        SetRunning(True)
        Dim progress As New Progress(Of ExportProgress)(
            Sub(p)
                _progress.Maximum = Math.Max(1, p.Total)
                _progress.Value = Math.Min(p.Completed, _progress.Maximum)
                _status.Text = p.Message
            End Sub)

        Try
            Dim token = _cts.Token
            Result = Await Task.Run(Function() _exporter.Export(_project, settings, progress, token))
            _cts.Dispose()
            _cts = Nothing
            DialogResult = DialogResult.OK
            Close()
        Catch ex As OperationCanceledException
            _status.Text = "已取消。"
        Catch ex As Exception When TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ArgumentException
            ' GDI+ 配置大型 Bitmap 失敗時常擲出 ArgumentException（參數無效）
            ShowError("記憶體不足，無法產生這麼大的影像。請降低解析度後再試一次。")
        Catch ex As Exception
            ' Async Sub 中未處理的例外會讓整個程式（含宿主）結束，所以一律攔下
            ShowError("匯出失敗：" & ex.Message)
        Finally
            _cts?.Dispose()
            _cts = Nothing
            If Not IsDisposed Then SetRunning(False)
        End Try
    End Sub

    Private Sub ShowError(message As String)
        _status.Text = message
        MessageBox.Show(Me, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

    Private Sub OnCancelClick(sender As Object, e As EventArgs)
        If _cts IsNot Nothing Then
            _cts.Cancel()
            _status.Text = "取消中…"
        Else
            DialogResult = DialogResult.Cancel
            Close()
        End If
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' 匯出中按標題列的關閉鈕 → 先取消，等背景工作結束
        If _cts IsNot Nothing Then
            e.Cancel = True
            _cts.Cancel()
            _status.Text = "取消中…"
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Private Sub SetRunning(running As Boolean)
        For Each c In New Control() {_preset, _format, _path, _browse}
            c.Enabled = Not running
        Next
        _customEdge.Enabled = Not running AndAlso SelectedPreset.IsCustom
        _quality.Enabled = Not running AndAlso SelectedFormat = ExportFormat.Jpeg
        _progress.Visible = running
        _exportButton.Enabled = Not running
        If running Then _status.Text = "準備中…" Else UpdateSize()
    End Sub

    Private Sub RememberSettings(settings As ExportSettings)
        _lastPresetIndex = _preset.SelectedIndex
        _lastCustomEdge = _customEdge.Value * 100
        _lastFormat = settings.Format
        _lastQuality = settings.JpegQuality
        _lastFolder = Path.GetDirectoryName(settings.FilePath)
    End Sub
End Class

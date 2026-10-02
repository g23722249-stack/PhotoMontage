Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>匯出設定與進度對話框。匯出在背景執行，可取消。</summary>
Friend Class ExportDialog
    Inherits Form

    ''' <summary>同一個行程內記住上次的設定。</summary>
    Private Shared _lastPresetIndex As Integer = 1
    Private Shared _lastCustomEdge As Integer = 3000
    Private Shared _lastFormat As ExportFormat = ExportFormat.Jpeg
    Private Shared _lastQuality As Integer = 92
    Private Shared _lastFolder As String

    Private ReadOnly _project As MontageProject
    Private ReadOnly _exporter As CollageExporter
    Private _cts As CancellationTokenSource

    Private ReadOnly _preset As ComboBox
    Private ReadOnly _customEdge As NumericUpDown
    Private ReadOnly _sizeLabel As Label
    Private ReadOnly _jpeg As RadioButton
    Private ReadOnly _png As RadioButton
    Private ReadOnly _quality As LabeledSlider
    Private ReadOnly _path As TextBox
    Private ReadOnly _browse As Button
    Private ReadOnly _progress As ProgressBar
    Private ReadOnly _status As Label
    Private ReadOnly _exportButton As Button
    Private ReadOnly _cancelButton As Button

    ''' <summary>匯出成功後的結果；未匯出時為 Nothing。</summary>
    Public Property Result As ExportResult

    Public Sub New(project As MontageProject, exporter As CollageExporter, defaultFolder As String)
        _project = project
        _exporter = exporter

        Text = "匯出作品"
        AutoScaleMode = AutoScaleMode.Dpi
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(460, 400)
        Padding = New Padding(12)

        Dim layout As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoSize = True}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        _preset = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
        _preset.Items.AddRange(ExportPreset.All.Cast(Of Object)().ToArray())
        AddHandler _preset.SelectedIndexChanged, Sub(s, e) UpdateSize()
        AddRow(layout, "解析度", _preset)

        _customEdge = New NumericUpDown() With {.Minimum = ExportPlanner.MinLongEdge, .Maximum = ExportPlanner.MaxLongEdge, .Increment = 100, .Width = 100}
        AddHandler _customEdge.ValueChanged, Sub(s, e) UpdateSize()
        Dim customRow As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = False}
        customRow.Controls.Add(_customEdge)
        customRow.Controls.Add(New Label() With {.Text = "像素（長邊）", .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)})
        AddRow(layout, "", customRow)

        _sizeLabel = New Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(330, 0)}
        AddRow(layout, "輸出尺寸", _sizeLabel)

        _jpeg = New RadioButton() With {.Text = "JPEG（檔案小）", .AutoSize = True}
        _png = New RadioButton() With {.Text = "PNG（無損）", .AutoSize = True}
        AddHandler _jpeg.CheckedChanged, Sub(s, e) OnFormatChanged()
        Dim formatRow As New FlowLayoutPanel() With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = False}
        formatRow.Controls.Add(_jpeg)
        formatRow.Controls.Add(_png)
        AddRow(layout, "格式", formatRow)

        _quality = New LabeledSlider(50, 100, Function(v) v.ToString()) With {.Dock = DockStyle.Fill}
        AddRow(layout, "JPEG 品質", _quality)

        _path = New TextBox() With {.Dock = DockStyle.Fill}
        _browse = New Button() With {.Text = "瀏覽…", .AutoSize = True, .Dock = DockStyle.Right}
        AddHandler _browse.Click, AddressOf OnBrowse
        Dim pathRow As New Panel() With {.Dock = DockStyle.Fill, .Height = 28}
        pathRow.Controls.Add(_path)
        pathRow.Controls.Add(_browse)
        AddRow(layout, "儲存位置", pathRow)

        _progress = New ProgressBar() With {.Dock = DockStyle.Fill, .Visible = False}
        _status = New Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(330, 0)}
        AddRow(layout, "", _progress)
        AddRow(layout, "", _status)

        _exportButton = New Button() With {.Text = "匯出", .AutoSize = True, .MinimumSize = New Size(90, 30)}
        AddHandler _exportButton.Click, AddressOf OnExportClick
        _cancelButton = New Button() With {.Text = "取消", .AutoSize = True, .MinimumSize = New Size(90, 30)}
        AddHandler _cancelButton.Click, AddressOf OnCancelClick
        Dim buttons As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True}
        buttons.Controls.Add(_cancelButton)
        buttons.Controls.Add(_exportButton)

        Controls.Add(layout)
        Controls.Add(buttons)
        AcceptButton = _exportButton

        ' 還原上次設定
        _preset.SelectedIndex = Math.Min(_lastPresetIndex, _preset.Items.Count - 1)
        _customEdge.Value = Math.Max(_customEdge.Minimum, Math.Min(_customEdge.Maximum, _lastCustomEdge))
        _jpeg.Checked = _lastFormat = ExportFormat.Jpeg
        _png.Checked = _lastFormat = ExportFormat.Png
        _quality.Value = _lastQuality
        Dim folder = If(_lastFolder, If(String.IsNullOrWhiteSpace(defaultFolder), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), defaultFolder))
        _path.Text = Path.Combine(folder, ExportPlanner.DefaultFileName(SelectedFormat, Date.Now))
        UpdateSize()
        OnFormatChanged()
    End Sub

    Private Shared Sub AddRow(layout As TableLayoutPanel, label As String, control As Control)
        Dim row = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label() With {.Text = label, .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)}, 0, row)
        layout.Controls.Add(control, 1, row)
    End Sub

    Private ReadOnly Property SelectedPreset As ExportPreset
        Get
            Return CType(_preset.SelectedItem, ExportPreset)
        End Get
    End Property

    Private ReadOnly Property SelectedFormat As ExportFormat
        Get
            Return If(_png.Checked, ExportFormat.Png, ExportFormat.Jpeg)
        End Get
    End Property

    Private Function BuildSettings() As ExportSettings
        Dim preset = SelectedPreset
        Return New ExportSettings With {
            .LongEdge = If(preset.IsCustom, CInt(_customEdge.Value), preset.LongEdge),
            .Dpi = preset.Dpi,
            .Format = SelectedFormat,
            .JpegQuality = _quality.Value,
            .FilePath = ExportPlanner.EnsureExtension(_path.Text.Trim(), SelectedFormat)}
    End Function

    Private Sub UpdateSize()
        If SelectedPreset Is Nothing Then Return
        _customEdge.Enabled = SelectedPreset.IsCustom
        Dim settings = BuildSettings()
        Dim size = settings.GetOutputSize(_project.CanvasAspect)
        Dim invalid = ExportPlanner.ValidateOutputSize(size)

        Dim text = $"{size.Width} × {size.Height} 像素（{size.Width * CLng(size.Height) / 1_000_000.0:0.#} 百萬像素）"
        If settings.Dpi >= 300 Then text &= $"{vbLf}列印約 {size.Width / settings.Dpi * 2.54:0.#} × {size.Height / settings.Dpi * 2.54:0.#} 公分"
        If invalid IsNot Nothing Then
            text &= vbLf & invalid
        ElseIf Not Environment.Is64BitProcess AndAlso CLng(size.Width) * size.Height > 30_000_000L Then
            text &= vbLf & "解析度很高，在 32 位元模式下可能記憶體不足。"
        End If
        _sizeLabel.Text = text
        _sizeLabel.ForeColor = If(invalid IsNot Nothing, Color.Firebrick, SystemColors.ControlText)
        _exportButton.Enabled = invalid Is Nothing AndAlso _cts Is Nothing
    End Sub

    Private Sub OnFormatChanged()
        _quality.Enabled = _jpeg.Checked
        If _path.Text.Length > 0 Then _path.Text = ExportPlanner.EnsureExtension(_path.Text, SelectedFormat)
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
            _png.Checked = dlg.FilterIndex = 2
            _jpeg.Checked = Not _png.Checked
            _path.Text = ExportPlanner.EnsureExtension(dlg.FileName, SelectedFormat)
        End Using
    End Sub

    Private Async Sub OnExportClick(sender As Object, e As EventArgs)
        Dim settings = BuildSettings()
        If String.IsNullOrWhiteSpace(settings.FilePath) OrElse Not Path.IsPathFullyQualified(settings.FilePath) Then
            MessageBox.Show(Me, "請選擇儲存位置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If File.Exists(settings.FilePath) AndAlso
           MessageBox.Show(Me, $"「{Path.GetFileName(settings.FilePath)}」已存在，要取代嗎？", Text,
                           MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        RememberSettings(settings)
        SetRunning(True)
        _cts = New CancellationTokenSource()
        Dim progress As New Progress(Of ExportProgress)(
            Sub(p)
                _progress.Maximum = Math.Max(1, p.Total)
                _progress.Value = Math.Min(p.Completed, _progress.Maximum)
                _status.Text = p.Message
            End Sub)

        Try
            Dim token = _cts.Token
            Result = Await Task.Run(Function() _exporter.Export(_project, settings, progress, token))
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
        ' 匯出中按右上角關閉 → 先取消，等背景工作結束
        If _cts IsNot Nothing Then
            e.Cancel = True
            _cts.Cancel()
            _status.Text = "取消中…"
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Private Sub SetRunning(running As Boolean)
        For Each c In {_preset, CType(_customEdge, Control), _jpeg, _png, _quality, _path, _browse}
            c.Enabled = Not running
        Next
        If Not running Then
            _customEdge.Enabled = SelectedPreset.IsCustom
            _quality.Enabled = _jpeg.Checked
        End If
        _progress.Visible = running
        _exportButton.Enabled = Not running
        If running Then _status.Text = "準備中…"
    End Sub

    Private Sub RememberSettings(settings As ExportSettings)
        _lastPresetIndex = _preset.SelectedIndex
        _lastCustomEdge = CInt(_customEdge.Value)
        _lastFormat = settings.Format
        _lastQuality = settings.JpegQuality
        _lastFolder = Path.GetDirectoryName(settings.FilePath)
    End Sub
End Class

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports System.Threading
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 列印對話框（Aqua 視窗）：選印表機、紙張、方向、邊界與完整顯示／填滿，左側即時顯示紙上的樣子。
''' 列印時先在背景以印表機解析度（上限 300 dpi）繪製作品，再送到印表機。
''' </summary>
Friend Class PrintSetupDialog
    Inherits Aqua.AquaForm

    ''' <summary>同一個行程內記住上次的設定。</summary>
    Private Shared _lastPrinter As String
    Private Shared _lastPaper As String
    Private Shared _lastOrientation As PrintOrientation = PrintOrientation.Auto
    Private Shared _lastFit As PrintFit = PrintFit.Fit
    Private Shared _lastMargin As Integer = 1

    Private Const PreviewLongEdge As Integer = 900

    Private ReadOnly _project As MontageProject
    Private ReadOnly _exporter As MontageExporter
    Private ReadOnly _settings As New PrinterSettings()
    Private _cts As CancellationTokenSource
    Private _previewCts As CancellationTokenSource
    Private _preview As Bitmap
    Private _initializing As Boolean = True

    Private ReadOnly _previewBox As PagePreview
    Private ReadOnly _printer As ComboBox
    Private ReadOnly _paper As ComboBox
    Private ReadOnly _orientation As SegmentedChoice
    Private ReadOnly _fit As SegmentedChoice
    Private ReadOnly _margin As SegmentedChoice
    Private ReadOnly _copies As LabeledSlider
    Private ReadOnly _info As System.Windows.Forms.Label
    Private ReadOnly _progress As Aqua.ProgressBar
    Private ReadOnly _status As System.Windows.Forms.Label
    Private ReadOnly _moreButton As PillButton
    Private ReadOnly _printButton As PillButton
    Private ReadOnly _cancelButton As PillButton

    ''' <summary>列印時無法讀取的照片等問題；列印成功後由呼叫端顯示。</summary>
    Public ReadOnly Property Warnings As New List(Of String)

    Public Sub New(project As MontageProject, exporter As MontageExporter,
                   Optional accent As Aqua.ColorConstants = Aqua.ColorConstants.Blue)
        _project = project
        _exporter = exporter

        Text = "列印作品"
        TitleFont = New Font("Microsoft JhengHei UI", 10.0F, FontStyle.Bold)
        MinButton = False
        MaxButton = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(780, 470)

        _previewBox = New PagePreview() With {.Dock = DockStyle.Fill}

        Dim layout As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .BackColor = Color.Transparent}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        ' 印表機與紙張可能很多，用可捲動的標準下拉選單（Aqua 下拉選單無法捲動）
        _printer = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .MaxDropDownItems = 16}
        AddHandler _printer.SelectedIndexChanged, Sub(s, e) OnPrinterChanged()
        AddRow(layout, "印表機", _printer)

        _paper = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .MaxDropDownItems = 20}
        AddHandler _paper.SelectedIndexChanged, Sub(s, e) UpdateLayout()
        AddRow(layout, "紙張", _paper)

        _orientation = New SegmentedChoice("自動", "直向", "橫向") With {.Width = 210}
        AddHandler _orientation.SelectedChanged, Sub(s, e) UpdateLayout()
        AddRow(layout, "方向", _orientation)

        _fit = New SegmentedChoice("完整顯示", "填滿紙張") With {.Width = 210}
        AddHandler _fit.SelectedChanged, Sub(s, e) UpdateLayout()
        AddRow(layout, "版面", _fit)

        _margin = New SegmentedChoice("無", "窄", "一般") With {.Width = 210}
        AddHandler _margin.SelectedChanged, Sub(s, e) UpdateLayout()
        AddRow(layout, "邊界", _margin)

        _copies = New LabeledSlider(1, 20, Function(v) $"{v} 份") With {.Dock = DockStyle.Fill}
        AddRow(layout, "份數", _copies)

        _moreButton = New PillButton() With {.Text = "印表機設定…", .Width = 130}
        AddHandler _moreButton.Click, AddressOf OnMoreSettings
        AddRow(layout, "", _moreButton)

        _info = New System.Windows.Forms.Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(260, 0), .BackColor = Color.Transparent, .ForeColor = Color.DimGray}
        AddRow(layout, "", _info)

        _progress = New Aqua.ProgressBar() With {.Dock = DockStyle.Fill, .Visible = False}
        _status = New System.Windows.Forms.Label() With {.AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(260, 0), .BackColor = Color.Transparent}
        AddRow(layout, "", _progress)
        AddRow(layout, "", _status)

        Dim right As New System.Windows.Forms.Panel() With {.Dock = DockStyle.Right, .Width = 350, .Padding = New Padding(14, 0, 0, 0), .BackColor = Color.Transparent}
        right.Controls.Add(layout)

        _printButton = New PillButton() With {.Text = "列印", .Width = 90}
        AddHandler _printButton.Click, AddressOf OnPrintClick
        _cancelButton = New PillButton() With {.Text = "取消", .Width = 90}
        AddHandler _cancelButton.Click, AddressOf OnCancelClick
        Dim buttons As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .BackColor = Color.Transparent}
        buttons.Controls.Add(_cancelButton)
        buttons.Controls.Add(_printButton)

        Controls.Add(_previewBox)
        Controls.Add(right)
        Controls.Add(buttons)

        _orientation.SelectedIndex = CInt(_lastOrientation)
        _fit.SelectedIndex = CInt(_lastFit)
        _margin.SelectedIndex = _lastMargin
        _copies.Value = 1
        LoadPrinters()
        _initializing = False
        OnPrinterChanged()

        Dim help As New HelpToolTip(Me)
        help.SetHelp(HelpTexts.PrintPrinter, _printer)
        help.SetHelp(HelpTexts.PrintPaper, _paper)
        help.SetHelp(HelpTexts.PrintOrientation, _orientation)
        help.SetHelp(HelpTexts.PrintFit, _fit)
        help.SetHelp(HelpTexts.PrintMargin, _margin)
        help.SetHelp(HelpTexts.PrintCopies, _copies)
        help.SetHelp(HelpTexts.PrintMore, _moreButton)
        help.SetHelp(HelpTexts.PrintStart, _printButton)

        AquaTheme.Apply(Me, accent)
    End Sub

    ''' <summary>內容放在 Aqua 標題列下方。在自動縮放之後設定，確保是實際像素。</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Padding = New Padding(12, MontageEditorForm.AquaTitleBarHeight + 10, 12, 12)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If _printer.Items.Count = 0 Then
            MessageBox.Show(Me, "找不到印表機。請先在 Windows 設定中新增印表機。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        RenderPreviewAsync()
    End Sub

    Private Shared Sub AddRow(layout As TableLayoutPanel, label As String, control As Control)
        Dim row = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        control.Margin = New Padding(3, 3, 3, 5)
        layout.Controls.Add(New System.Windows.Forms.Label() With {.Text = label, .AutoSize = True, .Padding = New Padding(0, 6, 0, 0), .BackColor = Color.Transparent}, 0, row)
        layout.Controls.Add(control, 1, row)
    End Sub

#Region "印表機與紙張"

    Private Sub LoadPrinters()
        Dim names As New List(Of String)
        Try
            For Each name As String In PrinterSettings.InstalledPrinters
                names.Add(name)
            Next
        Catch ex As System.ComponentModel.Win32Exception
            ' 列印多工緩衝處理器（Print Spooler）沒有執行
        End Try
        _printer.Items.AddRange(names.ToArray())
        If names.Count = 0 Then Return

        Dim preferred = If(_lastPrinter, _settings.PrinterName)
        Dim index = names.IndexOf(preferred)
        _printer.SelectedIndex = If(index >= 0, index, 0)
    End Sub

    Private Sub OnPrinterChanged(Optional preferredPaper As String = Nothing)
        If _initializing Then Return
        Dim name = TryCast(_printer.SelectedItem, String)
        _paper.Items.Clear()
        If name Is Nothing Then
            UpdateLayout()
            Return
        End If

        _settings.PrinterName = name
        Dim papers As New List(Of PaperSize)
        Try
            If _settings.IsValid Then
                For Each paper As PaperSize In _settings.PaperSizes
                    If paper.Width > 0 AndAlso paper.Height > 0 Then papers.Add(paper)
                Next
            End If
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidPrinterException
        End Try
        For Each paper In papers
            _paper.Items.Add(New PaperItem(paper))
        Next

        ' 上次用的紙 → 印表機預設的紙 → 第一種
        Dim defaultName As String = Nothing
        Try
            defaultName = _settings.DefaultPageSettings.PaperSize.PaperName
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidPrinterException
        End Try
        Dim pick = FindPaper(If(preferredPaper, _lastPaper))
        If pick < 0 Then pick = FindPaper(defaultName)
        If pick < 0 AndAlso _paper.Items.Count > 0 Then pick = 0
        _paper.SelectedIndex = pick
        UpdateLayout()
    End Sub

    Private Function FindPaper(name As String) As Integer
        If String.IsNullOrEmpty(name) Then Return -1
        For i = 0 To _paper.Items.Count - 1
            If DirectCast(_paper.Items(i), PaperItem).Paper.PaperName = name Then Return i
        Next
        Return -1
    End Function

    Private ReadOnly Property SelectedPaper As PaperSize
        Get
            Return TryCast(_paper.SelectedItem, PaperItem)?.Paper
        End Get
    End Property

    Private ReadOnly Property Landscape As Boolean
        Get
            Return PrintPlanner.UseLandscape(CType(Math.Max(0, _orientation.SelectedIndex), PrintOrientation), _project.CanvasAspect)
        End Get
    End Property

    Private ReadOnly Property SelectedFit As PrintFit
        Get
            Return If(_fit.SelectedIndex = 1, PrintFit.Fill, PrintFit.Fit)
        End Get
    End Property

    Private ReadOnly Property SelectedMargin As Single
        Get
            Return PrintPlanner.MarginChoices(Math.Max(0, Math.Min(PrintPlanner.MarginChoices.Count - 1, _margin.SelectedIndex)))
        End Get
    End Property

    ''' <summary>依目前選擇建立頁面設定。</summary>
    Private Function BuildPageSettings() As PageSettings
        Dim page = CType(_settings.DefaultPageSettings.Clone(), PageSettings)
        If SelectedPaper IsNot Nothing Then page.PaperSize = SelectedPaper
        page.Landscape = Landscape
        Return page
    End Function

    ''' <summary>紙張大小（已依方向轉好）與印表機可列印範圍，單位 1/100 吋。</summary>
    Private Function GetPageGeometry(page As PageSettings, ByRef printable As RectangleF) As SizeF
        Dim paper = page.PaperSize
        Dim size As New SizeF(paper.Width, paper.Height)
        If page.Landscape Then size = New SizeF(paper.Height, paper.Width)
        printable = New RectangleF(0, 0, size.Width, size.Height)
        Try
            Dim area = page.PrintableArea
            ' 依驅動程式不同，可列印範圍可能還是直向的數字
            If (area.Width > area.Height) <> (size.Width > size.Height) AndAlso size.Width <> size.Height Then
                area = New RectangleF(area.Y, area.X, area.Height, area.Width)
            End If
            If area.Width > 0 AndAlso area.Height > 0 Then printable = area
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidPrinterException
        End Try
        Return size
    End Function

    Private Function ComputePlacement(page As PageSettings, ByRef pageSize As SizeF, ByRef printable As RectangleF) As PrintPlacement
        pageSize = GetPageGeometry(page, printable)
        Dim area = PrintPlanner.GetContentArea(pageSize, printable, SelectedMargin)
        Return PrintPlanner.Place(area, _project.CanvasAspect, SelectedFit)
    End Function

    Private Function PrinterDpi() As Integer
        Try
            Dim x = _settings.DefaultPageSettings.PrinterResolution.X
            ' 負值代表「草稿／高品質」等模式，不是實際數字
            Return If(x > 0, x, PrintPlanner.MaxPrintDpi)
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidPrinterException
            Return PrintPlanner.MaxPrintDpi
        End Try
    End Function

    Private Sub UpdateLayout()
        If _initializing Then Return
        Dim paper = SelectedPaper
        If paper Is Nothing Then
            _previewBox.SetPage(SizeF.Empty, RectangleF.Empty, Nothing, Nothing)
            _info.Text = ""
            _printButton.Enabled = False
            Return
        End If

        Dim page = BuildPageSettings()
        Dim pageSize As SizeF
        Dim printable As RectangleF
        Dim placement = ComputePlacement(page, pageSize, printable)
        Dim valid = placement.Destination.Width > 0 AndAlso placement.Destination.Height > 0
        _previewBox.SetPage(pageSize, printable, If(valid, CType(placement, PrintPlacement?), Nothing), _preview)

        If valid Then
            Dim d = placement.Destination
            Dim text = $"紙上大小 {d.Width * 0.0254:0.#} × {d.Height * 0.0254:0.#} 公分"
            If SelectedFit = PrintFit.Fill AndAlso (placement.Source.Width < 0.995 OrElse placement.Source.Height < 0.995) Then
                text &= vbLf & "作品比例和紙張不同，邊緣會被裁掉。"
            End If
            If SelectedMargin = 0 Then text &= vbLf & "多數印表機無法印到紙張邊緣，最外圈會留一點白邊。"
            _info.Text = text
        Else
            _info.Text = "紙張太小，放不下作品。"
        End If
        _printButton.Enabled = valid AndAlso _cts Is Nothing
    End Sub

    Private Sub OnMoreSettings(sender As Object, e As EventArgs)
        Using dlg As New System.Windows.Forms.PrintDialog()
            _settings.DefaultPageSettings.Landscape = Landscape
            If SelectedPaper IsNot Nothing Then _settings.DefaultPageSettings.PaperSize = SelectedPaper
            _settings.Copies = CShort(_copies.Value)
            dlg.PrinterSettings = _settings
            dlg.UseEXDialog = True
            dlg.AllowSomePages = False
            dlg.AllowSelection = False
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using

        ' 帶回在 Windows 對話框中改的印表機、紙張、份數與方向
        Dim paperName = _settings.DefaultPageSettings.PaperSize.PaperName
        Dim copies = _settings.Copies
        Dim landscapeChosen = _settings.DefaultPageSettings.Landscape
        _initializing = True
        Dim index = _printer.Items.IndexOf(_settings.PrinterName)
        If index >= 0 Then _printer.SelectedIndex = index
        _initializing = False
        OnPrinterChanged(paperName)
        _copies.Value = copies
        If landscapeChosen <> Landscape Then _orientation.SelectedIndex = If(landscapeChosen, 2, 1)
    End Sub

#End Region

#Region "預覽"

    ''' <summary>在背景繪製小張的作品供左側預覽。</summary>
    Private Async Sub RenderPreviewAsync()
        _previewCts = New CancellationTokenSource()
        Dim token = _previewCts.Token
        Dim size = ExportPlanner.ComputeOutputSize(_project.CanvasAspect, PreviewLongEdge)
        _previewBox.Message = "產生預覽中…"
        Try
            Dim bitmap = Await Task.Run(Function() _exporter.RenderBitmap(_project, size, Nothing, token, New List(Of String)))
            If IsDisposed OrElse token.IsCancellationRequested Then
                bitmap.Dispose()
                Return
            End If
            _preview = bitmap
            _previewBox.Message = Nothing
            UpdateLayout()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            ' Async Sub 中未處理的例外會讓整個程式（含宿主）結束；預覽失敗不影響列印
            If Not IsDisposed Then _previewBox.Message = "無法產生預覽"
        End Try
    End Sub

#End Region

#Region "列印"

    Private Async Sub OnPrintClick(sender As Object, e As EventArgs)
        If _cts IsNot Nothing OrElse SelectedPaper Is Nothing Then Return
        Dim page = BuildPageSettings()
        Dim pageSize As SizeF
        Dim printable As RectangleF
        Dim placement = ComputePlacement(page, pageSize, printable)
        If placement.Destination.Width <= 0 Then Return
        Dim renderSize = PrintPlanner.GetRenderSize(placement, _project.CanvasAspect, PrinterDpi())

        RememberSettings()
        _cts = New CancellationTokenSource()
        SetRunning(True)
        Dim progress As New Progress(Of ExportProgress)(
            Sub(p)
                _progress.Maximum = Math.Max(1, p.Total)
                _progress.Value = Math.Min(p.Completed, _progress.Maximum)
                _status.Text = p.Message
            End Sub)

        Dim image As Bitmap = Nothing
        Try
            Dim token = _cts.Token
            Dim warnings As New List(Of String)
            image = Await Task.Run(Function() _exporter.RenderBitmap(_project, renderSize, progress, token, warnings))
            Me.Warnings.AddRange(warnings)
            _status.Text = "傳送到印表機…"
            _cancelButton.Enabled = False
            Update()
            SendToPrinter(image, page, placement)
            _cts.Dispose()
            _cts = Nothing
            DialogResult = DialogResult.OK
            Close()
        Catch ex As OperationCanceledException
            _status.Text = "已取消。"
        Catch ex As Exception When TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ArgumentException
            ShowError("記憶體不足，無法產生列印用的影像。請改用較小的紙張後再試一次。")
        Catch ex As Exception
            ' Async Sub 中未處理的例外會讓整個程式（含宿主）結束，所以一律攔下
            ShowError("列印失敗：" & ex.Message)
        Finally
            image?.Dispose()
            _cts?.Dispose()
            _cts = Nothing
            If Not IsDisposed Then SetRunning(False)
        End Try
    End Sub

    ''' <summary>送出一頁。座標轉成整張紙（左上角為紙張邊緣）以符合預覽的計算方式。</summary>
    Private Sub SendToPrinter(image As Bitmap, page As PageSettings, placement As PrintPlacement)
        _settings.Copies = CShort(_copies.Value)
        Using doc As New PrintDocument()
            doc.DocumentName = "蒙太奇相片"
            doc.PrinterSettings = _settings
            doc.DefaultPageSettings = page
            doc.PrintController = New StandardPrintController()
            AddHandler doc.PrintPage,
                Sub(s, e)
                    Dim g = e.Graphics
                    g.TranslateTransform(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                    Dim src = placement.Source
                    Dim srcRect As New RectangleF(src.X * image.Width, src.Y * image.Height, src.Width * image.Width, src.Height * image.Height)
                    g.DrawImage(image, placement.Destination, srcRect, GraphicsUnit.Pixel)
                    e.HasMorePages = False
                End Sub
            doc.Print()
        End Using
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
        If _cts IsNot Nothing Then
            e.Cancel = True
            _cts.Cancel()
            _status.Text = "取消中…"
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _previewCts?.Cancel()
            _previewCts?.Dispose()
            _previewBox.SetPage(SizeF.Empty, RectangleF.Empty, Nothing, Nothing)
            _preview?.Dispose()
            _preview = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub

    Private Sub SetRunning(running As Boolean)
        For Each c In New Control() {_printer, _paper, _orientation, _fit, _margin, _copies, _moreButton}
            c.Enabled = Not running
        Next
        _cancelButton.Enabled = True
        _progress.Visible = running
        If running Then
            _printButton.Enabled = False
            _status.Text = "準備中…"
        Else
            UpdateLayout()
        End If
    End Sub

    Private Sub RememberSettings()
        _lastPrinter = _settings.PrinterName
        _lastPaper = SelectedPaper?.PaperName
        _lastOrientation = CType(Math.Max(0, _orientation.SelectedIndex), PrintOrientation)
        _lastFit = SelectedFit
        _lastMargin = _margin.SelectedIndex
    End Sub

#End Region

    ''' <summary>下拉選單中的紙張，顯示名稱與公分大小。</summary>
    Private NotInheritable Class PaperItem
        Public ReadOnly Paper As PaperSize

        Public Sub New(paper As PaperSize)
            Me.Paper = paper
        End Sub

        Public Overrides Function ToString() As String
            Return $"{Paper.PaperName}（{Paper.Width * 0.0254:0.#} × {Paper.Height * 0.0254:0.#} 公分）"
        End Function
    End Class

    ''' <summary>畫出紙張、可列印範圍與作品位置。</summary>
    Private NotInheritable Class PagePreview
        Inherits Control

        Private _pageSize As SizeF
        Private _printable As RectangleF
        Private _placement As PrintPlacement?
        Private _image As Image
        Private _message As String

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Color.FromArgb(214, 218, 224)
        End Sub

        Public Property Message As String
            Get
                Return _message
            End Get
            Set(value As String)
                _message = value
                Invalidate()
            End Set
        End Property

        Public Sub SetPage(pageSize As SizeF, printable As RectangleF, placement As PrintPlacement?, image As Image)
            _pageSize = pageSize
            _printable = printable
            _placement = placement
            _image = image
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            If _pageSize.Width <= 0 OrElse _pageSize.Height <= 0 Then
                DrawMessage(g, ClientRectangle, If(_message, "請選擇印表機與紙張"))
                Return
            End If

            Const pad = 16
            Dim scale = Math.Min((Width - pad * 2) / _pageSize.Width, (Height - pad * 2) / _pageSize.Height)
            If scale <= 0 Then Return
            Dim paper As New RectangleF((Width - _pageSize.Width * scale) / 2, (Height - _pageSize.Height * scale) / 2,
                                        _pageSize.Width * scale, _pageSize.Height * scale)
            Dim toScreen = Function(r As RectangleF) New RectangleF(paper.X + r.X * scale, paper.Y + r.Y * scale, r.Width * scale, r.Height * scale)

            Using shadow As New SolidBrush(Color.FromArgb(60, 0, 0, 0))
                g.FillRectangle(shadow, paper.X + 3, paper.Y + 4, paper.Width, paper.Height)
            End Using
            g.FillRectangle(Brushes.White, paper)

            If _placement.HasValue Then
                Dim dest = toScreen(_placement.Value.Destination)
                If _image IsNot Nothing Then
                    Dim src = _placement.Value.Source
                    Dim srcRect As New RectangleF(src.X * _image.Width, src.Y * _image.Height, src.Width * _image.Width, src.Height * _image.Height)
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                    g.DrawImage(_image, dest, srcRect, GraphicsUnit.Pixel)
                Else
                    Using fill As New SolidBrush(Color.FromArgb(200, 208, 220))
                        g.FillRectangle(fill, dest)
                    End Using
                    DrawMessage(g, Rectangle.Round(dest), _message)
                End If
            End If

            ' 印表機印不到的範圍用虛線標出
            If _printable.Width > 0 AndAlso (_printable.Width < _pageSize.Width - 1 OrElse _printable.Height < _pageSize.Height - 1) Then
                Using pen As New Pen(Color.FromArgb(150, 120, 130, 145)) With {.DashStyle = DashStyle.Dash}
                    Dim r = toScreen(_printable)
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height)
                End Using
            End If
            g.DrawRectangle(Pens.Gray, paper.X, paper.Y, paper.Width, paper.Height)
        End Sub

        Private Sub DrawMessage(g As Graphics, bounds As Rectangle, text As String)
            If String.IsNullOrEmpty(text) Then Return
            TextRenderer.DrawText(g, text, Font, bounds, Color.DimGray,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End Sub
    End Class

End Class

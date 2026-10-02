Imports System.Drawing
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>「馬賽克」頁：主圖、格數、比例、使用次數、疊色、產生。</summary>
Friend Class MosaicPanel
    Inherits StackPanel

    Private _project As MontageProject
    Private _updating As Boolean
    Private _tileCount As Integer

    Private ReadOnly _targetLabel As Label
    Private ReadOnly _useSelected As Button
    Private ReadOnly _ratio As ComboBox
    Private ReadOnly _columns As LabeledSlider
    Private ReadOnly _gridLabel As Label
    Private ReadOnly _maxRepeat As NumericUpDown
    Private ReadOnly _avoidAdjacent As CheckBox
    Private ReadOnly _tint As LabeledSlider
    Private ReadOnly _generate As Button
    Private ReadOnly _progress As ProgressBar
    Private ReadOnly _cancel As LinkLabel
    Private ReadOnly _status As Label

    ''' <summary>即將變更（供復原記錄）。</summary>
    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)

    ''' <summary>設定已變更；<see cref="MosaicSettingsChangedEventArgs.TilesInvalidated"/> 表示需要重新產生。</summary>
    Public Event SettingsChanged As EventHandler(Of MosaicSettingsChangedEventArgs)

    Public Event GenerateRequested As EventHandler
    Public Event CancelRequested As EventHandler
    Public Event UseSelectedAsTargetRequested As EventHandler
    Public Event TargetFileRequested As EventHandler(Of FilesDroppedEventArgs)

    ''' <summary>使用者選了「依主圖比例」以外的比例，或重新選了「依主圖比例」。</summary>
    Public Event RatioRequested As EventHandler

    Public Sub New()
        AddLabel("主圖")
        _targetLabel = Add(New Label() With {.AutoEllipsis = True, .Height = 20})
        _useSelected = New Button() With {.Text = "用左側選取的照片", .AutoSize = True}
        AddHandler _useSelected.Click, Sub(s, e) RaiseEvent UseSelectedAsTargetRequested(Me, EventArgs.Empty)
        Dim pick As New Button() With {.Text = "選擇檔案…", .AutoSize = True}
        AddHandler pick.Click, AddressOf OnPickTarget
        AddRow(_useSelected, pick)

        AddLabel("畫布比例")
        _ratio = Add(New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList})
        _ratio.Items.Add("依主圖比例")
        For Each p In CanvasPresets.All
            _ratio.Items.Add(p)
        Next
        _ratio.SelectedIndex = 0
        AddHandler _ratio.SelectedIndexChanged, Sub(s, e) If Not _updating Then RaiseEvent RatioRequested(Me, EventArgs.Empty)

        AddLabel("每列格數")
        _columns = Add(New LabeledSlider(MosaicSettings.MinColumns, MosaicSettings.MaxColumns, Function(v) v.ToString()))
        AddHandler _columns.ValueChanged, Sub(s, e) Apply("mosaic:columns", True,
            Sub(m)
                m.Columns = _columns.Value
                m.Rows = MosaicSettings.RowsFor(m.Columns, _project.CanvasAspect)
            End Sub)
        _gridLabel = Add(New Label() With {.Height = 20, .ForeColor = SystemColors.GrayText})

        AddLabel("每張素材最多使用（0 = 不限）")
        _maxRepeat = Add(New NumericUpDown() With {.Minimum = 0, .Maximum = 1000})
        AddHandler _maxRepeat.ValueChanged, Sub(s, e) Apply("mosaic:repeat", True, Sub(m) m.MaxRepeat = CInt(_maxRepeat.Value))

        _avoidAdjacent = Add(New CheckBox() With {.Text = "避免相鄰格子重複", .Margin = New Padding(0, 6, 0, 2)})
        AddHandler _avoidAdjacent.CheckedChanged, Sub(s, e) Apply(Nothing, True, Sub(m) m.AvoidAdjacentDuplicates = _avoidAdjacent.Checked)

        AddLabel("疊上主圖")
        _tint = Add(New LabeledSlider(0, 30, Function(v) $"{v}%"))
        AddHandler _tint.ValueChanged, Sub(s, e) Apply("mosaic:tint", False, Sub(m) m.Tint = _tint.Value / 100.0F)
        Dim tintHint = AddLabel("適度疊色（約 10～20%）能讓遠看時更容易認出主圖。")
        tintHint.ForeColor = SystemColors.GrayText
        tintHint.MaximumSize = New Size(210, 0)

        _generate = Add(New Button() With {.Text = "產生馬賽克", .Height = 36, .Margin = New Padding(0, 14, 0, 4)})
        AddHandler _generate.Click, Sub(s, e) RaiseEvent GenerateRequested(Me, EventArgs.Empty)
        _progress = Add(New ProgressBar() With {.Height = 14, .Visible = False})
        _cancel = New LinkLabel() With {.Text = "取消", .AutoSize = True, .Visible = False}
        AddHandler _cancel.LinkClicked, Sub(s, e) RaiseEvent CancelRequested(Me, EventArgs.Empty)
        AddRow(_cancel)
        _status = AddLabel("")
        _status.MaximumSize = New Size(210, 0)
    End Sub

    Public Sub Bind(project As MontageProject)
        _project = project
        RefreshFromProject()
    End Sub

    ''' <summary>是否選了「依主圖比例」。</summary>
    Public ReadOnly Property MatchTargetAspect As Boolean
        Get
            Return _ratio.SelectedIndex <= 0
        End Get
    End Property

    Public ReadOnly Property SelectedPreset As CanvasPreset
        Get
            Return TryCast(_ratio.SelectedItem, CanvasPreset)
        End Get
    End Property

    ''' <summary>目前可用的素材張數（顯示建議用）。</summary>
    Public Property TileCount As Integer
        Get
            Return _tileCount
        End Get
        Set(value As Integer)
            _tileCount = value
            RefreshStatus()
        End Set
    End Property

    Public Sub RefreshFromProject()
        If _project Is Nothing Then Return
        _updating = True
        Dim m = _project.Mosaic
        Dim path = m.TargetPath
        _targetLabel.Text = If(path Is Nothing, "（尚未選擇）", IO.Path.GetFileName(path))
        _targetLabel.ForeColor = If(path Is Nothing, SystemColors.GrayText, SystemColors.ControlText)
        If Not MatchTargetAspect Then
            Dim preset = CanvasPresets.Find(_project.CanvasSize)
            If preset IsNot Nothing Then _ratio.SelectedItem = preset
        End If
        _columns.Value = m.Columns
        _maxRepeat.Value = Math.Min(_maxRepeat.Maximum, m.MaxRepeat)
        _avoidAdjacent.Checked = m.AvoidAdjacentDuplicates
        _tint.Value = CInt(Math.Round(m.Tint * 100))
        _gridLabel.Text = $"{m.Columns} × {m.Rows} = {m.CellCount:N0} 格"
        _updating = False
        RefreshStatus()
    End Sub

    Public Sub SetBusy(busy As Boolean)
        _generate.Enabled = Not busy
        _progress.Visible = busy
        _cancel.Visible = busy
        If Not busy Then RefreshStatus()
    End Sub

    Public Sub ReportProgress(p As ExportProgress)
        _progress.Maximum = Math.Max(1, p.Total)
        _progress.Value = Math.Min(p.Completed, _progress.Maximum)
        _status.Text = p.Message
    End Sub

    Public Sub ShowStatus(text As String)
        _status.Text = text
    End Sub

    Private Sub RefreshStatus()
        If _project Is Nothing OrElse _progress.Visible Then Return
        Dim lines As New List(Of String)
        lines.Add($"素材：{_tileCount:N0} 張")
        If _tileCount < MosaicGenerator.RecommendedTiles Then lines.Add($"建議至少 {MosaicGenerator.RecommendedTiles} 張，效果較好。")
        If _project.Mosaic.TargetPath Is Nothing Then
            lines.Add("請先選擇主圖。")
        ElseIf Not _project.Mosaic.IsGenerated Then
            lines.Add("按「產生馬賽克」開始。")
        Else
            Dim used = _project.Mosaic.Tiles.Where(Function(t) t IsNot Nothing).Distinct().Count()
            lines.Add($"已使用 {used:N0} 張素材。")
            lines.Add("在畫布上按右鍵可更換單格素材。")
        End If
        _status.Text = String.Join(vbLf, lines)
        _generate.Enabled = _project.Mosaic.TargetPath IsNot Nothing AndAlso _tileCount >= MosaicGenerator.MinTiles
    End Sub

    Private Sub Apply(key As String, invalidatesTiles As Boolean, change As Action(Of MosaicSettings))
        If _updating OrElse _project Is Nothing Then Return
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(key))
        change(_project.Mosaic)
        If invalidatesTiles Then _project.Mosaic.Tiles.Clear()
        RefreshFromProject()
        RaiseEvent SettingsChanged(Me, New MosaicSettingsChangedEventArgs(invalidatesTiles))
    End Sub

    Private Sub OnPickTarget(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog() With {.Title = "選擇主圖", .Filter = ImageFormatSniffer.DialogFilter}
            If dlg.ShowDialog(FindForm()) = DialogResult.OK Then
                RaiseEvent TargetFileRequested(Me, New FilesDroppedEventArgs({dlg.FileName}))
            End If
        End Using
    End Sub
End Class

Friend Class MosaicSettingsChangedEventArgs
    Inherits EventArgs

    Public ReadOnly Property TilesInvalidated As Boolean

    Public Sub New(tilesInvalidated As Boolean)
        Me.TilesInvalidated = tilesInvalidated
    End Sub
End Class

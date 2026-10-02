Imports System.Drawing
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>「馬賽克」頁：主圖、格數、比例、使用次數、疊色、產生。</summary>
Friend Class MosaicPanel
    Inherits StackPanel

    Private _project As MontageProject
    Private _updating As Boolean
    Private _tileCount As Integer

    Private ReadOnly _targetLabel As System.Windows.Forms.Label
    Private ReadOnly _useSelected As PillButton
    ''' <summary>第 0 項為「依主圖比例」，其後依序為 <see cref="CanvasPresets.All"/>。</summary>
    Private ReadOnly _ratio As Aqua.DropDownList
    Private ReadOnly _columns As LabeledSlider
    Private ReadOnly _gridLabel As System.Windows.Forms.Label
    Private ReadOnly _maxRepeat As LabeledSlider
    Private ReadOnly _avoidAdjacent As Aqua.CheckBox
    Private ReadOnly _tint As LabeledSlider
    Private ReadOnly _generate As PillButton
    Private ReadOnly _progress As Aqua.ProgressBar
    Private ReadOnly _cancel As PillButton
    Private ReadOnly _status As System.Windows.Forms.Label

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
        _targetLabel = Add(New System.Windows.Forms.Label() With {.AutoEllipsis = True, .Height = 20, .BackColor = Color.Transparent})
        _useSelected = New PillButton() With {.Text = "用左側選取的照片"}
        AddHandler _useSelected.Click, Sub(s, e) RaiseEvent UseSelectedAsTargetRequested(Me, EventArgs.Empty)
        Dim pick As New PillButton() With {.Text = "選擇檔案…"}
        AddHandler pick.Click, AddressOf OnPickTarget
        Add(_useSelected)
        Add(pick)

        AddLabel("畫布比例")
        _ratio = Add(New Aqua.DropDownList())
        _ratio.AddItem("target", "依主圖比例")
        For Each p In CanvasPresets.All
            _ratio.AddItem(p.Name, p.Name)
        Next
        _updating = True
        _ratio.SelectedIndex = 0
        _updating = False
        AddHandler _ratio.SelectedChanged, Sub(s, e) If Not _updating Then RaiseEvent RatioRequested(Me, EventArgs.Empty)

        AddLabel("每列格數")
        _columns = Add(New LabeledSlider(MosaicSettings.MinColumns, MosaicSettings.MaxColumns, Function(v) v.ToString()))
        AddHandler _columns.ValueChanged, Sub(s, e) Apply("mosaic:columns", True,
            Sub(m)
                m.Columns = _columns.Value
                m.Rows = MosaicSettings.RowsFor(m.Columns, _project.CanvasAspect)
            End Sub)
        _gridLabel = Add(New System.Windows.Forms.Label() With {.Height = 20, .ForeColor = SystemColors.GrayText, .BackColor = Color.Transparent})

        AddLabel("每張素材最多使用（0 = 不限）")
        _maxRepeat = Add(New LabeledSlider(0, 50, Function(v) If(v = 0, "不限", $"{v} 次")))
        AddHandler _maxRepeat.ValueChanged, Sub(s, e) Apply("mosaic:repeat", True, Sub(m) m.MaxRepeat = _maxRepeat.Value)

        _avoidAdjacent = Add(New Aqua.CheckBox() With {.Text = "避免相鄰格子重複"})
        _avoidAdjacent.Margin = New Padding(0, 6, 0, 2)
        AddHandler _avoidAdjacent.CheckedChanged, Sub(s, e) Apply(Nothing, True, Sub(m) m.AvoidAdjacentDuplicates = _avoidAdjacent.Checked)

        AddLabel("疊上主圖")
        _tint = Add(New LabeledSlider(0, 30, Function(v) $"{v}%"))
        AddHandler _tint.ValueChanged, Sub(s, e) Apply("mosaic:tint", False, Sub(m) m.Tint = _tint.Value / 100.0F)
        Dim tintHint = AddLabel("適度疊色（約 10～20%）能讓遠看時更容易認出主圖。")
        tintHint.ForeColor = SystemColors.GrayText
        tintHint.MaximumSize = New Size(210, 0)

        _generate = Add(New PillButton() With {.Text = "產生馬賽克"})
        _generate.Margin = New Padding(0, 14, 0, 4)
        AddHandler _generate.Click, Sub(s, e) RaiseEvent GenerateRequested(Me, EventArgs.Empty)
        _progress = Add(New Aqua.ProgressBar() With {.Visible = False})
        _cancel = New PillButton() With {.Text = "取消", .Width = 76, .Visible = False}
        AddHandler _cancel.Click, Sub(s, e) RaiseEvent CancelRequested(Me, EventArgs.Empty)
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
            Dim i = _ratio.SelectedIndex - 1
            Return If(i >= 0 AndAlso i < CanvasPresets.All.Count, CanvasPresets.All(i), Nothing)
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
            If preset IsNot Nothing Then _ratio.SelectedIndex = CanvasPresets.All.ToList().IndexOf(preset) + 1
        End If
        _columns.Value = m.Columns
        _maxRepeat.Value = Math.Min(50, m.MaxRepeat)
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

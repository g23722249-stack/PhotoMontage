Imports System.Drawing
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>「自由拼貼」頁：選取照片的外框、大小、旋轉、陰影、圖層，以及整張作品的散佈。</summary>
Friend Class FreePanel
    Inherits StackPanel

    Private _project As MontageProject
    Private _selection As IReadOnlyList(Of FreeItem) = New List(Of FreeItem)
    Private _updating As Boolean

    Private ReadOnly _selectionLabel As System.Windows.Forms.Label
    Private ReadOnly _editors As New List(Of Control)
    Private ReadOnly _frame As SegmentedChoice
    Private ReadOnly _frameWidth As LabeledSlider
    Private ReadOnly _size As LabeledSlider
    Private ReadOnly _rotation As LabeledSlider
    Private ReadOnly _shadow As Aqua.CheckBox
    Private ReadOnly _looseness As LabeledSlider
    Private ReadOnly _style As Aqua.DropDownList
    Private ReadOnly _overlap As Aqua.CheckBox
    Private ReadOnly _direction As SegmentedChoice
    Private ReadOnly _directionLabel As System.Windows.Forms.Label

    ' 順序與 Core.ArrangeStyle 相同
    Private Shared ReadOnly StyleNames As String() = {"隨機散佈", "整齊格狀", "螺旋", "圓環", "愛心", "扇形", "照片堆", "斜向瀑布"}

    ''' <summary>即將變更（供復原記錄）。</summary>
    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)

    ''' <summary>照片設定已變更。</summary>
    Public Event ItemsChanged As EventHandler

    ''' <summary>要求依目前的排列方式重新擺放所有照片。</summary>
    Public Event ArrangeRequested As EventHandler

    ''' <summary>要求對選取的照片執行圖層命令。</summary>
    Public Event CommandRequested As EventHandler(Of FreeCommandEventArgs)

    Private ReadOnly _help As New HelpToolTip(Me)

    Public Sub New()
        _selectionLabel = AddLabel("")
        _selectionLabel.Font = New Font(Font, FontStyle.Bold)

        Dim frameLabel = Editor(AddLabel("外框"))
        _frame = Editor(Add(New SegmentedChoice("無", "白邊", "拍立得")))
        AddHandler _frame.SelectedChanged, Sub(s, e) Apply(Nothing, Sub(i) i.Frame = CType(Math.Max(0, _frame.SelectedIndex), Core.FrameStyle))

        Dim frameWidthLabel = Editor(AddLabel("外框寬度"))
        _frameWidth = Editor(Add(New LabeledSlider(0, 10, Function(v) $"{v}%")))
        AddHandler _frameWidth.ValueChanged, Sub(s, e) Apply("free:frame-width", Sub(i) i.FrameWidth = _frameWidth.Value / 100.0F)

        Dim sizeLabel = Editor(AddLabel("大小（畫布寬度）"))
        _size = Editor(Add(New LabeledSlider(4, 100, Function(v) $"{v}%")))
        AddHandler _size.ValueChanged, Sub(s, e) Apply("free:size", Sub(i) i.Width = _size.Value / 100.0F)

        Dim rotationLabel = Editor(AddLabel("旋轉"))
        _rotation = Editor(Add(New LabeledSlider(-180, 180, Function(v) $"{v}°")))
        AddHandler _rotation.ValueChanged, Sub(s, e) Apply("free:rotation", Sub(i) i.Rotation = _rotation.Value)

        _shadow = Editor(Add(New Aqua.CheckBox() With {.Text = "陰影"}))
        _shadow.Margin = New Padding(0, 8, 0, 2)
        AddHandler _shadow.CheckedChanged, Sub(s, e) Apply(Nothing, Sub(i) i.Shadow = _shadow.Checked)

        Dim crop = Editor(Add(New PillButton() With {.Text = "裁切…"}))
        crop.Margin = New Padding(0, 10, 0, 4)
        AddHandler crop.Click, Sub(s, e) RaiseEvent CommandRequested(Me, New FreeCommandEventArgs(FreeCommand.Crop))

        Editor(AddLabel("圖層順序"))
        Dim front As New PillButton() With {.Text = "移到最上層", .Width = 104}
        AddHandler front.Click, Sub(s, e) RaiseEvent CommandRequested(Me, New FreeCommandEventArgs(FreeCommand.BringToFront))
        Dim back As New PillButton() With {.Text = "移到最下層", .Width = 104}
        AddHandler back.Click, Sub(s, e) RaiseEvent CommandRequested(Me, New FreeCommandEventArgs(FreeCommand.SendToBack))
        Editor(AddRow(front, back))

        Dim applyAll = Editor(Add(New PillButton() With {.Text = "外框套用到全部照片"}))
        AddHandler applyAll.Click, AddressOf OnApplyFrameToAll

        Dim whole = AddLabel("整張作品")
        whole.Font = New Font(Font, FontStyle.Bold)
        whole.Margin = New Padding(0, 16, 0, 2)

        Dim styleLabel = AddLabel("排列方式")
        _style = Add(New Aqua.DropDownList())
        For i = 0 To StyleNames.Length - 1
            _style.AddItem(i.ToString(), StyleNames(i))
        Next
        AddHandler _style.SelectedChanged, Sub(s, e) ChangeArrangeOption("free:style", Sub(f) f.Style = CType(Math.Max(0, _style.SelectedIndex), ArrangeStyle))

        _overlap = Add(New Aqua.CheckBox() With {.Text = "照片可以重疊"})
        _overlap.Margin = New Padding(0, 6, 0, 2)
        AddHandler _overlap.CheckedChanged, Sub(s, e) ChangeArrangeOption(Nothing, Sub(f) f.Overlap = _overlap.Checked)

        _directionLabel = AddLabel("方向")
        _direction = Add(New SegmentedChoice("順時針", "逆時針"))
        AddHandler _direction.SelectedChanged, Sub(s, e) ChangeArrangeOption(Nothing, Sub(f) f.Clockwise = _direction.SelectedIndex <> 1)

        Dim loosenessLabel = AddLabel("隨性程度")
        _looseness = Add(New LabeledSlider(0, 10, Function(v) If(v = 0, "整齊", If(v <= 3, "低", If(v <= 7, "中", "高")))))
        AddHandler _looseness.ValueChanged, Sub(s, e) ChangeArrangeOption("free:looseness", Sub(f) f.Looseness = _looseness.Value / 10.0F)
        Dim hint = AddLabel("隨性程度影響位置偏移與傾斜角度。按「套用排列」重新擺放，每按一次換一種變化。")
        hint.ForeColor = SystemColors.GrayText
        hint.MaximumSize = New Size(210, 0)

        Dim arrange = Add(New PillButton() With {.Text = "套用排列"})
        AddHandler arrange.Click, Sub(s, e) RaiseEvent ArrangeRequested(Me, EventArgs.Empty)

        Dim tips = AddLabel("從左側拖曳縮圖到畫布可加入照片。" & vbLf & "Ctrl+點選或框選可多選；方向鍵微調位置；Delete 從畫布移除。")
        tips.ForeColor = SystemColors.GrayText
        tips.MaximumSize = New Size(210, 0)
        tips.Margin = New Padding(0, 12, 0, 2)

        _help.SetHelp(HelpTexts.FreeFrame, frameLabel, _frame)
        _help.SetHelp(HelpTexts.FreeFrameWidth, frameWidthLabel, _frameWidth)
        _help.SetHelp(HelpTexts.FreeSize, sizeLabel, _size)
        _help.SetHelp(HelpTexts.FreeRotation, rotationLabel, _rotation)
        _help.SetHelp(HelpTexts.FreeShadow, _shadow)
        _help.SetHelp(HelpTexts.FreeCrop, crop)
        _help.SetHelp(HelpTexts.FreeBringToFront, front)
        _help.SetHelp(HelpTexts.FreeSendToBack, back)
        _help.SetHelp(HelpTexts.FreeApplyFrameToAll, applyAll)
        _help.SetHelp(HelpTexts.FreeLooseness, loosenessLabel, _looseness)
        _help.SetHelp(HelpTexts.FreeStyle, styleLabel, _style)
        _help.SetHelp(HelpTexts.FreeOverlap, _overlap)
        _help.SetHelp(HelpTexts.FreeDirection, _directionLabel, _direction)
        _help.SetHelp(HelpTexts.FreeArrange, arrange)

        SetSelection(New List(Of FreeItem))
    End Sub

    Private Function Editor(Of T As Control)(control As T) As T
        _editors.Add(control)
        Return control
    End Function

    Public Sub Bind(project As MontageProject)
        _project = project
        RefreshFromProject()
    End Sub

    ''' <summary>畫布的選取改變時呼叫。</summary>
    Public Sub SetSelection(items As IReadOnlyList(Of FreeItem))
        _selection = If(items, New List(Of FreeItem))
        RefreshFromProject()
    End Sub

    ''' <summary>照片被畫布或復原改變後重新顯示（以第一張選取的照片為準）。</summary>
    Public Sub RefreshFromProject()
        _updating = True
        Dim has = _selection.Count > 0
        For Each c In _editors
            c.Enabled = has
        Next
        If _selection.Count = 0 Then
            _selectionLabel.Text = "未選取照片"
        ElseIf _selection.Count = 1 Then
            _selectionLabel.Text = If(_project?.FindPhoto(_selection(0).PhotoId)?.FileName, "選取的照片")
        Else
            _selectionLabel.Text = $"已選取 {_selection.Count} 張"
        End If
        If has Then
            Dim first = _selection(0)
            _frame.SelectedIndex = CInt(first.Frame)
            _frameWidth.Value = CInt(Math.Round(first.FrameWidth * 100))
            _size.Value = CInt(Math.Round(first.Width * 100))
            _rotation.Value = CInt(Math.Round(first.Rotation))
            _shadow.Checked = first.Shadow
        End If
        If _project IsNot Nothing Then
            Dim f = _project.Free
            _looseness.Value = CInt(Math.Round(f.Looseness * 10))
            _style.SelectedIndex = CInt(f.Style)
            _overlap.Checked = f.Overlap
            _direction.SelectedIndex = If(f.Clockwise, 0, 1)
        End If
        UpdateArrangeOptionState()
        _updating = False
    End Sub

    ''' <summary>修改排列設定（不移動照片，按「套用排列」才重新擺放）。</summary>
    Private Sub ChangeArrangeOption(key As String, change As Action(Of FreeLayoutSettings))
        If _updating OrElse _project Is Nothing Then Return
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(key))
        change(_project.Free)
        UpdateArrangeOptionState()
    End Sub

    ''' <summary>只有部分排列方式能選重疊與方向，其餘停用。</summary>
    Private Sub UpdateArrangeOptionState()
        Dim style = CType(Math.Max(0, _style.SelectedIndex), ArrangeStyle)
        _overlap.Enabled = FreeArrangeStyles.SupportsOverlap(style)
        Dim directional = FreeArrangeStyles.SupportsDirection(style)
        _direction.Enabled = directional
        _directionLabel.Enabled = directional
    End Sub

    Private Sub Apply(key As String, change As Action(Of FreeItem))
        If _updating OrElse _selection.Count = 0 Then Return
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(key))
        For Each item In _selection
            change(item)
        Next
        RaiseEvent ItemsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnApplyFrameToAll(sender As Object, e As EventArgs)
        If _project Is Nothing OrElse _selection.Count = 0 Then Return
        Dim source = _selection(0)
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(Nothing))
        For Each item In _project.Free.Items
            item.Frame = source.Frame
            item.FrameWidth = source.FrameWidth
            item.Shadow = source.Shadow
        Next
        RaiseEvent ItemsChanged(Me, EventArgs.Empty)
    End Sub
End Class

Friend Class FreeCommandEventArgs
    Inherits EventArgs

    Public ReadOnly Property Command As FreeCommand

    Public Sub New(command As FreeCommand)
        Me.Command = command
    End Sub
End Class

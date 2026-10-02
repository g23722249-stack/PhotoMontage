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

    ''' <summary>即將變更（供復原記錄）。</summary>
    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)

    ''' <summary>照片設定已變更。</summary>
    Public Event ItemsChanged As EventHandler

    ''' <summary>要求自動散佈（<see cref="ArrangeRequestedEventArgs.Tidy"/> 為「整齊排列」）。</summary>
    Public Event ArrangeRequested As EventHandler(Of ArrangeRequestedEventArgs)

    ''' <summary>要求對選取的照片執行圖層命令。</summary>
    Public Event CommandRequested As EventHandler(Of FreeCommandEventArgs)

    Public Sub New()
        _selectionLabel = AddLabel("")
        _selectionLabel.Font = New Font(Font, FontStyle.Bold)

        Editor(AddLabel("外框"))
        _frame = Editor(Add(New SegmentedChoice("無", "白邊", "拍立得")))
        AddHandler _frame.SelectedChanged, Sub(s, e) Apply(Nothing, Sub(i) i.Frame = CType(Math.Max(0, _frame.SelectedIndex), Core.FrameStyle))

        Editor(AddLabel("外框寬度"))
        _frameWidth = Editor(Add(New LabeledSlider(0, 10, Function(v) $"{v}%")))
        AddHandler _frameWidth.ValueChanged, Sub(s, e) Apply("free:frame-width", Sub(i) i.FrameWidth = _frameWidth.Value / 100.0F)

        Editor(AddLabel("大小（畫布寬度）"))
        _size = Editor(Add(New LabeledSlider(4, 100, Function(v) $"{v}%")))
        AddHandler _size.ValueChanged, Sub(s, e) Apply("free:size", Sub(i) i.Width = _size.Value / 100.0F)

        Editor(AddLabel("旋轉"))
        _rotation = Editor(Add(New LabeledSlider(-180, 180, Function(v) $"{v}°")))
        AddHandler _rotation.ValueChanged, Sub(s, e) Apply("free:rotation", Sub(i) i.Rotation = _rotation.Value)

        _shadow = Editor(Add(New Aqua.CheckBox() With {.Text = "陰影"}))
        _shadow.Margin = New Padding(0, 8, 0, 2)
        AddHandler _shadow.CheckedChanged, Sub(s, e) Apply(Nothing, Sub(i) i.Shadow = _shadow.Checked)

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

        AddLabel("隨性程度")
        _looseness = Add(New LabeledSlider(0, 10, Function(v) If(v = 0, "整齊", If(v <= 3, "低", If(v <= 7, "中", "高")))))
        AddHandler _looseness.ValueChanged, Sub(s, e)
                                                If _updating OrElse _project Is Nothing Then Return
                                                RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs("free:looseness"))
                                                _project.Free.Looseness = _looseness.Value / 10.0F
                                            End Sub
        Dim hint = AddLabel("影響「自動散佈」時的位置偏移、傾斜角度與重疊量。")
        hint.ForeColor = SystemColors.GrayText
        hint.MaximumSize = New Size(210, 0)

        Dim scatter = Add(New PillButton() With {.Text = "自動散佈"})
        AddHandler scatter.Click, Sub(s, e) RaiseEvent ArrangeRequested(Me, New ArrangeRequestedEventArgs(False))
        Dim tidy = Add(New PillButton() With {.Text = "整齊排列"})
        AddHandler tidy.Click, Sub(s, e) RaiseEvent ArrangeRequested(Me, New ArrangeRequestedEventArgs(True))

        Dim tips = AddLabel("從左側拖曳縮圖到畫布可加入照片。" & vbLf & "Ctrl+點選或框選可多選；方向鍵微調位置；Delete 從畫布移除。")
        tips.ForeColor = SystemColors.GrayText
        tips.MaximumSize = New Size(210, 0)
        tips.Margin = New Padding(0, 12, 0, 2)

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
        If _project IsNot Nothing Then _looseness.Value = CInt(Math.Round(_project.Free.Looseness * 10))
        _updating = False
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

Friend Class ArrangeRequestedEventArgs
    Inherits EventArgs

    Public ReadOnly Property Tidy As Boolean

    Public Sub New(tidy As Boolean)
        Me.Tidy = tidy
    End Sub
End Class

Friend Class FreeCommandEventArgs
    Inherits EventArgs

    Public ReadOnly Property Command As FreeCommand

    Public Sub New(command As FreeCommand)
        Me.Command = command
    End Sub
End Class

Imports System.Drawing
Imports System.Drawing.Text
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>「文字」頁：新增文字與編輯選取的文字圖層。</summary>
Friend Class TextPanel
    Inherits StackPanel


    Private _layer As TextLayer
    Private _updating As Boolean

    Private ReadOnly _hint As System.Windows.Forms.Label
    Private ReadOnly _editors As New List(Of Control)
    Private ReadOnly _text As Aqua.TextBox
    ''' <summary>字型清單保留標準下拉選單：Aqua 下拉選單無法捲動，放不下數百種字型。</summary>
    Private ReadOnly _font As ComboBox
    Private ReadOnly _size As LabeledSlider
    Private ReadOnly _color As ColorButton
    Private ReadOnly _bold As Aqua.CheckBox
    Private ReadOnly _italic As Aqua.CheckBox
    Private ReadOnly _alignment As SegmentedChoice
    Private ReadOnly _outline As LabeledSlider
    Private ReadOnly _outlineColor As ColorButton
    Private ReadOnly _shadow As Aqua.CheckBox
    Private ReadOnly _shadowColor As ColorButton
    Private ReadOnly _rotation As LabeledSlider

    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)
    Public Event TextChangedByUser As EventHandler
    Public Event AddRequested As EventHandler
    Public Event DeleteRequested As EventHandler

    Public Sub New()
        Dim addButton = Add(New PillButton() With {.Text = "新增文字"})
        AddHandler addButton.Click, Sub(s, e) RaiseEvent AddRequested(Me, EventArgs.Empty)

        _hint = AddLabel("在畫布上點選文字即可編輯。" & vbLf & "拖曳移動、拖曳上方圓點旋轉（按住 Shift 每 15°）、Ctrl+滾輪調整大小、雙擊編輯內容。")
        _hint.ForeColor = SystemColors.GrayText
        _hint.MaximumSize = New Size(210, 0)

        Editor(AddLabel("內容"))
        _text = Editor(Add(New Aqua.TextBox() With {.Multiline = True, .ScrollBars = ScrollBars.Vertical, .Height = 64}))
        AddHandler _text.TextChanged, Sub(s, e) Apply("text:content", Sub(t) t.Text = _text.Text)

        Editor(AddLabel("字型"))
        _font = Editor(Add(New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .MaxDropDownItems = 20}))
        Using fonts As New InstalledFontCollection()
            For Each family In fonts.Families
                _font.Items.Add(family.Name)
            Next
        End Using
        AddHandler _font.SelectedIndexChanged, Sub(s, e) Apply(Nothing, Sub(t) t.FontFamily = CStr(_font.SelectedItem))

        Editor(AddLabel("大小"))
        _size = Editor(Add(New LabeledSlider(1, 40, Function(v) $"{v * 0.5:0.#}%")))
        AddHandler _size.ValueChanged, Sub(s, e) Apply("text:size", Sub(t) t.FontSize = _size.Value * 0.005F)

        Editor(AddLabel("顏色"))
        _color = Editor(Add(New ColorButton()))
        AddHandler _color.ColorPicked, Sub(s, e) Apply(Nothing, Sub(t) t.Color = _color.SelectedColor)

        _bold = New Aqua.CheckBox() With {.Text = "粗體"}
        _italic = New Aqua.CheckBox() With {.Text = "斜體"}
        Editor(AddRow(_bold, _italic))
        AddHandler _bold.CheckedChanged, Sub(s, e) Apply(Nothing, Sub(t) t.Bold = _bold.Checked)
        AddHandler _italic.CheckedChanged, Sub(s, e) Apply(Nothing, Sub(t) t.Italic = _italic.Checked)

        Editor(AddLabel("對齊"))
        _alignment = Editor(Add(New SegmentedChoice("靠左", "置中", "靠右")))
        AddHandler _alignment.SelectedChanged, Sub(s, e) Apply(Nothing, Sub(t) t.Alignment = CType(Math.Max(0, _alignment.SelectedIndex), StringAlignment))

        Editor(AddLabel("外框"))
        _outline = Editor(Add(New LabeledSlider(0, 20, Function(v) If(v = 0, "無", $"{v}%"))))
        AddHandler _outline.ValueChanged, Sub(s, e) Apply("text:outline", Sub(t) t.OutlineWidth = _outline.Value / 100.0F)
        _outlineColor = Editor(Add(New ColorButton() With {.ButtonText = "外框色…"}))
        AddHandler _outlineColor.ColorPicked, Sub(s, e) Apply(Nothing, Sub(t) t.OutlineColor = _outlineColor.SelectedColor)

        _shadow = Editor(Add(New Aqua.CheckBox() With {.Text = "陰影"}))
        _shadow.Margin = New Padding(0, 8, 0, 2)
        AddHandler _shadow.CheckedChanged, Sub(s, e) Apply(Nothing, Sub(t) t.ShadowEnabled = _shadow.Checked)
        _shadowColor = Editor(Add(New ColorButton() With {.ButtonText = "陰影色…", .PreserveAlpha = True}))
        AddHandler _shadowColor.ColorPicked, Sub(s, e) Apply(Nothing, Sub(t) t.ShadowColor = _shadowColor.SelectedColor)

        Editor(AddLabel("旋轉（度）"))
        _rotation = Editor(Add(New LabeledSlider(-180, 180, Function(v) $"{v}°")))
        AddHandler _rotation.ValueChanged, Sub(s, e) Apply("text:rotation", Sub(t) t.Rotation = _rotation.Value)

        Dim delete = Editor(Add(New PillButton() With {.Text = "刪除這段文字"}))
        delete.Margin = New Padding(0, 12, 0, 2)
        AddHandler delete.Click, Sub(s, e) RaiseEvent DeleteRequested(Me, EventArgs.Empty)

        Bind(Nothing)
    End Sub

    Private Function Editor(Of T As Control)(control As T) As T
        _editors.Add(control)
        Return control
    End Function

    ''' <summary>顯示並編輯指定的文字圖層；Nothing 表示沒有選取。</summary>
    Public Sub Bind(layer As TextLayer)
        _layer = layer
        RefreshFromLayer()
    End Sub

    ''' <summary>文字被畫布或復原改變後重新顯示。</summary>
    Public Sub RefreshFromLayer()
        _updating = True
        Dim has = _layer IsNot Nothing
        For Each c In _editors
            c.Enabled = has
        Next
        If has Then
            If _text.Text <> _layer.Text Then _text.Text = _layer.Text
            Dim fontIndex = _font.Items.IndexOf(_layer.FontFamily)
            _font.SelectedIndex = fontIndex
            _size.Value = CInt(Math.Round(_layer.FontSize / 0.005F))
            _color.SelectedColor = _layer.Color
            _bold.Checked = _layer.Bold
            _italic.Checked = _layer.Italic
            _alignment.SelectedIndex = CInt(_layer.Alignment)
            _outline.Value = CInt(Math.Round(_layer.OutlineWidth * 100))
            _outlineColor.SelectedColor = _layer.OutlineColor
            _shadow.Checked = _layer.ShadowEnabled
            _shadowColor.SelectedColor = _layer.ShadowColor
            _rotation.Value = CInt(Math.Max(-180, Math.Min(180, Math.Round(_layer.Rotation))))
        End If
        _updating = False
    End Sub

    ''' <summary>把焦點移到內容輸入框並全選。</summary>
    Public Sub FocusText()
        If _layer Is Nothing Then Return
        _text.Focus()
        _text.SelStart = 0
        _text.SelLength = If(_text.Text, "").Length
    End Sub

    Private Sub Apply(key As String, change As Action(Of TextLayer))
        If _updating OrElse _layer Is Nothing Then Return
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(If(key Is Nothing, Nothing, key & ":" & _layer.Id)))
        change(_layer)
        RaiseEvent TextChangedByUser(Me, EventArgs.Empty)
    End Sub
End Class

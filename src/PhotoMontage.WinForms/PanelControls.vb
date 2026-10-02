Imports System.Drawing
Imports System.Windows.Forms

''' <summary>顏色色塊＋Aqua「選擇…」按鈕，點擊開啟色彩對話框。</summary>
Friend Class ColorButton
    Inherits UserControl

    Private _color As Color = Color.White
    Private ReadOnly _swatch As System.Windows.Forms.Panel
    Private ReadOnly _button As Aqua.FlashButton

    ''' <summary>使用者選了新顏色。</summary>
    Public Event ColorPicked As EventHandler

    ''' <summary>選色時保留原本的透明度（例如半透明陰影）。</summary>
    Public Property PreserveAlpha As Boolean

    Public Sub New()
        BackColor = Color.Transparent
        _swatch = New System.Windows.Forms.Panel() With {.Dock = DockStyle.Left, .Width = 44, .BorderStyle = BorderStyle.FixedSingle}
        _button = New Aqua.FlashButton() With {.Text = "選擇…", .Dock = DockStyle.Fill}
        AddHandler _button.Click, AddressOf OnButtonClick
        Dim gap As New System.Windows.Forms.Panel() With {.Dock = DockStyle.Left, .Width = 6, .BackColor = Color.Transparent}
        Controls.Add(_button)
        Controls.Add(gap)
        Controls.Add(_swatch)
        Height = Math.Max(26, _button.Height)
        UpdateSwatch()
    End Sub

    ''' <summary>按鈕上的文字。</summary>
    Public Property ButtonText As String
        Get
            Return _button.Text
        End Get
        Set(value As String)
            _button.Text = value
        End Set
    End Property

    Public Property SelectedColor As Color
        Get
            Return _color
        End Get
        Set(value As Color)
            _color = value
            UpdateSwatch()
        End Set
    End Property

    Private Sub UpdateSwatch()
        _swatch.BackColor = Color.FromArgb(255, _color)
    End Sub

    Private Sub OnButtonClick(sender As Object, e As EventArgs)
        Using dlg As New ColorDialog() With {.Color = Color.FromArgb(255, _color), .FullOpen = True, .AnyColor = True}
            If dlg.ShowDialog(FindForm()) <> DialogResult.OK Then Return
            SelectedColor = If(PreserveAlpha, Color.FromArgb(_color.A, dlg.Color), dlg.Color)
            RaiseEvent ColorPicked(Me, EventArgs.Empty)
        End Using
    End Sub
End Class

''' <summary>由上而下排列「標題＋控制項」的捲動面板（透明背景，露出 Aqua 分頁的底紋）。</summary>
Friend Class StackPanel
    Inherits FlowLayoutPanel

    Public Sub New()
        FlowDirection = FlowDirection.TopDown
        WrapContents = False
        AutoScroll = True
        Padding = New Padding(4)
        BackColor = Color.Transparent
    End Sub

    Public Function AddLabel(text As String) As System.Windows.Forms.Label
        Dim label As New System.Windows.Forms.Label() With {.Text = text, .AutoSize = True, .Margin = New Padding(0, 8, 0, 2), .BackColor = Color.Transparent}
        Controls.Add(label)
        Return label
    End Function

    Public Function Add(Of T As Control)(control As T) As T
        control.Margin = New Padding(0, 0, 0, 2)
        Controls.Add(control)
        StretchChild(control)
        Return control
    End Function

    Public Function AddRow(ParamArray items As Control()) As FlowLayoutPanel
        Dim row As New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 2), .BackColor = Color.Transparent}
        For Each c In items
            c.Margin = New Padding(0, 0, 8, 0)
            row.Controls.Add(c)
        Next
        Controls.Add(row)
        Return row
    End Function

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        For Each c As Control In Controls
            StretchChild(c)
        Next
        MyBase.OnLayout(levent)
    End Sub

    ''' <summary>寬度填滿；標籤、列、Aqua 核取方塊（自行計算大小）除外。</summary>
    Private Sub StretchChild(c As Control)
        If TypeOf c Is System.Windows.Forms.Label OrElse TypeOf c Is FlowLayoutPanel OrElse
           TypeOf c Is Aqua.CheckBox OrElse TypeOf c Is Aqua.RadioButton Then Return
        Dim w = ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth
        If w > 50 AndAlso c.Width <> w Then c.Width = w
    End Sub
End Class

''' <summary>Aqua 滑桿＋數值顯示。</summary>
Friend Class LabeledSlider
    Inherits UserControl

    Private ReadOnly _slider As Aqua.Slider
    Private ReadOnly _value As System.Windows.Forms.Label
    Private ReadOnly _format As Func(Of Integer, String)

    Public Event ValueChanged As EventHandler

    Public Sub New(minimum As Integer, maximum As Integer, format As Func(Of Integer, String))
        _format = format
        BackColor = Color.Transparent
        _slider = New Aqua.Slider() With {.Minimum = minimum, .Maximum = maximum, .Dock = DockStyle.Fill, .ShowTicks = False}
        _value = New System.Windows.Forms.Label() With {.Dock = DockStyle.Right, .Width = 52, .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}
        Controls.Add(_slider)
        Controls.Add(_value)
        Height = 28
        AddHandler _slider.ValueChanged, Sub(s, e)
                                              _value.Text = _format(_slider.Value)
                                              RaiseEvent ValueChanged(Me, EventArgs.Empty)
                                          End Sub
        _value.Text = _format(_slider.Value)
    End Sub

    Public Property Value As Integer
        Get
            Return _slider.Value
        End Get
        Set(value As Integer)
            _slider.Value = Math.Max(_slider.Minimum, Math.Min(_slider.Maximum, value))
            _value.Text = _format(_slider.Value)
        End Set
    End Property
End Class

''' <summary>Aqua 分段按鈕（例如「靠左｜置中｜靠右」），以索引選擇。</summary>
Friend Class SegmentedChoice
    Inherits Aqua.Buttons

    Public Sub New(ParamArray captions As String())
        For Each caption In captions
            AdditionButton(caption, Nothing)
        Next
    End Sub
End Class

''' <summary>把主題色套用到所有 Aqua 控制項（有 <c>Color As Aqua.ColorConstants</c> 屬性者）。</summary>
Friend Module AquaTheme

    Public Sub Apply(root As Control, color As Aqua.ColorConstants)
        If root Is Nothing Then Return
        Try
            Dim prop = root.GetType().GetProperty("Color", GetType(Aqua.ColorConstants))
            If prop IsNot Nothing AndAlso prop.CanWrite Then prop.SetValue(root, color)
        Catch ex As Reflection.AmbiguousMatchException
            ' 衍生類別重新宣告了 Color：略過，不影響功能
        End Try
        For Each child As Control In root.Controls
            Apply(child, color)
        Next
        Dim tabs = TryCast(root, Aqua.TabControl)
        If tabs IsNot Nothing Then
            ' 未顯示的分頁不在 Controls 中
            For Each page In tabs.TabPages
                Apply(page, color)
            Next
        End If
    End Sub

End Module

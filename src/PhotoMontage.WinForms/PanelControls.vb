Imports System.Drawing
Imports System.Windows.Forms

''' <summary>顯示目前顏色、點擊開啟色彩對話框的按鈕。</summary>
Friend Class ColorButton
    Inherits Button

    Private _color As Color = Color.White

    ''' <summary>使用者選了新顏色。</summary>
    Public Event ColorPicked As EventHandler

    ''' <summary>選色時保留原本的透明度（例如半透明陰影）。</summary>
    Public Property PreserveAlpha As Boolean

    Public Sub New()
        Height = 28
        FlatStyle = FlatStyle.Flat
        TextAlign = ContentAlignment.MiddleRight
    End Sub

    Public Property SelectedColor As Color
        Get
            Return _color
        End Get
        Set(value As Color)
            _color = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim swatch As New Rectangle(6, 5, Math.Max(10, Width - 70), Height - 11)
        Using brush As New SolidBrush(Color.FromArgb(255, _color))
            e.Graphics.FillRectangle(brush, swatch)
        End Using
        e.Graphics.DrawRectangle(SystemPens.ControlDark, swatch)
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        MyBase.OnClick(e)
        Using dlg As New ColorDialog() With {.Color = Color.FromArgb(255, _color), .FullOpen = True, .AnyColor = True}
            If dlg.ShowDialog(FindForm()) <> DialogResult.OK Then Return
            SelectedColor = If(PreserveAlpha, Color.FromArgb(_color.A, dlg.Color), dlg.Color)
            RaiseEvent ColorPicked(Me, EventArgs.Empty)
        End Using
    End Sub
End Class

''' <summary>由上而下排列「標題＋控制項」的捲動面板。</summary>
Friend Class StackPanel
    Inherits FlowLayoutPanel

    Public Sub New()
        FlowDirection = FlowDirection.TopDown
        WrapContents = False
        AutoScroll = True
        Padding = New Padding(4)
    End Sub

    Public Function AddLabel(text As String) As Label
        Dim label As New Label() With {.Text = text, .AutoSize = True, .Margin = New Padding(0, 8, 0, 2)}
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
        Dim row As New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 0, 0, 2)}
        For Each c In items
            c.Margin = New Padding(0, 0, 8, 0)
            row.Controls.Add(c)
        Next
        Controls.Add(row)
        Return row
    End Function

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        For Each c As Control In Controls
            If TypeOf c IsNot Label AndAlso TypeOf c IsNot FlowLayoutPanel Then StretchChild(c)
        Next
        MyBase.OnLayout(levent)
    End Sub

    Private Sub StretchChild(c As Control)
        Dim w = ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth
        If w > 50 AndAlso c.Width <> w Then c.Width = w
    End Sub
End Class

''' <summary>附數值顯示的滑桿。</summary>
Friend Class LabeledSlider
    Inherits UserControl

    Private ReadOnly _track As TrackBar
    Private ReadOnly _value As Label
    Private ReadOnly _format As Func(Of Integer, String)

    Public Event ValueChanged As EventHandler

    Public Sub New(minimum As Integer, maximum As Integer, format As Func(Of Integer, String))
        _format = format
        _track = New TrackBar() With {.Minimum = minimum, .Maximum = maximum, .TickStyle = TickStyle.None, .Dock = DockStyle.Fill,
                                      .LargeChange = Math.Max(1, (maximum - minimum) \ 10)}
        _value = New Label() With {.Dock = DockStyle.Right, .Width = 48, .TextAlign = ContentAlignment.MiddleRight}
        Controls.Add(_track)
        Controls.Add(_value)
        Height = 32
        AddHandler _track.ValueChanged, Sub(s, e)
                                             _value.Text = _format(_track.Value)
                                             RaiseEvent ValueChanged(Me, EventArgs.Empty)
                                         End Sub
        _value.Text = _format(_track.Value)
    End Sub

    Public Property Value As Integer
        Get
            Return _track.Value
        End Get
        Set(value As Integer)
            _track.Value = Math.Max(_track.Minimum, Math.Min(_track.Maximum, value))
            _value.Text = _format(_track.Value)
        End Set
    End Property
End Class

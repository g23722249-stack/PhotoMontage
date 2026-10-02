Imports System.Drawing
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>「樣式」頁：間距、圓角、背景色、背景圖。</summary>
Friend Class StylePanel
    Inherits StackPanel

    Private _project As MontageProject
    Private _updating As Boolean

    Private ReadOnly _gap As LabeledSlider
    Private ReadOnly _radius As LabeledSlider
    Private ReadOnly _background As ColorButton
    Private ReadOnly _bgImageLabel As System.Windows.Forms.Label
    Private ReadOnly _clearBgImage As PillButton
    ''' <summary>只適用於拼貼的設定（間距、圓角）。</summary>
    Private ReadOnly _collageOnly As New List(Of Control)

    ''' <summary>即將變更（供復原記錄）。</summary>
    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)

    ''' <summary>樣式已變更。</summary>
    Public Event DesignChanged As EventHandler

    ''' <summary>使用者選了背景圖檔案（由編輯器檢查後套用）。</summary>
    Public Event BackgroundImageRequested As EventHandler(Of FilesDroppedEventArgs)

    Public Sub New()
        _collageOnly.Add(AddLabel("間距"))
        _gap = Add(New LabeledSlider(0, 16, Function(v) $"{v * 0.5:0.#}%"))
        _collageOnly.Add(_gap)
        AddHandler _gap.ValueChanged, Sub(s, e) Apply("style:gap", Sub(p) p.Collage.Gap = _gap.Value * 0.005F)

        _collageOnly.Add(AddLabel("圓角"))
        _radius = Add(New LabeledSlider(0, 50, Function(v) $"{v * 2}%"))
        _collageOnly.Add(_radius)
        AddHandler _radius.ValueChanged, Sub(s, e) Apply("style:radius", Sub(p) p.Collage.CornerRadius = _radius.Value / 100.0F)

        AddLabel("背景色")
        _background = Add(New ColorButton())
        AddHandler _background.ColorPicked, Sub(s, e) Apply(Nothing, Sub(p) p.BackgroundColor = _background.SelectedColor)

        AddLabel("背景圖")
        _bgImageLabel = Add(New System.Windows.Forms.Label() With {.AutoEllipsis = True, .Height = 20, .ForeColor = SystemColors.GrayText, .BackColor = Color.Transparent})
        Dim pick As New PillButton() With {.Text = "選擇圖片…", .Width = 116}
        AddHandler pick.Click, AddressOf OnPickBackgroundImage
        _clearBgImage = New PillButton() With {.Text = "移除", .Width = 76}
        AddHandler _clearBgImage.Click, Sub(s, e) Apply(Nothing, Sub(p) p.BackgroundImagePath = Nothing)
        AddRow(pick, _clearBgImage)

        Dim hint = AddLabel("背景圖會鋪滿整張畫布，從格子間距與空格中露出。")
        hint.ForeColor = SystemColors.GrayText
        hint.MaximumSize = New Size(200, 0)
    End Sub

    ''' <summary>拼貼模式顯示間距與圓角；自由拼貼只需要背景設定。</summary>
    Public Sub SetCollageOptionsVisible(visible As Boolean)
        For Each c In _collageOnly
            c.Visible = visible
        Next
    End Sub

    Public Sub Bind(project As MontageProject)
        _project = project
        RefreshFromProject()
    End Sub

    ''' <summary>專案被外部改變（例如復原）後重新顯示。</summary>
    Public Sub RefreshFromProject()
        If _project Is Nothing Then Return
        _updating = True
        _gap.Value = CInt(Math.Round(_project.Collage.Gap / 0.005F))
        _radius.Value = CInt(Math.Round(_project.Collage.CornerRadius * 100))
        _background.SelectedColor = _project.BackgroundColor
        Dim path = _project.BackgroundImagePath
        _bgImageLabel.Text = If(path Is Nothing, "（無）", IO.Path.GetFileName(path))
        _clearBgImage.Enabled = path IsNot Nothing
        _updating = False
    End Sub

    Private Sub Apply(key As String, change As Action(Of MontageProject))
        If _updating OrElse _project Is Nothing Then Return
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(key))
        change(_project)
        RefreshFromProject()
        RaiseEvent DesignChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnPickBackgroundImage(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog() With {.Title = "選擇背景圖", .Filter = ImageFormatSniffer.DialogFilter}
            If dlg.ShowDialog(FindForm()) = DialogResult.OK Then
                RaiseEvent BackgroundImageRequested(Me, New FilesDroppedEventArgs({dlg.FileName}))
            End If
        End Using
    End Sub
End Class

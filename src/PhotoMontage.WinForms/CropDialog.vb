Imports System.Drawing
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 裁切視窗（Aqua 視窗）：拖曳裁切框選取範圍，可旋轉 90°、水平翻轉。
''' 拼貼模式的比例固定為格子比例；自由拼貼可選比例，裁切後照片形狀跟著改變。
''' </summary>
Friend Class CropDialog
    Inherits Aqua.AquaForm

    ''' <summary>比例選項；0 = 原始（照片本身）、-1 = 自由。</summary>
    Private Shared ReadOnly RatioNames As String() = {"原始比例", "自由", "1:1 正方形", "4:3", "3:4", "3:2", "2:3", "16:9", "9:16"}
    Private Shared ReadOnly RatioValues As Double() = {0, -1, 1, 4 / 3, 3 / 4, 3 / 2, 2 / 3, 16 / 9, 9 / 16}

    Private ReadOnly _loader As Func(Of Bitmap)
    Private ReadOnly _fixedAspect As Double
    Private ReadOnly _initialCrop As CropInfo
    Private ReadOnly _initialAspect As Double
    Private ReadOnly _photoSize As Size
    Private _source As Bitmap
    Private _display As Bitmap
    Private _rotation As Integer
    Private _flip As Boolean
    Private _updating As Boolean

    Private ReadOnly _editor As CropEditor
    Private ReadOnly _ratio As Aqua.DropDownList
    Private ReadOnly _info As System.Windows.Forms.Label
    Private ReadOnly _okButton As PillButton
    Private ReadOnly _toolButtons As New List(Of Control)

    ''' <summary>確定後的取景（含旋轉與翻轉）。</summary>
    Public Property ResultCrop As CropInfo
    ''' <summary>確定後裁切範圍的長寬比（寬 / 高，轉正後）。</summary>
    Public Property ResultAspect As Double

    ''' <param name="loader">在背景執行，回傳要裁切的照片（已依 EXIF 轉正，尚未套用 <paramref name="crop"/> 的旋轉）。</param>
    ''' <param name="fixedAspect">拼貼格子的比例；0 表示可選比例（自由拼貼）。</param>
    ''' <param name="currentAspect">自由拼貼目前的照片比例（<see cref="FreeItem.InnerAspect"/>）。</param>
    ''' <param name="photoSize">原圖像素大小（已依 EXIF 轉正），用來顯示裁切後的像素數。</param>
    Public Sub New(loader As Func(Of Bitmap), crop As CropInfo, fixedAspect As Double, currentAspect As Double,
                   photoSize As Size, fileName As String, Optional accent As Aqua.ColorConstants = Aqua.ColorConstants.Blue)
        _loader = loader
        _fixedAspect = fixedAspect
        _initialCrop = If(crop, New CropInfo())
        _initialAspect = currentAspect
        _photoSize = photoSize
        _rotation = PhotoOrientation.NormalizeRotation(_initialCrop.Rotation)
        _flip = _initialCrop.FlipHorizontal

        Text = If(String.IsNullOrEmpty(fileName), "裁切", $"裁切 － {fileName}")
        TitleFont = New Font("Microsoft JhengHei UI", 10.0F, FontStyle.Bold)
        MinButton = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(900, 620)
        MinimumSize = New Size(640, 460)

        _editor = New CropEditor() With {.Dock = DockStyle.Fill, .Message = "載入照片中…"}
        AddHandler _editor.BoxChanged, Sub(s, e) UpdateInfo()

        Dim panel As New StackPanel() With {.Dock = DockStyle.Right, .Width = 210, .Padding = New Padding(12, 0, 0, 0)}
        Dim help As New HelpToolTip(Me)

        If _fixedAspect <= 0 Then
            Dim ratioLabel = panel.AddLabel("比例")
            ratioLabel.Margin = New Padding(0, 0, 0, 2)
            _ratio = panel.Add(New Aqua.DropDownList())
            For i = 0 To RatioNames.Length - 1
                _ratio.AddItem(i.ToString(), RatioNames(i))
            Next
            AddHandler _ratio.SelectedChanged, Sub(s, e) If Not _updating Then OnRatioChanged()
            _toolButtons.Add(_ratio)
            help.SetHelp(HelpTexts.CropRatio, ratioLabel, _ratio)
        Else
            Dim fixedLabel = panel.AddLabel("比例固定為格子的形狀。" & vbLf & "要改格子形狀，請在畫布上拖曳格子之間的分隔線。")
            fixedLabel.ForeColor = SystemColors.GrayText
            fixedLabel.MaximumSize = New Size(190, 0)
            fixedLabel.Margin = New Padding(0, 0, 0, 2)
        End If

        panel.AddLabel("旋轉與翻轉")
        Dim rotateLeft As New PillButton() With {.Text = "向左轉", .Width = 92}
        AddHandler rotateLeft.Click, Sub(s, e) Rotate(-1)
        Dim rotateRight As New PillButton() With {.Text = "向右轉", .Width = 92}
        AddHandler rotateRight.Click, Sub(s, e) Rotate(1)
        panel.AddRow(rotateLeft, rotateRight)
        Dim flip = panel.Add(New PillButton() With {.Text = "水平翻轉"})
        AddHandler flip.Click, Sub(s, e) ToggleFlip()
        Dim reset = panel.Add(New PillButton() With {.Text = "重設"})
        AddHandler reset.Click, Sub(s, e) ResetAll()
        _toolButtons.AddRange({rotateLeft, rotateRight, flip, reset})
        help.SetHelp(HelpTexts.CropRotateLeft, rotateLeft)
        help.SetHelp(HelpTexts.CropRotateRight, rotateRight)
        help.SetHelp(HelpTexts.CropFlip, flip)
        help.SetHelp(HelpTexts.CropReset, reset)

        _info = panel.AddLabel("")
        _info.ForeColor = SystemColors.GrayText
        _info.MaximumSize = New Size(190, 0)
        _info.Margin = New Padding(0, 12, 0, 2)

        Dim tips = panel.AddLabel("拖曳框內移動、拖曳四角縮放；滾輪縮放；雙擊框內恢復最大。")
        tips.ForeColor = SystemColors.GrayText
        tips.MaximumSize = New Size(190, 0)
        tips.Margin = New Padding(0, 12, 0, 2)

        _okButton = New PillButton() With {.Text = "確定", .Width = 90, .Enabled = False}
        AddHandler _okButton.Click, AddressOf OnOk
        Dim cancel As New PillButton() With {.Text = "取消", .Width = 90}
        AddHandler cancel.Click, Sub(s, e)
                                     DialogResult = DialogResult.Cancel
                                     Close()
                                 End Sub
        Dim buttons As New FlowLayoutPanel() With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .BackColor = Color.Transparent}
        buttons.Controls.Add(cancel)
        buttons.Controls.Add(_okButton)

        Controls.Add(_editor)
        Controls.Add(panel)
        Controls.Add(buttons)
        For Each c In _toolButtons
            c.Enabled = False
        Next

        AquaTheme.Apply(Me, accent)
    End Sub

    ''' <summary>內容放在 Aqua 標題列下方。在自動縮放之後設定，確保是實際像素。</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Padding = New Padding(12, MontageEditorForm.AquaTitleBarHeight + 10, 12, 12)
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Try
            Dim bitmap = Await Task.Run(_loader)
            If IsDisposed Then
                bitmap.Dispose()
                Return
            End If
            _source = bitmap
        Catch ex As Exception
            ' Async Sub 中未處理的例外會讓整個程式（含宿主）結束，所以一律攔下
            If IsDisposed Then Return
            MessageBox.Show(Me, "無法讀取照片：" & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.Cancel
            Close()
            Return
        End Try

        _editor.Message = Nothing
        For Each c In _toolButtons
            c.Enabled = True
        Next
        _okButton.Enabled = True
        RebuildDisplay()
        ApplyInitialBox()
    End Sub

#Region "狀態"

    Private ReadOnly Property Orientation As CropInfo
        Get
            Return New CropInfo With {.Rotation = _rotation, .FlipHorizontal = _flip}
        End Get
    End Property

    ''' <summary>依旋轉與翻轉產生顯示用的照片（與匯出使用同一套轉換）。</summary>
    Private Sub RebuildDisplay()
        Dim size = PhotoOrientation.OrientedSize(New Size(_source.Width, _source.Height), Orientation)
        Dim bitmap As New Bitmap(size.Width, size.Height)
        Using g = Graphics.FromImage(bitmap)
            Dim r As New RectangleF(0, 0, size.Width, size.Height)
            PhotoOrientation.Draw(g, _source, r, r, Orientation)
        End Using
        Dim old = _display
        _display = bitmap
        _editor.Image = Nothing
        _editor.LockedAspect = CurrentLockedAspect()
        _editor.Image = _display
        old?.Dispose()
    End Sub

    Private Function DisplayAspect() As Double
        Return _display.Width / CDbl(Math.Max(1, _display.Height))
    End Function

    ''' <summary>目前要鎖定的比例：拼貼為格子比例；自由拼貼依選項（原始 = 照片比例，自由 = 0）。</summary>
    Private Function CurrentLockedAspect() As Double
        If _fixedAspect > 0 Then Return _fixedAspect
        If _ratio Is Nothing OrElse _display Is Nothing Then Return 0
        Dim i = Math.Max(0, _ratio.SelectedIndex)
        If RatioValues(i) = 0 Then Return DisplayAspect()
        Return Math.Max(0, RatioValues(i))
    End Function

    ''' <summary>開啟時：依現有的取景放好裁切框，並選好對應的比例選項。</summary>
    Private Sub ApplyInitialBox()
        Dim aspect = _fixedAspect
        If aspect <= 0 Then
            aspect = If(_initialAspect > 0, _initialAspect, DisplayAspect())
            _updating = True
            _ratio.SelectedIndex = MatchRatio(aspect)
            _updating = False
            _editor.LockedAspect = CurrentLockedAspect()
        End If
        Dim oriented As New SizeF(_display.Width, _display.Height)
        _editor.Box = CropMath.ToCropRect(oriented, aspect, _initialCrop)
    End Sub

    Private Function MatchRatio(aspect As Double) As Integer
        If Math.Abs(aspect - DisplayAspect()) < 0.01 Then Return 0
        For i = 2 To RatioValues.Length - 1
            If Math.Abs(aspect - RatioValues(i)) < 0.01 Then Return i
        Next
        Return 1
    End Function

    Private Sub OnRatioChanged()
        If _display Is Nothing Then Return
        _editor.LockedAspect = CurrentLockedAspect()
        If _editor.LockedAspect > 0 Then _editor.ResetBox()
        _editor.Invalidate()
    End Sub

    Private Sub Rotate(direction As Integer)
        If _source Is Nothing Then Return
        _rotation = PhotoOrientation.NormalizeRotation(_rotation + direction)
        RebuildDisplay()
    End Sub

    Private Sub ToggleFlip()
        If _source Is Nothing Then Return
        _flip = Not _flip
        ' 翻轉時裁切框跟著鏡像，選好的範圍不變
        Dim box = _editor.Box
        RebuildDisplay()
        _editor.Box = New RectangleF(_display.Width - box.Right, box.Y, box.Width, box.Height)
    End Sub

    Private Sub ResetAll()
        If _source Is Nothing Then Return
        _rotation = 0
        _flip = False
        If _ratio IsNot Nothing Then
            _updating = True
            _ratio.SelectedIndex = 0
            _updating = False
        End If
        RebuildDisplay()
    End Sub

    Private Sub UpdateInfo()
        If _display Is Nothing OrElse _editor.Box.IsEmpty Then
            _info.Text = ""
            Return
        End If
        Dim full = PhotoOrientation.OrientedSize(_photoSize, Orientation)
        If full.Width <= 0 OrElse full.Height <= 0 Then full = New Size(_display.Width, _display.Height)
        Dim box = _editor.Box
        Dim w = CInt(full.Width * box.Width / _display.Width)
        Dim h = CInt(full.Height * box.Height / _display.Height)
        Dim text = $"裁切後 {w} × {h} 像素"
        If Math.Max(w, h) < 1000 Then text &= vbLf & "範圍很小，放大列印時可能不夠清晰。"
        _info.Text = text
    End Sub

#End Region

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _display Is Nothing Then Return
        Dim box = _editor.Box
        ResultCrop = CropMath.FromCropRect(New SizeF(_display.Width, _display.Height), box, Orientation)
        ResultAspect = box.Width / box.Height
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _editor.Image = Nothing
            _display?.Dispose()
            _source?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class

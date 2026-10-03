Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 版型縮圖清單：依照片張數分組，每組以縮圖排成三欄；縮圖依目前畫布比例繪製。
''' 第一個版型為「自動排版」，以示意圖表示。
''' </summary>
Friend NotInheritable Class TemplateGallery
    Inherits ScrollableControl

    Private Const Columns As Integer = 3
    Private Const EdgePadding As Integer = 4
    Private Const TileGap As Integer = 6
    Private Const HeaderHeight As Integer = 22

    Private Shared ReadOnly AutoSampleAspects As Double() = {1.5, 0.75, 1.33, 1.0, 1.5, 0.8}
    Private Shared ReadOnly SelectedColor As Color = Color.FromArgb(47, 123, 216)
    Private Shared ReadOnly CellColor As Color = Color.FromArgb(176, 188, 204)

    Private _templates As IReadOnlyList(Of CollageTemplate) = Array.Empty(Of CollageTemplate)()
    Private _autoSample As CollageTemplate
    Private _selectedId As String
    Private _hover As Integer = -1
    Private _canvasAspect As Double = 1.5
    Private ReadOnly _tiles As New List(Of Rectangle)
    Private ReadOnly _headers As New List(Of (Text As String, Bounds As Rectangle))
    Private ReadOnly _toolTip As New HelpToolTip(Me)

    ''' <summary>使用者點選了另一個版型。</summary>
    Public Event SelectedChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
        AutoScroll = True
        BackColor = Color.FromArgb(244, 246, 249)
    End Sub

    Public Sub SetTemplates(templates As IReadOnlyList(Of CollageTemplate))
        _templates = If(templates, Array.Empty(Of CollageTemplate)())
        RebuildLayout()
    End Sub

    ''' <summary>選取的版型 Id；由程式設定時不觸發 <see cref="SelectedChanged"/>，並捲動到看得見的位置。</summary>
    Public Property SelectedId As String
        Get
            Return _selectedId
        End Get
        Set(value As String)
            If _selectedId = value Then Return
            _selectedId = value
            ScrollToSelected()
            Invalidate()
        End Set
    End Property

    ''' <summary>畫布長寬比；改變時縮圖重新排版。</summary>
    Public Property CanvasAspect As Double
        Get
            Return _canvasAspect
        End Get
        Set(value As Double)
            If value <= 0 OrElse Math.Abs(value - _canvasAspect) < 0.001 Then Return
            _canvasAspect = value
            RebuildLayout()
        End Set
    End Property

#Region "排版"

    Private Sub RebuildLayout()
        _tiles.Clear()
        _headers.Clear()
        _autoSample = JustifiedLayout.Create(AutoSampleAspects, _canvasAspect)
        Dim pad = LogicalToDeviceUnits(EdgePadding), gap = LogicalToDeviceUnits(TileGap), header = LogicalToDeviceUnits(HeaderHeight)
        Dim width = Math.Max(60, ClientSize.Width - pad * 2)
        Dim tileW = (width - gap * (Columns - 1)) \ Columns
        Dim tileH = CInt(tileW / Math.Max(0.6, Math.Min(1.8, _canvasAspect)))
        Dim y = pad
        Dim column = 0
        Dim lastGroup = -2

        For i = 0 To _templates.Count - 1
            Dim group = If(IsAuto(i), -1, _templates(i).CellCount)
            If group <> lastGroup Then
                If column > 0 Then y += tileH + gap
                column = 0
                _headers.Add((If(group < 0, "自動", $"{group} 張"), New Rectangle(pad, y, width, header)))
                y += header
                lastGroup = group
            End If
            _tiles.Add(New Rectangle(pad + column * (tileW + gap), y, tileW, tileH))
            column += 1
            If column = Columns Then
                column = 0
                y += tileH + gap
            End If
        Next
        If column > 0 Then y += tileH + gap
        AutoScrollMinSize = New Size(0, y + pad)
        Invalidate()
    End Sub

    Private Function IsAuto(index As Integer) As Boolean
        Return _templates(index).Id = CollageTemplates.AutoId
    End Function

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If _templates.Count > 0 Then RebuildLayout()
    End Sub

    Private Sub ScrollToSelected()
        Dim i = IndexOf(_selectedId)
        If i < 0 OrElse i >= _tiles.Count Then Return
        Dim r = _tiles(i)
        Dim top = -AutoScrollPosition.Y
        If r.Top < top OrElse r.Bottom > top + ClientSize.Height Then
            AutoScrollPosition = New Point(0, Math.Max(0, r.Top - LogicalToDeviceUnits(HeaderHeight) - LogicalToDeviceUnits(EdgePadding)))
        End If
    End Sub

    Private Function IndexOf(id As String) As Integer
        For i = 0 To _templates.Count - 1
            If _templates(i).Id = id Then Return i
        Next
        Return -1
    End Function

    Private Function HitTest(p As Point) As Integer
        Dim q As New Point(p.X - AutoScrollPosition.X, p.Y - AutoScrollPosition.Y)
        For i = 0 To _tiles.Count - 1
            If _tiles(i).Contains(q) Then Return i
        Next
        Return -1
    End Function

#End Region

#Region "滑鼠"

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim i = HitTest(e.Location)
        If i = _hover Then Return
        _hover = i
        Cursor = If(i >= 0, Cursors.Hand, Cursors.Default)
        If i >= 0 Then
            Dim t = _templates(i)
            Dim text = If(IsAuto(i), "依照片的數量與直橫方向自動安排格子。", $"{t.CellCount} 張照片")
            _toolTip.ShowFor(Me, If(IsAuto(i), "自動排版", t.Name), text)
        Else
            _toolTip.ShowFor(Me, Nothing, Nothing)
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hover >= 0 Then
            _hover = -1
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button <> MouseButtons.Left Then Return
        Dim i = HitTest(e.Location)
        If i < 0 OrElse _templates(i).Id = _selectedId Then Return
        _selectedId = _templates(i).Id
        Invalidate()
        RaiseEvent SelectedChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        ' 讓滾輪直接捲動縮圖清單（不搶走文字輸入框的焦點）
        If Not (TypeOf FindForm()?.ActiveControl Is TextBoxBase) Then Focus()
    End Sub

#End Region

#Region "繪製"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y)
        g.SmoothingMode = SmoothingMode.AntiAlias

        Using headerFont As New Font(Font, FontStyle.Bold)
            For Each h In _headers
                TextRenderer.DrawText(g, h.Text, headerFont, h.Bounds, Color.FromArgb(90, 98, 110),
                                      TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
            Next
        End Using

        For i = 0 To _tiles.Count - 1
            DrawTile(g, i, _tiles(i))
        Next
    End Sub

    Private Sub DrawTile(g As Graphics, index As Integer, r As Rectangle)
        Dim selected = _templates(index).Id = _selectedId
        Dim radius = LogicalToDeviceUnits(5)
        Using path = CollageRenderer.CreateCellPath(r, radius),
              back As New SolidBrush(If(selected, Color.FromArgb(226, 237, 252), If(index = _hover, Color.FromArgb(236, 241, 248), Color.White)))
            g.FillPath(back, path)
            Using border As New Pen(If(selected, SelectedColor, Color.FromArgb(214, 219, 226)), If(selected, 2.0F, 1.0F))
                g.DrawPath(border, path)
            End Using
        End Using

        ' 版型的格子：留邊後依比例縮小，格子之間留一點縫
        Dim inset = LogicalToDeviceUnits(5)
        Dim area = Rectangle.Inflate(r, -inset, -inset)
        Dim cells = If(IsAuto(index), _autoSample.Cells, _templates(index).Cells)
        Dim seam = Math.Max(1.0F, LogicalToDeviceUnits(2) / 2.0F)
        Using fill As New SolidBrush(If(selected, Color.FromArgb(120, 160, 220), CellColor))
            For Each c In cells
                Dim cr As New RectangleF(area.X + c.X * area.Width + seam, area.Y + c.Y * area.Height + seam,
                                         c.Width * area.Width - seam * 2, c.Height * area.Height - seam * 2)
                If cr.Width > 0 AndAlso cr.Height > 0 Then g.FillRectangle(fill, cr)
            Next
        End Using
        If IsAuto(index) Then
            Using badge As New Font(Font.FontFamily, Font.Size * 0.85F, FontStyle.Bold)
                TextRenderer.DrawText(g, "自動", badge, r, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Using
        End If
    End Sub

#End Region

End Class

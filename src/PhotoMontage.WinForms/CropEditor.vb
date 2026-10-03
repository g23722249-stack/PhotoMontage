Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 裁切框編輯器：顯示（已轉正的）照片，上面疊一個可拖曳、縮放的裁切框。
''' 拖曳框內移動、拖曳四角縮放、比例自由時可拖曳四邊；滾輪縮放、雙擊恢復最大。
''' 裁切框以照片像素座標表示。
''' </summary>
Friend NotInheritable Class CropEditor
    Inherits Control

    Private Const ImagePadding As Integer = 20
    Private Const HandleSize As Integer = 9
    Private Const EdgeSize As Integer = 6

    Private Enum DragPart
        None
        Move
        TopLeft
        TopRight
        BottomRight
        BottomLeft
        Left
        Top
        Right
        Bottom
    End Enum

    Private _image As Image
    Private _aspect As Double
    Private _box As RectangleF
    Private _drag As DragPart = DragPart.None
    Private _dragStart As PointF
    Private _boxStart As RectangleF
    Private _message As String

    ''' <summary>裁切框改變（拖曳中也會觸發）。</summary>
    Public Event BoxChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or
                 ControlStyles.UserPaint Or ControlStyles.Selectable, True)
        BackColor = Color.FromArgb(52, 54, 58)
        TabStop = True
    End Sub

    ''' <summary>沒有照片時顯示的文字（例如「載入中…」）。</summary>
    Public Property Message As String
        Get
            Return _message
        End Get
        Set(value As String)
            _message = value
            Invalidate()
        End Set
    End Property

    ''' <summary>要裁切的照片（已轉正）。設定後裁切框恢復最大。</summary>
    Public Property Image As Image
        Get
            Return _image
        End Get
        Set(value As Image)
            _image = value
            ResetBox()
        End Set
    End Property

    ''' <summary>鎖定的長寬比（寬 / 高）；0 表示自由比例。</summary>
    Public Property LockedAspect As Double
        Get
            Return _aspect
        End Get
        Set(value As Double)
            _aspect = Math.Max(0, value)
        End Set
    End Property

    ''' <summary>裁切框（照片像素座標）。</summary>
    Public Property Box As RectangleF
        Get
            Return _box
        End Get
        Set(value As RectangleF)
            _box = ClampToImage(value)
            Invalidate()
            RaiseEvent BoxChanged(Me, EventArgs.Empty)
        End Set
    End Property

    ''' <summary>裁切框恢復為置中、最大（比例鎖定時依比例）。</summary>
    Public Sub ResetBox()
        If _image Is Nothing Then
            _box = RectangleF.Empty
        Else
            Dim max = MaxBoxSize(If(_aspect > 0, _aspect, ImageAspect()))
            _box = New RectangleF((_image.Width - max.Width) / 2, (_image.Height - max.Height) / 2, max.Width, max.Height)
        End If
        Invalidate()
        RaiseEvent BoxChanged(Me, EventArgs.Empty)
    End Sub

#Region "座標"

    Private Function ImageAspect() As Double
        Return _image.Width / CDbl(Math.Max(1, _image.Height))
    End Function

    ''' <summary>指定比例下，照片內放得下的最大裁切框。</summary>
    Private Function MaxBoxSize(aspect As Double) As SizeF
        Dim w As Double = _image.Width, h As Double = _image.Height
        If w / h > aspect Then w = h * aspect Else h = w / aspect
        Return New SizeF(CSng(w), CSng(h))
    End Function

    ''' <summary>
    ''' 最小裁切框：最大框的 1/<see cref="CropMath.MaxScale"/>（再小就超過取景倍率上限，結果不會跟著變）。
    ''' </summary>
    Private Function MinBoxSize(aspect As Double) As SizeF
        Dim max = MaxBoxSize(aspect)
        Dim f = 1.0F / CropMath.MaxScale * 1.01F
        Return New SizeF(max.Width * f, max.Height * f)
    End Function

    Private Function GetImageRect() As RectangleF
        If _image Is Nothing Then Return RectangleF.Empty
        Dim pad = LogicalToDeviceUnits(ImagePadding)
        Dim avail As New RectangleF(pad, pad, Math.Max(1, Width - pad * 2), Math.Max(1, Height - pad * 2))
        Dim s = Math.Min(avail.Width / _image.Width, avail.Height / _image.Height)
        Dim w = _image.Width * s, h = _image.Height * s
        Return New RectangleF(avail.X + (avail.Width - w) / 2, avail.Y + (avail.Height - h) / 2, w, h)
    End Function

    Private Function ScreenScale() As Single
        If _image Is Nothing Then Return 1
        Return GetImageRect().Width / _image.Width
    End Function

    Private Function ToScreen(r As RectangleF) As RectangleF
        Dim ir = GetImageRect()
        Dim s = ScreenScale()
        Return New RectangleF(ir.X + r.X * s, ir.Y + r.Y * s, r.Width * s, r.Height * s)
    End Function

    Private Function ToImage(p As Point) As PointF
        Dim ir = GetImageRect()
        Dim s = ScreenScale()
        Return New PointF((p.X - ir.X) / s, (p.Y - ir.Y) / s)
    End Function

    Private Function ClampToImage(r As RectangleF) As RectangleF
        If _image Is Nothing Then Return r
        Dim w = Math.Min(r.Width, _image.Width), h = Math.Min(r.Height, _image.Height)
        Dim x = Math.Max(0, Math.Min(_image.Width - w, r.X))
        Dim y = Math.Max(0, Math.Min(_image.Height - h, r.Y))
        Return New RectangleF(x, y, w, h)
    End Function

#End Region

#Region "滑鼠"

    Private Function HitTest(p As Point) As DragPart
        If _image Is Nothing OrElse _box.IsEmpty Then Return DragPart.None
        Dim r = ToScreen(_box)
        Dim hs = LogicalToDeviceUnits(HandleSize) + 2
        Dim near = Function(x As Single, y As Single) Math.Abs(p.X - x) <= hs AndAlso Math.Abs(p.Y - y) <= hs
        If near(r.Left, r.Top) Then Return DragPart.TopLeft
        If near(r.Right, r.Top) Then Return DragPart.TopRight
        If near(r.Right, r.Bottom) Then Return DragPart.BottomRight
        If near(r.Left, r.Bottom) Then Return DragPart.BottomLeft
        If _aspect = 0 Then
            Dim es = LogicalToDeviceUnits(EdgeSize)
            Dim inX = p.X > r.Left AndAlso p.X < r.Right
            Dim inY = p.Y > r.Top AndAlso p.Y < r.Bottom
            If inY AndAlso Math.Abs(p.X - r.Left) <= es Then Return DragPart.Left
            If inY AndAlso Math.Abs(p.X - r.Right) <= es Then Return DragPart.Right
            If inX AndAlso Math.Abs(p.Y - r.Top) <= es Then Return DragPart.Top
            If inX AndAlso Math.Abs(p.Y - r.Bottom) <= es Then Return DragPart.Bottom
        End If
        If r.Contains(p) Then Return DragPart.Move
        Return DragPart.None
    End Function

    Private Shared Function CursorFor(part As DragPart) As Cursor
        Select Case part
            Case DragPart.Move : Return Cursors.SizeAll
            Case DragPart.TopLeft, DragPart.BottomRight : Return Cursors.SizeNWSE
            Case DragPart.TopRight, DragPart.BottomLeft : Return Cursors.SizeNESW
            Case DragPart.Left, DragPart.Right : Return Cursors.SizeWE
            Case DragPart.Top, DragPart.Bottom : Return Cursors.SizeNS
            Case Else : Return Cursors.Default
        End Select
    End Function

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If e.Button <> MouseButtons.Left Then Return
        _drag = HitTest(e.Location)
        _dragStart = ToImage(e.Location)
        _boxStart = _box
        If _drag <> DragPart.None Then Capture = True
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _drag = DragPart.None Then
            Cursor = CursorFor(HitTest(e.Location))
            Return
        End If

        Dim p = ToImage(e.Location)
        If _drag = DragPart.Move Then
            Dim moved = _boxStart
            moved.Offset(p.X - _dragStart.X, p.Y - _dragStart.Y)
            Box = moved
        ElseIf _drag >= DragPart.Left Then
            Box = ResizeEdge(p)
        Else
            Box = ResizeCorner(p)
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        _drag = DragPart.None
        Capture = False
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        If e.Button = MouseButtons.Left AndAlso HitTest(e.Location) = DragPart.Move Then ResetBox()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _image Is Nothing OrElse _box.IsEmpty Then Return
        Dim factor = If(e.Delta > 0, 1 / 1.1F, 1.1F)
        Dim aspect = _box.Width / _box.Height
        Dim max = MaxBoxSize(aspect), min = MinBoxSize(aspect)
        Dim w = Math.Max(min.Width, Math.Min(max.Width, _box.Width * factor))
        Dim h = w / aspect
        Dim cx = _box.X + _box.Width / 2, cy = _box.Y + _box.Height / 2
        Box = New RectangleF(cx - w / 2, cy - h / 2, w, h)
    End Sub

    ''' <summary>拖曳角落：對角固定；比例鎖定時取寬高中較小的一邊，讓框不超出滑鼠與照片。</summary>
    Private Function ResizeCorner(p As PointF) As RectangleF
        Dim b = _boxStart
        Dim left = _drag = DragPart.TopLeft OrElse _drag = DragPart.BottomLeft
        Dim top = _drag = DragPart.TopLeft OrElse _drag = DragPart.TopRight
        Dim ax = If(left, b.Right, b.Left), ay = If(top, b.Bottom, b.Top)
        ' 從固定角往拖曳方向，照片內最多可延伸的距離
        Dim maxW = If(left, ax, _image.Width - ax), maxH = If(top, ay, _image.Height - ay)
        Dim w = Math.Min(maxW, Math.Max(0, If(left, ax - p.X, p.X - ax)))
        Dim h = Math.Min(maxH, Math.Max(0, If(top, ay - p.Y, p.Y - ay)))

        If _aspect > 0 Then
            Dim a = CSng(_aspect)
            If h * a < w Then w = h * a Else h = w / a
            Dim min = MinBoxSize(_aspect)
            If w < min.Width Then
                w = Math.Min(min.Width, Math.Min(maxW, maxH * a))
                h = w / a
            End If
        Else
            ' 自由比例：最小尺寸以照片的 1/MaxScale 為準
            w = Math.Min(maxW, Math.Max(w, _image.Width / CropMath.MaxScale))
            h = Math.Min(maxH, Math.Max(h, _image.Height / CropMath.MaxScale))
        End If
        Return New RectangleF(If(left, ax - w, ax), If(top, ay - h, ay), w, h)
    End Function

    ''' <summary>拖曳單邊（只在自由比例時可用）。</summary>
    Private Function ResizeEdge(p As PointF) As RectangleF
        Dim b = _boxStart
        Dim minW = _image.Width / CropMath.MaxScale, minH = _image.Height / CropMath.MaxScale
        Dim l = b.Left, t = b.Top, r = b.Right, bt = b.Bottom
        Select Case _drag
            Case DragPart.Left : l = Math.Max(0, Math.Min(r - minW, p.X))
            Case DragPart.Right : r = Math.Min(_image.Width, Math.Max(l + minW, p.X))
            Case DragPart.Top : t = Math.Max(0, Math.Min(bt - minH, p.Y))
            Case DragPart.Bottom : bt = Math.Min(_image.Height, Math.Max(t + minH, p.Y))
        End Select
        Return RectangleF.FromLTRB(l, t, r, bt)
    End Function

#End Region

#Region "繪製"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        If _image Is Nothing Then
            If Not String.IsNullOrEmpty(_message) Then
                TextRenderer.DrawText(g, _message, Font, ClientRectangle, Color.Gainsboro,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
            End If
            Return
        End If

        Dim ir = GetImageRect()
        g.InterpolationMode = InterpolationMode.HighQualityBilinear
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        g.DrawImage(_image, ir)

        Dim r = ToScreen(_box)
        ' 裁掉的部分調暗
        Using shade As New SolidBrush(Color.FromArgb(150, 0, 0, 0))
            g.FillRectangle(shade, ir.Left, ir.Top, ir.Width, r.Top - ir.Top)
            g.FillRectangle(shade, ir.Left, r.Bottom, ir.Width, ir.Bottom - r.Bottom)
            g.FillRectangle(shade, ir.Left, r.Top, r.Left - ir.Left, r.Height)
            g.FillRectangle(shade, r.Right, r.Top, ir.Right - r.Right, r.Height)
        End Using

        g.SmoothingMode = SmoothingMode.AntiAlias
        ' 三分線
        Using thirds As New Pen(Color.FromArgb(110, 255, 255, 255), 1)
            For k = 1 To 2
                Dim x = r.Left + r.Width * k / 3, y = r.Top + r.Height * k / 3
                g.DrawLine(thirds, x, r.Top, x, r.Bottom)
                g.DrawLine(thirds, r.Left, y, r.Right, y)
            Next
        End Using
        Using border As New Pen(Color.White, 1.5F)
            g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height)
        End Using

        ' 角落控制點：粗 L 形；自由比例時四邊中央加短線
        Dim len = LogicalToDeviceUnits(16)
        Using handle As New Pen(Color.White, LogicalToDeviceUnits(4)) With {.StartCap = LineCap.Square, .EndCap = LineCap.Square}
            g.DrawLines(handle, {New PointF(r.Left, r.Top + len), New PointF(r.Left, r.Top), New PointF(r.Left + len, r.Top)})
            g.DrawLines(handle, {New PointF(r.Right - len, r.Top), New PointF(r.Right, r.Top), New PointF(r.Right, r.Top + len)})
            g.DrawLines(handle, {New PointF(r.Right, r.Bottom - len), New PointF(r.Right, r.Bottom), New PointF(r.Right - len, r.Bottom)})
            g.DrawLines(handle, {New PointF(r.Left + len, r.Bottom), New PointF(r.Left, r.Bottom), New PointF(r.Left, r.Bottom - len)})
            If _aspect = 0 Then
                Dim cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2, half = len / 2.0F
                g.DrawLine(handle, cx - half, r.Top, cx + half, r.Top)
                g.DrawLine(handle, cx - half, r.Bottom, cx + half, r.Bottom)
                g.DrawLine(handle, r.Left, cy - half, r.Left, cy + half)
                g.DrawLine(handle, r.Right, cy - half, r.Right, cy + half)
            End If
        End Using
    End Sub

#End Region

End Class

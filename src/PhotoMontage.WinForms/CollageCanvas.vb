Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 拼貼預覽與編輯畫布。
''' 拖曳照片到另一格 = 交換；雙擊格子進入取景模式（拖曳平移、滾輪縮放、Esc 或點外面結束）；
''' 滾輪在任何有照片的格子上都能縮放；可從縮圖清單或檔案總管拖放照片進來。
''' </summary>
Friend Class CollageCanvas
    Inherits Control

    ''' <summary>從縮圖清單拖曳照片時使用的資料格式（內容為 PhotoAsset.Id）。</summary>
    Public Const PhotoDragFormat As String = "PhotoMontage.PhotoId"

    Private Const ZoomStep As Single = 1.1F

    Private _project As MontageProject
    Private _selected As Integer = -1
    Private _cropCell As Integer = -1
    Private _pressCell As Integer = -1
    Private _pressPoint As Point
    Private _lastPoint As Point
    Private _swapping As Boolean
    Private _dropTarget As Integer = -1
    Private ReadOnly _menu As New ContextMenuStrip()
    Private ReadOnly _menuCrop As ToolStripItem
    Private ReadOnly _menuResetCrop As ToolStripItem
    Private ReadOnly _menuClear As ToolStripItem

    ''' <summary>格子內容或取景改變。</summary>
    Public Event CellsChanged As EventHandler

    ''' <summary>從檔案總管拖放了檔案或資料夾。</summary>
    Public Event FilesDropped As EventHandler(Of FilesDroppedEventArgs)

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AllowDrop = True
        TabStop = True
        BackColor = Color.FromArgb(48, 48, 48)

        _menuCrop = _menu.Items.Add("調整取景（雙擊）", Nothing, Sub() EnterCropMode(_selected))
        _menuResetCrop = _menu.Items.Add("重設取景", Nothing, Sub() ResetCrop(_selected))
        _menu.Items.Add(New ToolStripSeparator())
        _menuClear = _menu.Items.Add("清空此格", Nothing, Sub() ClearCell(_selected))
        AddHandler _menu.Opening, AddressOf OnMenuOpening
        ContextMenuStrip = _menu
    End Sub

    ''' <summary>要顯示的專案；設定後會重繪。</summary>
    Public Property Project As MontageProject
        Get
            Return _project
        End Get
        Set(value As MontageProject)
            _project = value
            ResetInteraction()
        End Set
    End Property

    ''' <summary>取得照片的預覽影像（已轉正）；回傳 Nothing 表示尚無影像。影像由提供者擁有。</summary>
    Public Property ImageProvider As Func(Of PhotoAsset, Image)

    ''' <summary>是否允許清空格子（自動排版時每張照片都有固定的格子，不允許）。</summary>
    Public Property AllowClear As Boolean = True

    ''' <summary>格子數量改變但版型沒換時呼叫：保留仍有效的選取與取景模式。</summary>
    Public Sub ClampInteraction()
        Dim count = If(_project Is Nothing, 0, Cells.Count)
        If _selected >= count Then _selected = -1
        If _cropCell >= count OrElse Not HasPhoto(_cropCell) Then _cropCell = -1
        If _pressCell >= count Then
            _pressCell = -1
            _swapping = False
        End If
        Invalidate()
    End Sub

    ''' <summary>版型或畫布改變時呼叫，清除選取與取景模式。</summary>
    Public Sub ResetInteraction()
        _selected = -1
        _cropCell = -1
        _pressCell = -1
        _swapping = False
        _dropTarget = -1
        Invalidate()
    End Sub

#Region "版面"

    ''' <summary>畫布在控制項上的位置（依專案比例置中縮放）。</summary>
    Private Function GetCanvasBounds() As RectangleF
        Dim margin = LogicalToDeviceUnits(20)
        Dim area = RectangleF.Inflate(ClientRectangle, -margin, -margin)
        If _project Is Nothing OrElse area.Width <= 0 OrElse area.Height <= 0 Then Return RectangleF.Empty

        Dim aspect = CSng(_project.CanvasAspect)
        Dim w = area.Width, h = area.Width / aspect
        If h > area.Height Then
            h = area.Height
            w = h * aspect
        End If
        Return New RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h)
    End Function

    Private Function GetCellRects() As List(Of RectangleF)
        If _project Is Nothing Then Return New List(Of RectangleF)
        Return CellGeometry.GetCellRects(_project.Collage, GetCanvasBounds())
    End Function

    Private Function HitTest(pt As Point) As Integer
        Dim rects = GetCellRects()
        For i = 0 To rects.Count - 1
            If rects(i).Contains(pt) Then Return i
        Next
        Return -1
    End Function

    Private ReadOnly Property Cells As List(Of Cell)
        Get
            Return _project.Collage.Cells
        End Get
    End Property

    Private Function GetCellImage(index As Integer, ByRef asset As PhotoAsset) As Image
        asset = Nothing
        If _project Is Nothing OrElse index < 0 OrElse index >= Cells.Count Then Return Nothing
        asset = _project.FindPhoto(Cells(index).PhotoId)
        If asset Is Nothing OrElse asset.Status <> PhotoStatus.Ready OrElse ImageProvider Is Nothing Then Return Nothing
        Return ImageProvider(asset)
    End Function

    Private Function HasPhoto(index As Integer) As Boolean
        Return _project IsNot Nothing AndAlso index >= 0 AndAlso index < Cells.Count AndAlso Cells(index).PhotoId IsNot Nothing
    End Function

#End Region

#Region "繪製"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _project Is Nothing Then Return

        Dim bounds = GetCanvasBounds()
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return

        Dim provider = If(ImageProvider, Function(a As PhotoAsset) CType(Nothing, Image))
        CollageRenderer.Render(g, _project, bounds, provider,
                               New RenderOptions With {.HighQuality = False, .EmptyCellColor = Color.FromArgb(70, 70, 70)})

        Dim rects = GetCellRects()
        g.SmoothingMode = SmoothingMode.AntiAlias

        For i = 0 To rects.Count - 1
            If Not HasPhoto(i) Then DrawEmptyHint(g, rects(i))
        Next
        If rects.Count = 0 Then
            TextRenderer.DrawText(g, "加入照片後會自動排版", Font, Rectangle.Round(bounds), Color.Gray,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End If

        If _cropCell >= 0 AndAlso _cropCell < rects.Count Then DrawCropOverlay(g, _cropCell, rects(_cropCell))

        If _swapping AndAlso _pressCell >= 0 AndAlso _pressCell < rects.Count Then
            Using dim_ As New SolidBrush(Color.FromArgb(120, BackColor))
                g.FillRectangle(dim_, rects(_pressCell))
            End Using
        End If

        Dim target = If(_swapping, HitTest(PointToClient(MousePosition)), _dropTarget)
        If target >= 0 AndAlso target < rects.Count AndAlso target <> _pressCell Then
            DrawBorder(g, rects(target), Color.Gold, 3, dashed:=False)
        End If

        If _selected >= 0 AndAlso _selected < rects.Count AndAlso _selected <> _cropCell Then
            DrawBorder(g, rects(_selected), SystemColors.Highlight, 3, dashed:=False)
        End If
    End Sub

    Private Sub DrawEmptyHint(g As Graphics, rect As RectangleF)
        If rect.Width < 40 OrElse rect.Height < 20 Then Return
        TextRenderer.DrawText(g, "拖曳照片到這裡", Font, Rectangle.Round(rect), Color.Gray,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
    End Sub

    ''' <summary>取景模式：在格子外以半透明顯示整張照片，讓使用者看到被裁掉的部分。</summary>
    Private Sub DrawCropOverlay(g As Graphics, index As Integer, rect As RectangleF)
        Dim asset As PhotoAsset = Nothing
        Dim image = GetCellImage(index, asset)
        If image Is Nothing Then Return

        Dim src = CollageRenderer.GetSourceRect(image, Cells(index), rect)
        Dim scale = rect.Width / src.Width
        Dim full As New RectangleF(rect.X - src.X * scale, rect.Y - src.Y * scale, image.Width * scale, image.Height * scale)

        Dim state = g.Save()
        g.SetClip(rect, CombineMode.Exclude)
        Using attrs As New ImageAttributes()
            attrs.SetColorMatrix(New ColorMatrix With {.Matrix33 = 0.35F})
            g.DrawImage(image, Rectangle.Round(full), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs)
        End Using
        g.Restore(state)

        DrawBorder(g, full, Color.FromArgb(160, Color.White), 1, dashed:=True)
        DrawBorder(g, rect, Color.Orange, 3, dashed:=False)
    End Sub

    Private Sub DrawBorder(g As Graphics, rect As RectangleF, color As Color, width As Integer, dashed As Boolean)
        Using pen As New Pen(color, LogicalToDeviceUnits(width))
            If dashed Then pen.DashStyle = DashStyle.Dash
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height)
        End Using
    End Sub

#End Region

#Region "滑鼠與鍵盤"

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If _project Is Nothing Then Return
        Dim index = HitTest(e.Location)

        If e.Button = MouseButtons.Right Then
            If index <> _cropCell Then _cropCell = -1
            _selected = index
            Invalidate()
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return

        If _cropCell >= 0 AndAlso index <> _cropCell Then _cropCell = -1
        _selected = index
        _pressCell = If(HasPhoto(index), index, -1)
        _pressPoint = e.Location
        _lastPoint = e.Location
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _pressCell < 0 OrElse e.Button <> MouseButtons.Left Then Return

        If _pressCell = _cropCell Then
            PanCrop(_pressCell, e.X - _lastPoint.X, e.Y - _lastPoint.Y)
            _lastPoint = e.Location
            Return
        End If

        If Not _swapping Then
            Dim drag = SystemInformation.DragSize
            _swapping = Math.Abs(e.X - _pressPoint.X) > drag.Width OrElse Math.Abs(e.Y - _pressPoint.Y) > drag.Height
            If _swapping Then Cursor = Cursors.SizeAll
        End If
        If _swapping Then Invalidate()
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _swapping Then
            Dim target = HitTest(e.Location)
            If target >= 0 AndAlso target <> _pressCell Then
                PhotoAssignment.Swap(Cells(_pressCell), Cells(target))
                _selected = target
                RaiseEvent CellsChanged(Me, EventArgs.Empty)
            End If
            Cursor = Cursors.Default
        End If
        _swapping = False
        _pressCell = -1
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        If e.Button = MouseButtons.Left Then EnterCropMode(HitTest(e.Location))
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _project Is Nothing Then Return
        Dim index = If(_cropCell >= 0, _cropCell, HitTest(e.Location))
        If Not HasPhoto(index) Then Return

        Dim c = Cells(index)
        c.Crop = CropMath.Zoom(c.Crop, If(e.Delta > 0, ZoomStep, 1 / ZoomStep))
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        If keyData = Keys.Escape OrElse keyData = Keys.Delete Then Return True
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Escape AndAlso _cropCell >= 0 Then
            _cropCell = -1
            Invalidate()
            e.Handled = True
        ElseIf e.KeyCode = Keys.Delete AndAlso AllowClear AndAlso HasPhoto(_selected) Then
            ClearCell(_selected)
            e.Handled = True
        End If
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        If _swapping Then
            _swapping = False
            _pressCell = -1
            Cursor = Cursors.Default
            Invalidate()
        End If
    End Sub

    Private Sub OnMenuOpening(sender As Object, e As System.ComponentModel.CancelEventArgs)
        Dim has = HasPhoto(_selected)
        _menuCrop.Enabled = has
        _menuResetCrop.Enabled = has
        _menuClear.Enabled = has AndAlso AllowClear
        If _selected < 0 Then e.Cancel = True
    End Sub

#End Region

#Region "格子操作"

    Private Sub EnterCropMode(index As Integer)
        If Not HasPhoto(index) Then Return
        _cropCell = index
        _selected = index
        Invalidate()
    End Sub

    Private Sub PanCrop(index As Integer, dx As Integer, dy As Integer)
        Dim asset As PhotoAsset = Nothing
        Dim image = GetCellImage(index, asset)
        If image Is Nothing OrElse (dx = 0 AndAlso dy = 0) Then Return

        Dim rect = GetCellRects()(index)
        Dim c = Cells(index)
        c.Crop = CropMath.Pan(c.Crop, New SizeF(image.Width, image.Height), rect.Size, dx, dy)
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ResetCrop(index As Integer)
        If Not HasPhoto(index) Then Return
        Cells(index).Crop = New CropInfo()
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ClearCell(index As Integer)
        If Not HasPhoto(index) Then Return
        Cells(index).PhotoId = Nothing
        Cells(index).Crop = New CropInfo()
        If _cropCell = index Then _cropCell = -1
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

#End Region

#Region "拖放"

    Protected Overrides Sub OnDragOver(e As DragEventArgs)
        MyBase.OnDragOver(e)
        If e.Data Is Nothing OrElse _project Is Nothing Then Return

        If e.Data.GetDataPresent(PhotoDragFormat) Then
            Dim target = HitTest(PointToClient(New Point(e.X, e.Y)))
            e.Effect = If(target >= 0, DragDropEffects.Move, DragDropEffects.None)
            If target <> _dropTarget Then
                _dropTarget = target
                Invalidate()
            End If
        ElseIf e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        End If
    End Sub

    Protected Overrides Sub OnDragLeave(e As EventArgs)
        MyBase.OnDragLeave(e)
        _dropTarget = -1
        Invalidate()
    End Sub

    Protected Overrides Sub OnDragDrop(e As DragEventArgs)
        MyBase.OnDragDrop(e)
        _dropTarget = -1
        Invalidate()
        If e.Data Is Nothing OrElse _project Is Nothing Then Return

        Dim photoId = TryCast(e.Data.GetData(PhotoDragFormat), String)
        If photoId IsNot Nothing Then
            Dim target = HitTest(PointToClient(New Point(e.X, e.Y)))
            If target < 0 Then Return
            PhotoAssignment.Place(Cells, target, photoId)
            _selected = target
            _cropCell = -1
            RaiseEvent CellsChanged(Me, EventArgs.Empty)
            Return
        End If

        Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing AndAlso files.Length > 0 Then RaiseEvent FilesDropped(Me, New FilesDroppedEventArgs(files))
    End Sub

#End Region

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _menu.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

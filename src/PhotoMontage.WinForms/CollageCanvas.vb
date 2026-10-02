Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 拼貼預覽與編輯畫布。
''' 照片：拖曳到另一格 = 交換；雙擊進入取景模式（拖曳平移、滾輪縮放、Esc 結束）；滾輪直接縮放。
''' 文字：點選後拖曳移動、拖曳上方圓點旋轉（Shift 每 15 度）、Ctrl+滾輪調整大小、雙擊編輯、Delete 刪除。
''' 每次變更前觸發 <see cref="ChangeStarting"/>（供復原記錄），變更後觸發 <see cref="CellsChanged"/> 或 <see cref="TextsChanged"/>。
''' </summary>
Friend Class CollageCanvas
    Inherits Control

    ''' <summary>從縮圖清單拖曳照片時使用的資料格式（內容為 PhotoAsset.Id）。</summary>
    Public Const PhotoDragFormat As String = "PhotoMontage.PhotoId"

    Private Const ZoomStep As Single = 1.1F
    Private Const TextPadding As Single = 4
    Private Const RotateHandleDistance As Single = 22
    Private Const RotateHandleRadius As Single = 6

    Private Enum DragMode
        None
        PendingCell   ' 在有照片的格子按下，尚未判定是交換還是點擊
        SwapCell
        PanCrop
        MoveText
        RotateText
    End Enum

    Private _project As MontageProject
    Private _selected As Integer = -1
    Private _cropCell As Integer = -1
    Private _selectedTextId As String
    Private _drag As DragMode = DragMode.None
    Private _dragCell As Integer = -1
    Private _pressPoint As Point
    Private _lastPoint As Point
    Private _textStartPosition As PointF
    Private _changeRecorded As Boolean
    Private _dropTarget As Integer = -1

    Private ReadOnly _menu As New ContextMenuStrip()
    Private ReadOnly _menuCrop As ToolStripItem
    Private ReadOnly _menuResetCrop As ToolStripItem
    Private ReadOnly _menuSeparator As ToolStripItem
    Private ReadOnly _menuClear As ToolStripItem
    Private ReadOnly _menuDeleteText As ToolStripItem

    ''' <summary>即將變更（格子或文字）。Key 相同的連續變更可合併成一個復原步驟。</summary>
    Public Event ChangeStarting As EventHandler(Of ChangeStartingEventArgs)

    ''' <summary>格子內容或取景改變。</summary>
    Public Event CellsChanged As EventHandler

    ''' <summary>文字圖層的位置、角度、大小改變，或文字被刪除。</summary>
    Public Event TextsChanged As EventHandler

    ''' <summary>選取的文字改變。</summary>
    Public Event TextSelectionChanged As EventHandler

    ''' <summary>雙擊文字，要求編輯內容。</summary>
    Public Event TextEditRequested As EventHandler

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
        _menuSeparator = New ToolStripSeparator()
        _menu.Items.Add(_menuSeparator)
        _menuClear = _menu.Items.Add("清空此格", Nothing, Sub() ClearCell(_selected))
        _menuDeleteText = _menu.Items.Add("刪除文字", Nothing, Sub() DeleteSelectedText())
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

    ''' <summary>取得背景圖的預覽影像；回傳 Nothing 表示沒有背景圖。影像由提供者擁有。</summary>
    Public Property BackgroundImageProvider As Func(Of Image)

    ''' <summary>是否允許清空格子（自動排版時每張照片都有固定的格子，不允許）。</summary>
    Public Property AllowClear As Boolean = True

    ''' <summary>選取的文字圖層 Id；設定時不觸發 <see cref="TextSelectionChanged"/>。</summary>
    Public Property SelectedTextId As String
        Get
            Return _selectedTextId
        End Get
        Set(value As String)
            _selectedTextId = value
            If value IsNot Nothing Then
                _selected = -1
                _cropCell = -1
            End If
            Invalidate()
        End Set
    End Property

    Public ReadOnly Property SelectedText As TextLayer
        Get
            If _project Is Nothing OrElse _selectedTextId Is Nothing Then Return Nothing
            Return _project.Texts.Find(Function(t) t.Id = _selectedTextId)
        End Get
    End Property

    ''' <summary>格子數量改變但版型沒換時呼叫：保留仍有效的選取與取景模式。</summary>
    Public Sub ClampInteraction()
        Dim count = If(_project Is Nothing, 0, Cells.Count)
        If _selected >= count Then _selected = -1
        If _cropCell >= count OrElse Not HasPhoto(_cropCell) Then _cropCell = -1
        If _dragCell >= count Then CancelDrag()
        If _selectedTextId IsNot Nothing AndAlso SelectedText Is Nothing Then _selectedTextId = Nothing
        Invalidate()
    End Sub

    ''' <summary>版型或畫布改變時呼叫，清除格子的選取與取景模式（文字選取保留）。</summary>
    Public Sub ResetInteraction()
        _selected = -1
        _cropCell = -1
        _dropTarget = -1
        CancelDrag()
        If _selectedTextId IsNot Nothing AndAlso SelectedText Is Nothing Then _selectedTextId = Nothing
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

    Private Function HitTestCell(pt As Point) As Integer
        Dim rects = GetCellRects()
        For i = 0 To rects.Count - 1
            If rects(i).Contains(pt) Then Return i
        Next
        Return -1
    End Function

    ''' <summary>最上層（最後加入）且包含該點的文字。</summary>
    Private Function HitTestText(pt As Point) As TextLayer
        If _project Is Nothing Then Return Nothing
        Dim bounds = GetCanvasBounds()
        For i = _project.Texts.Count - 1 To 0 Step -1
            Dim layer = _project.Texts(i)
            If TextLayerRenderer.Measure(layer, bounds).Contains(pt, LogicalToDeviceUnits(CInt(TextPadding))) Then Return layer
        Next
        Return Nothing
    End Function

    Private Function HitTestRotateHandle(pt As Point) As Boolean
        Dim layer = SelectedText
        If layer Is Nothing Then Return False
        Dim handle = GetRotateHandle(layer)
        Dim dx = pt.X - handle.X, dy = pt.Y - handle.Y
        Dim r = LogicalToDeviceUnits(CInt(RotateHandleRadius)) + 3
        Return dx * dx + dy * dy <= r * r
    End Function

    Private Function GetRotateHandle(layer As TextLayer) As PointF
        Return TextLayerRenderer.Measure(layer, GetCanvasBounds()).GetRotateHandle(
            LogicalToDeviceUnits(CInt(RotateHandleDistance)), LogicalToDeviceUnits(CInt(TextPadding)))
    End Function

    Private ReadOnly Property Cells As List(Of Cell)
        Get
            Return _project.Collage.Cells
        End Get
    End Property

    Private Function GetCellImage(index As Integer) As Image
        If _project Is Nothing OrElse index < 0 OrElse index >= Cells.Count Then Return Nothing
        Dim asset = _project.FindPhoto(Cells(index).PhotoId)
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
        CollageRenderer.Render(g, _project, bounds, provider, New RenderOptions With {
            .HighQuality = False,
            .EmptyCellColor = Color.FromArgb(70, 70, 70),
            .BackgroundImage = BackgroundImageProvider?.Invoke()})

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

        If _drag = DragMode.SwapCell AndAlso _dragCell >= 0 AndAlso _dragCell < rects.Count Then
            Using dim_ As New SolidBrush(Color.FromArgb(120, BackColor))
                g.FillRectangle(dim_, rects(_dragCell))
            End Using
        End If

        Dim target = If(_drag = DragMode.SwapCell, HitTestCell(PointToClient(MousePosition)), _dropTarget)
        If target >= 0 AndAlso target < rects.Count AndAlso target <> _dragCell Then
            DrawBorder(g, rects(target), Color.Gold, 3, dashed:=False)
        End If

        If _selected >= 0 AndAlso _selected < rects.Count AndAlso _selected <> _cropCell Then
            DrawBorder(g, rects(_selected), SystemColors.Highlight, 3, dashed:=False)
        End If

        Dim text = SelectedText
        If text IsNot Nothing Then DrawTextSelection(g, text, bounds)
    End Sub

    Private Sub DrawEmptyHint(g As Graphics, rect As RectangleF)
        If rect.Width < 40 OrElse rect.Height < 20 Then Return
        TextRenderer.DrawText(g, "拖曳照片到這裡", Font, Rectangle.Round(rect), Color.Gray,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
    End Sub

    ''' <summary>取景模式：在格子外以半透明顯示整張照片，讓使用者看到被裁掉的部分。</summary>
    Private Sub DrawCropOverlay(g As Graphics, index As Integer, rect As RectangleF)
        Dim image = GetCellImage(index)
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

    Private Sub DrawTextSelection(g As Graphics, layer As TextLayer, bounds As RectangleF)
        Dim frame = TextLayerRenderer.Measure(layer, bounds)
        Dim pad = LogicalToDeviceUnits(CInt(TextPadding))
        Dim corners = frame.GetCorners(pad)
        Dim handle = GetRotateHandle(layer)
        Dim topCenter As New PointF((corners(0).X + corners(1).X) / 2, (corners(0).Y + corners(1).Y) / 2)
        Dim r = LogicalToDeviceUnits(CInt(RotateHandleRadius))

        Using outline As New Pen(Color.FromArgb(160, Color.Black), LogicalToDeviceUnits(3)),
              pen As New Pen(SystemColors.Highlight, LogicalToDeviceUnits(1)) With {.DashStyle = DashStyle.Dash}
            g.DrawPolygon(outline, corners)
            g.DrawPolygon(pen, corners)
            pen.DashStyle = DashStyle.Solid
            g.DrawLine(pen, topCenter, handle)
        End Using
        Using fill As New SolidBrush(Color.White), border As New Pen(SystemColors.Highlight, LogicalToDeviceUnits(2))
            g.FillEllipse(fill, handle.X - r, handle.Y - r, r * 2, r * 2)
            g.DrawEllipse(border, handle.X - r, handle.Y - r, r * 2, r * 2)
        End Using
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
        _pressPoint = e.Location
        _lastPoint = e.Location
        _changeRecorded = False

        ' 文字在照片上層，優先判定
        If e.Button = MouseButtons.Left AndAlso HitTestRotateHandle(e.Location) Then
            _drag = DragMode.RotateText
            Return
        End If

        Dim text = HitTestText(e.Location)
        If text IsNot Nothing Then
            SelectText(text.Id)
            If e.Button = MouseButtons.Left Then
                _drag = DragMode.MoveText
                _textStartPosition = text.Position
            End If
            Return
        End If
        SelectText(Nothing)

        Dim index = HitTestCell(e.Location)
        If e.Button = MouseButtons.Right Then
            If index <> _cropCell Then _cropCell = -1
            _selected = index
            Invalidate()
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return

        If _cropCell >= 0 AndAlso index <> _cropCell Then _cropCell = -1
        _selected = index
        If HasPhoto(index) Then
            _dragCell = index
            _drag = If(index = _cropCell, DragMode.PanCrop, DragMode.PendingCell)
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _project Is Nothing Then Return

        If e.Button <> MouseButtons.Left OrElse _drag = DragMode.None Then
            UpdateHoverCursor(e.Location)
            Return
        End If

        Select Case _drag
            Case DragMode.PanCrop
                PanCrop(_dragCell, e.X - _lastPoint.X, e.Y - _lastPoint.Y)
            Case DragMode.PendingCell
                Dim size = SystemInformation.DragSize
                If Math.Abs(e.X - _pressPoint.X) > size.Width OrElse Math.Abs(e.Y - _pressPoint.Y) > size.Height Then
                    _drag = DragMode.SwapCell
                    Cursor = Cursors.SizeAll
                    Invalidate()
                End If
            Case DragMode.SwapCell
                Invalidate()
            Case DragMode.MoveText
                MoveSelectedText(e.Location)
            Case DragMode.RotateText
                RotateSelectedText(e.Location, (ModifierKeys And Keys.Shift) = Keys.Shift)
        End Select
        _lastPoint = e.Location
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _drag = DragMode.SwapCell Then
            Dim target = HitTestCell(e.Location)
            If target >= 0 AndAlso target <> _dragCell Then
                RaiseChangeStarting(Nothing)
                PhotoAssignment.Swap(Cells(_dragCell), Cells(target))
                _selected = target
                RaiseEvent CellsChanged(Me, EventArgs.Empty)
            End If
        End If
        CancelDrag()
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        If e.Button <> MouseButtons.Left Then Return
        Dim text = HitTestText(e.Location)
        If text IsNot Nothing Then
            SelectText(text.Id)
            RaiseEvent TextEditRequested(Me, EventArgs.Empty)
        Else
            EnterCropMode(HitTestCell(e.Location))
        End If
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If _project Is Nothing Then Return
        Dim factor = If(e.Delta > 0, ZoomStep, 1 / ZoomStep)

        If (ModifierKeys And Keys.Control) = Keys.Control Then
            Dim text = If(SelectedText, HitTestText(e.Location))
            If text Is Nothing Then Return
            RaiseChangeStarting("text-size:" & text.Id)
            text.FontSize = Math.Max(0.01F, Math.Min(0.5F, text.FontSize * factor))
            SelectText(text.Id)
            Invalidate()
            RaiseEvent TextsChanged(Me, EventArgs.Empty)
            Return
        End If

        Dim index = If(_cropCell >= 0, _cropCell, HitTestCell(e.Location))
        If Not HasPhoto(index) Then Return
        RaiseChangeStarting("zoom:" & index)
        Dim c = Cells(index)
        c.Crop = CropMath.Zoom(c.Crop, factor)
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        If keyData = Keys.Escape OrElse keyData = Keys.Delete Then Return True
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Escape Then
            If _cropCell >= 0 Then
                _cropCell = -1
            ElseIf _selectedTextId IsNot Nothing Then
                SelectText(Nothing)
            End If
            Invalidate()
            e.Handled = True
        ElseIf e.KeyCode = Keys.Delete Then
            If SelectedText IsNot Nothing Then
                DeleteSelectedText()
            ElseIf AllowClear AndAlso HasPhoto(_selected) Then
                ClearCell(_selected)
            End If
            e.Handled = True
        End If
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        If _drag <> DragMode.None Then
            CancelDrag()
            Invalidate()
        End If
    End Sub

    Private Sub UpdateHoverCursor(pt As Point)
        Dim cursor As Cursor = Cursors.Default
        If HitTestRotateHandle(pt) Then
            cursor = Cursors.Hand
        ElseIf HitTestText(pt) IsNot Nothing Then
            cursor = Cursors.SizeAll
        End If
        If Me.Cursor IsNot cursor Then Me.Cursor = cursor
    End Sub

    Private Sub CancelDrag()
        _drag = DragMode.None
        _dragCell = -1
        Cursor = Cursors.Default
    End Sub

    Private Sub OnMenuOpening(sender As Object, e As System.ComponentModel.CancelEventArgs)
        Dim textSelected = SelectedText IsNot Nothing
        Dim has = HasPhoto(_selected)
        _menuCrop.Visible = Not textSelected
        _menuResetCrop.Visible = Not textSelected
        _menuSeparator.Visible = Not textSelected
        _menuClear.Visible = Not textSelected
        _menuDeleteText.Visible = textSelected
        _menuCrop.Enabled = has
        _menuResetCrop.Enabled = has
        _menuClear.Enabled = has AndAlso AllowClear
        If Not textSelected AndAlso _selected < 0 Then e.Cancel = True
    End Sub

    ''' <summary>每次拖曳只在第一次真正改變時觸發一次 <see cref="ChangeStarting"/>。</summary>
    Private Sub RecordDragChangeOnce()
        If _changeRecorded Then Return
        _changeRecorded = True
        RaiseChangeStarting(Nothing)
    End Sub

    Private Sub RaiseChangeStarting(key As String)
        RaiseEvent ChangeStarting(Me, New ChangeStartingEventArgs(key))
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
        Dim image = GetCellImage(index)
        If image Is Nothing OrElse (dx = 0 AndAlso dy = 0) Then Return

        RecordDragChangeOnce()
        Dim rect = GetCellRects()(index)
        Dim c = Cells(index)
        c.Crop = CropMath.Pan(c.Crop, New SizeF(image.Width, image.Height), rect.Size, dx, dy)
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ResetCrop(index As Integer)
        If Not HasPhoto(index) Then Return
        RaiseChangeStarting(Nothing)
        Cells(index).Crop = New CropInfo()
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ClearCell(index As Integer)
        If Not HasPhoto(index) Then Return
        RaiseChangeStarting(Nothing)
        Cells(index).PhotoId = Nothing
        Cells(index).Crop = New CropInfo()
        If _cropCell = index Then _cropCell = -1
        Invalidate()
        RaiseEvent CellsChanged(Me, EventArgs.Empty)
    End Sub

#End Region

#Region "文字操作"

    Private Sub SelectText(id As String)
        If _selectedTextId = id Then Return
        _selectedTextId = id
        If id IsNot Nothing Then
            _selected = -1
            _cropCell = -1
        End If
        Invalidate()
        RaiseEvent TextSelectionChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub MoveSelectedText(pt As Point)
        Dim layer = SelectedText
        Dim bounds = GetCanvasBounds()
        If layer Is Nothing OrElse bounds.Width <= 0 OrElse pt = _pressPoint Then Return

        RecordDragChangeOnce()
        layer.Position = New PointF(
            Math.Max(0F, Math.Min(1.0F, _textStartPosition.X + (pt.X - _pressPoint.X) / bounds.Width)),
            Math.Max(0F, Math.Min(1.0F, _textStartPosition.Y + (pt.Y - _pressPoint.Y) / bounds.Height)))
        Invalidate()
        RaiseEvent TextsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub RotateSelectedText(pt As Point, snap As Boolean)
        Dim layer = SelectedText
        If layer Is Nothing Then Return

        Dim center = TextLayerRenderer.GetCenter(layer, GetCanvasBounds())
        Dim angle = TextFrame.NormalizeAngle(TextFrame.AngleFromCenter(center, pt), If(snap, 15.0F, 0F))
        If angle = layer.Rotation Then Return
        RecordDragChangeOnce()
        layer.Rotation = angle
        Invalidate()
        RaiseEvent TextsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub DeleteSelectedText()
        Dim layer = SelectedText
        If layer Is Nothing Then Return
        RaiseChangeStarting(Nothing)
        _project.Texts.Remove(layer)
        SelectText(Nothing)
        RaiseEvent TextsChanged(Me, EventArgs.Empty)
    End Sub

#End Region

#Region "拖放"

    Protected Overrides Sub OnDragOver(e As DragEventArgs)
        MyBase.OnDragOver(e)
        If e.Data Is Nothing OrElse _project Is Nothing Then Return

        If e.Data.GetDataPresent(PhotoDragFormat) Then
            Dim target = HitTestCell(PointToClient(New Point(e.X, e.Y)))
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
            Dim target = HitTestCell(PointToClient(New Point(e.X, e.Y)))
            If target < 0 Then Return
            RaiseChangeStarting(Nothing)
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

Friend Class ChangeStartingEventArgs
    Inherits EventArgs

    ''' <summary>相同 Key 的連續變更可合併成一個復原步驟；Nothing 表示不合併。</summary>
    Public ReadOnly Property Key As String

    Public Sub New(key As String)
        Me.Key = key
    End Sub
End Class

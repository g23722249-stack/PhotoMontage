Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 拼貼預覽與編輯畫布。
''' 照片：拖曳到另一格 = 交換；雙擊進入取景模式（拖曳平移、滾輪縮放、Esc 結束）；滾輪直接縮放。
''' 文字：點選後拖曳移動、拖曳上方圓點旋轉（Shift 每 15 度）、Ctrl+滾輪調整大小、雙擊編輯、Delete 刪除。
''' 馬賽克模式：顯示預覽影像；點選格子、滑鼠停留顯示素材檔名、右鍵更換素材。
''' 自由拼貼模式：點選／Ctrl 多選／框選照片；拖曳移動（吸附對齊，Alt 暫停）、拖曳四角縮放、拖曳上方圓點旋轉；
''' 浮動工具列調整圖層、複製、移除；方向鍵微調、Delete 移除、Ctrl+A 全選、滾輪縮放。
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
        FreeMove
        FreeResize
        FreeRotate
        FreeMarquee
    End Enum

    Private Const SnapThreshold As Integer = 6
    Private Const HandleRadius As Integer = 7
    Private Shared ReadOnly GuideColor As Color = Color.FromArgb(214, 51, 127)
    Private Shared ReadOnly ToolbarCommands As FreeCommand() = {
        FreeCommand.BringToFront, FreeCommand.BringForward, FreeCommand.SendBackward, FreeCommand.SendToBack,
        FreeCommand.Duplicate, FreeCommand.Remove}

    ' 自由拼貼
    Private ReadOnly _freeSelection As New List(Of String)
    Private ReadOnly _freeStartCenters As New Dictionary(Of String, PointF)
    Private _freeActiveId As String
    Private _freeStartWidth As Single
    Private _freeMarquee As RectangleF = RectangleF.Empty
    Private _freeMarqueeBase As New List(Of String)
    Private _snap As SnapResult = SnapResult.None
    Private _hoverToolbar As Integer = -1

    ''' <summary>自由拼貼的選取改變。</summary>
    Public Event FreeSelectionChanged As EventHandler

    ''' <summary>自由拼貼的照片被移動、縮放、旋轉、調整圖層、複製或移除。</summary>
    Public Event FreeItemsChanged As EventHandler

    ''' <summary>從縮圖清單拖了一張照片到自由拼貼畫布上。</summary>
    Public Event FreePhotoDropped As EventHandler(Of FreePhotoDroppedEventArgs)

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
    Private ReadOnly _menuMosaicNext As ToolStripItem
    Private ReadOnly _menuMosaicSelected As ToolStripItem
    Private ReadOnly _toolTip As New HelpToolTip(Me)
    Private _hoverMosaicCell As Integer = -1

    ''' <summary>馬賽克格子的右鍵命令。</summary>
    Public Event MosaicCellCommand As EventHandler(Of MosaicCellCommandEventArgs)

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
        _menuMosaicNext = _menu.Items.Add("換成下一個相近的素材", Nothing,
            Sub() RaiseEvent MosaicCellCommand(Me, New MosaicCellCommandEventArgs(_selected, MosaicCellCommandKind.NextAlternative)))
        _menuMosaicSelected = _menu.Items.Add("換成左側選取的照片", Nothing,
            Sub() RaiseEvent MosaicCellCommand(Me, New MosaicCellCommandEventArgs(_selected, MosaicCellCommandKind.UseSelectedPhoto)))
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

    ''' <summary>馬賽克預覽影像（不含文字）；Nothing 表示尚未產生。影像由提供者擁有。</summary>
    Public Property MosaicPreview As Image

    ''' <summary>馬賽克模式下的訊息（例如「按產生馬賽克」）。</summary>
    Public Property MosaicPlaceholder As String

    Private ReadOnly Property IsMosaic As Boolean
        Get
            Return _project IsNot Nothing AndAlso _project.Mode = MontageMode.Mosaic
        End Get
    End Property

    Private ReadOnly Property IsFree As Boolean
        Get
            Return _project IsNot Nothing AndAlso _project.Mode = MontageMode.Free
        End Get
    End Property

    ''' <summary>自由拼貼中選取的照片（依圖層由下往上）。</summary>
    Public ReadOnly Property SelectedFreeItems As IReadOnlyList(Of FreeItem)
        Get
            If _project Is Nothing Then Return New List(Of FreeItem)
            Return _project.Free.Items.Where(Function(i) _freeSelection.Contains(i.Id)).ToList()
        End Get
    End Property

    ''' <summary>設定自由拼貼的選取，並觸發 <see cref="FreeSelectionChanged"/>。</summary>
    Public Sub SelectFreeItems(ids As IEnumerable(Of String))
        SetFreeSelection(ids)
    End Sub

    ''' <summary>選取的馬賽克格子；沒有時為 -1。</summary>
    Public ReadOnly Property SelectedMosaicCell As Integer
        Get
            Return If(IsMosaic, _selected, -1)
        End Get
    End Property

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
        If _project IsNot Nothing AndAlso _freeSelection.RemoveAll(Function(id) _project.Free.Find(id) Is Nothing) > 0 Then
            RaiseEvent FreeSelectionChanged(Me, EventArgs.Empty)
        End If
        Invalidate()
    End Sub

    ''' <summary>版型或畫布改變時呼叫，清除格子的選取與取景模式（文字選取保留）。</summary>
    Public Sub ResetInteraction()
        _selected = -1
        _cropCell = -1
        _dropTarget = -1
        CancelDrag()
        If _selectedTextId IsNot Nothing AndAlso SelectedText Is Nothing Then _selectedTextId = Nothing
        If _freeSelection.Count > 0 Then
            _freeSelection.Clear()
            RaiseEvent FreeSelectionChanged(Me, EventArgs.Empty)
        End If
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
        If _project Is Nothing OrElse IsMosaic OrElse IsFree Then Return New List(Of RectangleF)
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
        Return _project IsNot Nothing AndAlso Not IsMosaic AndAlso Not IsFree AndAlso index >= 0 AndAlso index < Cells.Count AndAlso Cells(index).PhotoId IsNot Nothing
    End Function

#End Region

#Region "繪製"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _project Is Nothing Then Return

        Dim bounds = GetCanvasBounds()
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return

        If IsMosaic Then
            PaintMosaic(g, bounds)
            Return
        End If
        If IsFree Then
            PaintFree(g, bounds)
            Return
        End If

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

    Private Sub PaintMosaic(g As Graphics, bounds As RectangleF)
        If MosaicPreview IsNot Nothing Then
            g.InterpolationMode = InterpolationMode.HighQualityBilinear
            g.PixelOffsetMode = PixelOffsetMode.Half
            g.DrawImage(MosaicPreview, bounds)
        Else
            Using back As New SolidBrush(Color.FromArgb(70, 70, 70))
                g.FillRectangle(back, bounds)
            End Using
            TextRenderer.DrawText(g, If(MosaicPlaceholder, ""), Font, Rectangle.Round(bounds), Color.Silver,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End If

        Dim state = g.Save()
        g.SetClip(bounds)
        For Each layer In _project.Texts
            TextLayerRenderer.Draw(g, layer, bounds)
        Next
        g.Restore(state)

        g.SmoothingMode = SmoothingMode.AntiAlias
        If _selected >= 0 AndAlso _project.Mosaic.IsGenerated AndAlso _selected < _project.Mosaic.CellCount Then
            Dim rect = MosaicRenderer.GetCellRect(_project.Mosaic, bounds, _selected)
            Using outline As New Pen(Color.Black, LogicalToDeviceUnits(3)), pen As New Pen(Color.Gold, LogicalToDeviceUnits(2))
                g.DrawRectangle(outline, rect)
                g.DrawRectangle(pen, rect)
            End Using
        End If
        Dim text = SelectedText
        If text IsNot Nothing Then DrawTextSelection(g, text, bounds)
    End Sub

    Private Function HitTestMosaic(pt As Point) As Integer
        If Not IsMosaic OrElse Not _project.Mosaic.IsGenerated Then Return -1
        Return MosaicRenderer.HitTest(_project.Mosaic, GetCanvasBounds(), pt)
    End Function

    Private Sub UpdateMosaicToolTip(pt As Point)
        Dim cell = HitTestMosaic(pt)
        If cell = _hoverMosaicCell Then Return
        _hoverMosaicCell = cell
        Dim asset = If(cell >= 0, _project.FindPhoto(_project.Mosaic.Tiles(cell)), Nothing)
        _toolTip.ShowFor(Me, asset?.FileName, If(asset Is Nothing, Nothing, "右鍵可換成相近的素材或左側選取的照片。"))
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

        ' 自由拼貼的工具列與控制點在最上層
        If IsFree AndAlso e.Button = MouseButtons.Left AndAlso TryFreeHandleMouseDown(e.Location) Then Return

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

        If IsFree Then
            FreeMouseDown(e)
            Return
        End If

        If IsMosaic Then
            _selected = HitTestMosaic(e.Location)
            Invalidate()
            Return
        End If

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
            If IsMosaic Then UpdateMosaicToolTip(e.Location)
            If IsFree Then UpdateToolbarHover(e.Location)
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
            Case DragMode.FreeMove
                FreeMove(e.Location)
            Case DragMode.FreeResize
                FreeResize(e.Location)
            Case DragMode.FreeRotate
                FreeRotate(e.Location, (ModifierKeys And Keys.Shift) = Keys.Shift)
            Case DragMode.FreeMarquee
                FreeMarquee(e.Location)
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

        If IsMosaic Then Return
        If IsFree Then
            Dim target = FreeGeometry.HitTest(_project.Free.Items, GetCanvasBounds(), e.Location)
            If target Is Nothing AndAlso _freeSelection.Count = 1 Then target = _project.Free.Find(_freeSelection(0))
            If target Is Nothing Then Return
            RaiseChangeStarting("free-size:" & target.Id)
            target.Width = Math.Max(FreeLayoutSettings.MinItemWidth, Math.Min(FreeLayoutSettings.MaxItemWidth, target.Width * factor))
            Invalidate()
            RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
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
        If IsFree AndAlso (keyData And Keys.KeyCode) >= Keys.Left AndAlso (keyData And Keys.KeyCode) <= Keys.Down Then Return True
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If IsFree AndAlso SelectedText Is Nothing AndAlso FreeKeyDown(e) Then
            e.Handled = True
            Return
        End If
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
        If IsFree AndAlso GetFreeHoverCursor(pt, cursor) Then
            If Me.Cursor IsNot cursor Then Me.Cursor = cursor
            Return
        End If
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
        _snap = SnapResult.None
        _freeMarquee = RectangleF.Empty
        Cursor = Cursors.Default
    End Sub

    Private Sub OnMenuOpening(sender As Object, e As System.ComponentModel.CancelEventArgs)
        Dim textSelected = SelectedText IsNot Nothing
        Dim has = HasPhoto(_selected)
        If IsFree AndAlso Not textSelected Then
            e.Cancel = True
            Return
        End If
        Dim mosaicCell = Not textSelected AndAlso IsMosaic AndAlso _selected >= 0
        _menuCrop.Visible = Not textSelected AndAlso Not IsMosaic
        _menuResetCrop.Visible = Not textSelected AndAlso Not IsMosaic
        _menuSeparator.Visible = Not textSelected AndAlso Not IsMosaic
        _menuClear.Visible = Not textSelected AndAlso Not IsMosaic
        _menuDeleteText.Visible = textSelected
        _menuMosaicNext.Visible = mosaicCell
        _menuMosaicSelected.Visible = mosaicCell
        If mosaicCell Then Return
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
            If _freeSelection.Count > 0 Then
                _freeSelection.Clear()
                RaiseEvent FreeSelectionChanged(Me, EventArgs.Empty)
            End If
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

#Region "自由拼貼"

    Private Sub SetFreeSelection(ids As IEnumerable(Of String))
        Dim list = ids.Distinct().ToList()
        If list.SequenceEqual(_freeSelection) Then Return
        _freeSelection.Clear()
        _freeSelection.AddRange(list)
        If list.Count > 0 AndAlso _selectedTextId IsNot Nothing Then
            _selectedTextId = Nothing
            RaiseEvent TextSelectionChanged(Me, EventArgs.Empty)
        End If
        Invalidate()
        RaiseEvent FreeSelectionChanged(Me, EventArgs.Empty)
    End Sub

    Private Function Dev(logical As Integer) As Integer
        Return LogicalToDeviceUnits(logical)
    End Function

    ''' <summary>選取照片的整體包圍框（畫面座標）；沒有選取時為 Empty。</summary>
    Private Function GetSelectionBounds() As RectangleF
        Dim bounds = GetCanvasBounds()
        Return FreeGeometry.Union(SelectedFreeItems.Select(Function(i) FreeGeometry.GetAxisAlignedBounds(FreeGeometry.GetFrame(i, bounds))))
    End Function

    Private Function SingleSelected() As FreeItem
        If _freeSelection.Count <> 1 OrElse _project Is Nothing Then Return Nothing
        Return _project.Free.Find(_freeSelection(0))
    End Function

    ''' <summary>浮動工具列的位置：選取範圍上方（空間不夠時放在下方）；拖曳中或沒有選取時為 Empty。</summary>
    Private Function GetToolbarRect() As Rectangle
        If _freeSelection.Count = 0 OrElse _drag <> DragMode.None Then Return Rectangle.Empty
        Dim sel = GetSelectionBounds()
        If sel.IsEmpty Then Return Rectangle.Empty
        Dim bw = Dev(32), bh = Dev(28), pad = Dev(4), sepW = Dev(9)
        Dim w = ToolbarCommands.Length * bw + pad * 2 + sepW
        Dim h = bh + pad * 2
        Dim above = If(SingleSelected() IsNot Nothing, Dev(40), Dev(12))   ' 留給旋轉控制點
        Dim x = CInt(sel.Left + sel.Width / 2 - w / 2)
        Dim y = CInt(sel.Top) - h - above
        If y < 2 Then y = CInt(sel.Bottom) + Dev(12)
        x = Math.Max(2, Math.Min(ClientSize.Width - w - 2, x))
        y = Math.Max(2, Math.Min(ClientSize.Height - h - 2, y))
        Return New Rectangle(x, y, w, h)
    End Function

    Private Function GetToolbarButtonRect(toolbar As Rectangle, index As Integer) As Rectangle
        Dim bw = Dev(32), bh = Dev(28), pad = Dev(4), sepW = Dev(9)
        Dim x = toolbar.X + pad + index * bw + If(index >= 4, sepW, 0)
        Return New Rectangle(x, toolbar.Y + pad, bw, bh)
    End Function

    Private Function HitTestToolbar(pt As Point) As Integer
        Dim toolbar = GetToolbarRect()
        If toolbar.IsEmpty OrElse Not toolbar.Contains(pt) Then Return -1
        For i = 0 To ToolbarCommands.Length - 1
            If GetToolbarButtonRect(toolbar, i).Contains(pt) Then Return i
        Next
        Return -1
    End Function

    Private Function GetFreeRotateHandle(item As FreeItem) As PointF
        Return FreeGeometry.GetFrame(item, GetCanvasBounds()).GetRotateHandle(Dev(22), Dev(3))
    End Function

    Private Function IsNear(a As PointF, b As Point, radius As Integer) As Boolean
        Dim dx = a.X - b.X, dy = a.Y - b.Y
        Return dx * dx + dy * dy <= radius * radius
    End Function

    ''' <summary>工具列、旋轉控制點、角落控制點。處理了就回傳 True。</summary>
    Private Function TryFreeHandleMouseDown(pt As Point) As Boolean
        Dim tool = HitTestToolbar(pt)
        If tool >= 0 Then
            ExecuteFreeCommand(ToolbarCommands(tool))
            Return True
        End If

        Dim item = SingleSelected()
        If item Is Nothing Then Return False
        Dim frame = FreeGeometry.GetFrame(item, GetCanvasBounds())
        If IsNear(GetFreeRotateHandle(item), pt, Dev(HandleRadius) + 3) Then
            _drag = DragMode.FreeRotate
            _freeActiveId = item.Id
            Return True
        End If
        If FreeGeometry.HitTestCorner(frame, pt, Dev(HandleRadius) + 2, Dev(3)) >= 0 Then
            _drag = DragMode.FreeResize
            _freeActiveId = item.Id
            _freeStartWidth = item.Width
            Return True
        End If
        Return False
    End Function

    Private Sub FreeMouseDown(e As MouseEventArgs)
        Dim bounds = GetCanvasBounds()
        Dim hit = FreeGeometry.HitTest(_project.Free.Items, bounds, e.Location)
        Dim ctrl = (ModifierKeys And Keys.Control) = Keys.Control

        If hit IsNot Nothing Then
            If ctrl Then
                Dim ids = _freeSelection.ToList()
                If Not ids.Remove(hit.Id) Then ids.Add(hit.Id)
                SetFreeSelection(ids)
            ElseIf Not _freeSelection.Contains(hit.Id) Then
                SetFreeSelection({hit.Id})
            End If
            If e.Button = MouseButtons.Left AndAlso _freeSelection.Contains(hit.Id) Then
                _drag = DragMode.FreeMove
                _freeStartCenters.Clear()
                For Each item In SelectedFreeItems
                    _freeStartCenters(item.Id) = New PointF(item.CenterX, item.CenterY)
                Next
            End If
        Else
            If Not ctrl Then SetFreeSelection(Array.Empty(Of String)())
            If e.Button = MouseButtons.Left Then
                _drag = DragMode.FreeMarquee
                _freeMarqueeBase = _freeSelection.ToList()
                _freeMarquee = RectangleF.Empty
            End If
        End If
        Invalidate()
    End Sub

    Private Sub FreeMove(pt As Point)
        Dim bounds = GetCanvasBounds()
        Dim dx As Single = pt.X - _pressPoint.X, dy As Single = pt.Y - _pressPoint.Y
        If Not _changeRecorded AndAlso Math.Abs(dx) + Math.Abs(dy) < 2 Then Return

        ' 以起始位置＋位移計算整體包圍框，吸附到其他照片與畫布
        Dim selected = SelectedFreeItems
        Dim startRects = selected.Select(Function(i)
                                             Dim c = _freeStartCenters(i.Id)
                                             Dim f As New TextFrame(New PointF(bounds.X + c.X * bounds.Width, bounds.Y + c.Y * bounds.Height),
                                                                    FreeGeometry.GetOuterSize(i, bounds.Size), i.Rotation)
                                             Return FreeGeometry.GetAxisAlignedBounds(f)
                                         End Function)
        Dim moving = FreeGeometry.Union(startRects)
        moving.Offset(dx, dy)
        If (ModifierKeys And Keys.Alt) = Keys.Alt Then
            _snap = SnapResult.None
        Else
            Dim others = _project.Free.Items.Where(Function(i) Not _freeSelection.Contains(i.Id)).
                Select(Function(i) FreeGeometry.GetAxisAlignedBounds(FreeGeometry.GetFrame(i, bounds)))
            _snap = FreeSnapping.Snap(moving, others, bounds, Dev(SnapThreshold))
            dx += _snap.Offset.X
            dy += _snap.Offset.Y
        End If

        RecordDragChangeOnce()
        For Each item In selected
            Dim c = _freeStartCenters(item.Id)
            item.CenterX = c.X + dx / bounds.Width
            item.CenterY = c.Y + dy / bounds.Height
        Next
        Invalidate()
        RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub FreeResize(pt As Point)
        Dim item = _project.Free.Find(_freeActiveId)
        If item Is Nothing Then Return
        Dim frame = FreeGeometry.GetFrame(item, GetCanvasBounds())
        Dim width = FreeGeometry.ScaleWidth(_freeStartWidth, frame.Center, _pressPoint, pt)
        If width = item.Width Then Return
        RecordDragChangeOnce()
        item.Width = width
        Invalidate()
        RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub FreeRotate(pt As Point, snap As Boolean)
        Dim item = _project.Free.Find(_freeActiveId)
        If item Is Nothing Then Return
        Dim center = FreeGeometry.GetFrame(item, GetCanvasBounds()).Center
        Dim angle = TextFrame.NormalizeAngle(TextFrame.AngleFromCenter(center, pt), If(snap, 15.0F, 0F))
        If angle = item.Rotation Then Return
        RecordDragChangeOnce()
        item.Rotation = angle
        Invalidate()
        RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub FreeMarquee(pt As Point)
        _freeMarquee = RectangleF.FromLTRB(Math.Min(_pressPoint.X, pt.X), Math.Min(_pressPoint.Y, pt.Y),
                                           Math.Max(_pressPoint.X, pt.X), Math.Max(_pressPoint.Y, pt.Y))
        Dim bounds = GetCanvasBounds()
        Dim hits = _project.Free.Items.
            Where(Function(i) FreeGeometry.GetAxisAlignedBounds(FreeGeometry.GetFrame(i, bounds)).IntersectsWith(_freeMarquee)).
            Select(Function(i) i.Id)
        SetFreeSelection(_freeMarqueeBase.Concat(hits))
        Invalidate()
    End Sub

    ''' <summary>浮動工具列與面板共用的命令。</summary>
    Public Sub ExecuteFreeCommand(command As FreeCommand)
        If _project Is Nothing OrElse _freeSelection.Count = 0 Then Return
        Dim items = _project.Free.Items
        Dim sel = New HashSet(Of String)(_freeSelection)
        Dim moving = items.Where(Function(i) sel.Contains(i.Id)).ToList()
        RaiseChangeStarting(Nothing)

        Select Case command
            Case FreeCommand.BringToFront
                items.RemoveAll(Function(i) sel.Contains(i.Id))
                items.AddRange(moving)
            Case FreeCommand.SendToBack
                items.RemoveAll(Function(i) sel.Contains(i.Id))
                items.InsertRange(0, moving)
            Case FreeCommand.BringForward
                For i = items.Count - 2 To 0 Step -1
                    If sel.Contains(items(i).Id) AndAlso Not sel.Contains(items(i + 1).Id) Then Swap(items, i, i + 1)
                Next
            Case FreeCommand.SendBackward
                For i = 1 To items.Count - 1
                    If sel.Contains(items(i).Id) AndAlso Not sel.Contains(items(i - 1).Id) Then Swap(items, i, i - 1)
                Next
            Case FreeCommand.Duplicate
                Dim copies = moving.Select(Function(i)
                                               Dim c = i.Clone()
                                               c.CenterX = Math.Min(0.97F, i.CenterX + 0.03F)
                                               c.CenterY = Math.Min(0.97F, i.CenterY + 0.03F)
                                               Return c
                                           End Function).ToList()
                items.AddRange(copies)
                SetFreeSelection(copies.Select(Function(c) c.Id))
            Case FreeCommand.Remove
                items.RemoveAll(Function(i) sel.Contains(i.Id))
                SetFreeSelection(Array.Empty(Of String)())
        End Select
        Invalidate()
        RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
    End Sub

    Private Shared Sub Swap(items As List(Of FreeItem), a As Integer, b As Integer)
        Dim t = items(a)
        items(a) = items(b)
        items(b) = t
    End Sub

    Private Function FreeKeyDown(e As KeyEventArgs) As Boolean
        If e.KeyCode = Keys.A AndAlso e.Control Then
            SetFreeSelection(_project.Free.Items.Select(Function(i) i.Id))
            Return True
        End If
        If _freeSelection.Count = 0 Then Return False

        Select Case e.KeyCode
            Case Keys.Delete
                ExecuteFreeCommand(FreeCommand.Remove)
                Return True
            Case Keys.Escape
                SetFreeSelection(Array.Empty(Of String)())
                Return True
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down
                Dim bounds = GetCanvasBounds()
                Dim stepPx = If(e.Shift, 10, 1)
                Dim dx = If(e.KeyCode = Keys.Left, -stepPx, If(e.KeyCode = Keys.Right, stepPx, 0))
                Dim dy = If(e.KeyCode = Keys.Up, -stepPx, If(e.KeyCode = Keys.Down, stepPx, 0))
                RaiseChangeStarting("free-nudge")
                For Each item In SelectedFreeItems
                    item.CenterX += dx / bounds.Width
                    item.CenterY += dy / bounds.Height
                Next
                Invalidate()
                RaiseEvent FreeItemsChanged(Me, EventArgs.Empty)
                Return True
        End Select
        Return False
    End Function

    Private Function GetFreeHoverCursor(pt As Point, ByRef cursor As Cursor) As Boolean
        If HitTestToolbar(pt) >= 0 Then
            cursor = Cursors.Hand
            Return True
        End If
        Dim item = SingleSelected()
        If item IsNot Nothing Then
            If IsNear(GetFreeRotateHandle(item), pt, Dev(HandleRadius) + 3) Then
                cursor = Cursors.Hand
                Return True
            End If
            If FreeGeometry.HitTestCorner(FreeGeometry.GetFrame(item, GetCanvasBounds()), pt, Dev(HandleRadius) + 2, Dev(3)) >= 0 Then
                cursor = Cursors.SizeNWSE
                Return True
            End If
        End If
        If HitTestText(pt) IsNot Nothing Then Return False
        If FreeGeometry.HitTest(_project.Free.Items, GetCanvasBounds(), pt) IsNot Nothing Then
            cursor = Cursors.SizeAll
            Return True
        End If
        Return False
    End Function

    Private Sub UpdateToolbarHover(pt As Point)
        Dim index = HitTestToolbar(pt)
        If index = _hoverToolbar Then Return
        _hoverToolbar = index
        _toolTip.ShowFor(Me, If(index >= 0, FreeCommandText(ToolbarCommands(index)), Nothing), If(index >= 0, FreeCommandHelp(ToolbarCommands(index)), Nothing))
        Invalidate()
    End Sub

    Friend Shared Function FreeCommandText(command As FreeCommand) As String
        Select Case command
            Case FreeCommand.BringToFront : Return "移到最上層"
            Case FreeCommand.BringForward : Return "上移一層"
            Case FreeCommand.SendBackward : Return "下移一層"
            Case FreeCommand.SendToBack : Return "移到最下層"
            Case FreeCommand.Duplicate : Return "複製"
            Case Else : Return "從畫布移除"
        End Select
    End Function

    Private Shared Function FreeCommandHelp(command As FreeCommand) As String
        Select Case command
            Case FreeCommand.BringToFront : Return "讓選取的照片蓋在所有照片上面。"
            Case FreeCommand.BringForward : Return "讓選取的照片往上一層。"
            Case FreeCommand.SendBackward : Return "讓選取的照片往下一層。"
            Case FreeCommand.SendToBack : Return "讓選取的照片放到所有照片下面。"
            Case FreeCommand.Duplicate : Return "複製選取的照片，放在旁邊。"
            Case Else : Return "從畫布移除選取的照片（Delete），照片仍保留在左側清單。"
        End Select
    End Function

    Private Sub PaintFree(g As Graphics, bounds As RectangleF)
        Dim provider = If(ImageProvider, Function(a As PhotoAsset) CType(Nothing, Image))
        FreeRenderer.Render(g, _project, bounds, provider, New RenderOptions With {
            .HighQuality = False, .BackgroundImage = BackgroundImageProvider?.Invoke()})
        g.SmoothingMode = SmoothingMode.AntiAlias

        If _project.Free.Items.Count = 0 Then
            TextRenderer.DrawText(g, "從左側拖曳縮圖到這裡，或按右側「自動散佈」", Font, Rectangle.Round(bounds), Color.Gray,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
        End If

        ' 對齊參考線
        Using guide As New Pen(GuideColor, 1) With {.DashStyle = DashStyle.Dash}
            For Each x In _snap.VerticalGuides
                g.DrawLine(guide, x, bounds.Top, x, bounds.Bottom)
            Next
            For Each y In _snap.HorizontalGuides
                g.DrawLine(guide, bounds.Left, y, bounds.Right, y)
            Next
        End Using

        ' 選取外框與控制點
        Dim accent = SystemColors.Highlight
        Dim selected = SelectedFreeItems
        Using dash As New Pen(accent, Dev(2)) With {.DashStyle = DashStyle.Dash}
            For Each item In selected
                g.DrawPolygon(dash, FreeGeometry.GetFrame(item, bounds).GetCorners(Dev(3)))
            Next
        End Using
        If selected.Count > 1 Then
            Using thin As New Pen(Color.FromArgb(160, accent), 1) With {.DashStyle = DashStyle.Dot}
                Dim r = GetSelectionBounds()
                g.DrawRectangle(thin, r.X, r.Y, r.Width, r.Height)
            End Using
        ElseIf selected.Count = 1 Then
            DrawFreeHandles(g, selected(0), bounds, accent)
        End If

        If Not _freeMarquee.IsEmpty Then
            Using fill As New SolidBrush(Color.FromArgb(30, accent)), pen As New Pen(accent, 1)
                g.FillRectangle(fill, _freeMarquee)
                g.DrawRectangle(pen, _freeMarquee.X, _freeMarquee.Y, _freeMarquee.Width, _freeMarquee.Height)
            End Using
        End If

        Dim text = SelectedText
        If text IsNot Nothing Then DrawTextSelection(g, text, bounds)
        DrawFreeToolbar(g)
    End Sub

    Private Sub DrawFreeHandles(g As Graphics, item As FreeItem, bounds As RectangleF, accent As Color)
        Dim frame = FreeGeometry.GetFrame(item, bounds)
        Dim corners = frame.GetCorners(Dev(3))
        Dim handle = GetFreeRotateHandle(item)
        Dim topCenter As New PointF((corners(0).X + corners(1).X) / 2, (corners(0).Y + corners(1).Y) / 2)
        Dim r = Dev(HandleRadius) \ 2 + 2
        Using pen As New Pen(accent, Dev(2)), fill As New SolidBrush(Color.White)
            g.DrawLine(pen, topCenter, handle)
            g.FillEllipse(fill, handle.X - r - 1, handle.Y - r - 1, (r + 1) * 2, (r + 1) * 2)
            g.DrawEllipse(pen, handle.X - r - 1, handle.Y - r - 1, (r + 1) * 2, (r + 1) * 2)
            For Each c In corners
                g.FillRectangle(fill, c.X - r, c.Y - r, r * 2, r * 2)
                g.DrawRectangle(pen, c.X - r, c.Y - r, r * 2, r * 2)
            Next
        End Using
    End Sub

    Private Sub DrawFreeToolbar(g As Graphics)
        Dim toolbar = GetToolbarRect()
        If toolbar.IsEmpty Then Return
        Using path = CollageRenderer.CreateCellPath(toolbar, Dev(8)),
              shadow = CollageRenderer.CreateCellPath(New RectangleF(toolbar.X + 1, toolbar.Y + 3, toolbar.Width, toolbar.Height), Dev(8)),
              shadowBrush As New SolidBrush(Color.FromArgb(70, 0, 0, 0)), back As New SolidBrush(Color.White)
            g.FillPath(shadowBrush, shadow)
            g.FillPath(back, path)
        End Using
        For i = 0 To ToolbarCommands.Length - 1
            Dim r = GetToolbarButtonRect(toolbar, i)
            If i = _hoverToolbar Then
                Using hover = CollageRenderer.CreateCellPath(Rectangle.Inflate(r, -1, -1), Dev(5)), b As New SolidBrush(Color.FromArgb(232, 240, 250))
                    g.FillPath(b, hover)
                End Using
            End If
            DrawToolbarIcon(g, ToolbarCommands(i), r)
        Next
        Using sep As New Pen(Color.FromArgb(213, 218, 225))
            Dim x = GetToolbarButtonRect(toolbar, 4).X - Dev(5)
            g.DrawLine(sep, x, toolbar.Y + Dev(8), x, toolbar.Bottom - Dev(8))
        End Using
    End Sub

    ''' <summary>工具列圖示：以 18×18 的設計格線繪製簡單線條。</summary>
    Private Sub DrawToolbarIcon(g As Graphics, command As FreeCommand, r As Rectangle)
        Dim s = Dev(18) / 18.0F
        Dim ox = r.X + (r.Width - Dev(18)) / 2.0F, oy = r.Y + (r.Height - Dev(18)) / 2.0F
        Dim P = Function(x As Single, y As Single) New PointF(ox + x * s, oy + y * s)
        Dim iconColor = If(command = FreeCommand.Remove, Color.FromArgb(180, 35, 24), Color.FromArgb(29, 35, 48))
        Using pen As New Pen(iconColor, Math.Max(1.4F, 1.6F * s)) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
            Select Case command
                Case FreeCommand.BringToFront
                    g.DrawRectangle(pen, ox + 3 * s, oy + 7 * s, 12 * s, 8 * s)
                    g.DrawLine(pen, P(6, 4), P(12, 4))
                    g.DrawLine(pen, P(9, 1.5F), P(9, 5.5F))
                Case FreeCommand.SendToBack
                    g.DrawRectangle(pen, ox + 3 * s, oy + 3 * s, 12 * s, 8 * s)
                    g.DrawLine(pen, P(6, 14), P(12, 14))
                    g.DrawLine(pen, P(9, 12.5F), P(9, 16.5F))
                Case FreeCommand.BringForward
                    g.DrawLine(pen, P(9, 14), P(9, 4))
                    g.DrawLines(pen, {P(5, 8), P(9, 4), P(13, 8)})
                Case FreeCommand.SendBackward
                    g.DrawLine(pen, P(9, 4), P(9, 14))
                    g.DrawLines(pen, {P(5, 10), P(9, 14), P(13, 10)})
                Case FreeCommand.Duplicate
                    g.DrawRectangle(pen, ox + 6 * s, oy + 6 * s, 9 * s, 9 * s)
                    g.DrawLines(pen, {P(12, 4), P(12, 3), P(3, 3), P(3, 12), P(4, 12)})
                Case FreeCommand.Remove
                    g.DrawLine(pen, P(3, 5), P(15, 5))
                    g.DrawLines(pen, {P(7, 5), P(7, 3), P(11, 3), P(11, 5)})
                    g.DrawLines(pen, {P(5, 5), P(6, 15), P(12, 15), P(13, 5)})
            End Select
        End Using
    End Sub

#End Region

#Region "拖放"

    Protected Overrides Sub OnDragOver(e As DragEventArgs)
        MyBase.OnDragOver(e)
        If e.Data Is Nothing OrElse _project Is Nothing Then Return

        If e.Data.GetDataPresent(PhotoDragFormat) Then
            If IsMosaic Then
                e.Effect = DragDropEffects.None
                Return
            End If
            If IsFree Then
                e.Effect = If(GetCanvasBounds().Contains(PointToClient(New Point(e.X, e.Y))), DragDropEffects.Copy, DragDropEffects.None)
                Return
            End If
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
        If photoId IsNot Nothing AndAlso IsFree Then
            Dim bounds = GetCanvasBounds()
            Dim pt = PointToClient(New Point(e.X, e.Y))
            If Not bounds.Contains(pt) Then Return
            RaiseEvent FreePhotoDropped(Me, New FreePhotoDroppedEventArgs(photoId,
                New PointF((pt.X - bounds.X) / bounds.Width, (pt.Y - bounds.Y) / bounds.Height)))
            Return
        End If
        If photoId IsNot Nothing AndAlso Not IsMosaic Then
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
        If disposing Then
            _menu.Dispose()
            _toolTip.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

Friend Enum FreeCommand
    BringToFront
    BringForward
    SendBackward
    SendToBack
    Duplicate
    Remove
End Enum

Friend Class FreePhotoDroppedEventArgs
    Inherits EventArgs

    Public ReadOnly Property PhotoId As String
    ''' <summary>放下的位置，0~1 相對畫布。</summary>
    Public ReadOnly Property Position As PointF

    Public Sub New(photoId As String, position As PointF)
        Me.PhotoId = photoId
        Me.Position = position
    End Sub
End Class

Friend Enum MosaicCellCommandKind
    NextAlternative
    UseSelectedPhoto
End Enum

Friend Class MosaicCellCommandEventArgs
    Inherits EventArgs

    Public ReadOnly Property Cell As Integer
    Public ReadOnly Property Kind As MosaicCellCommandKind

    Public Sub New(cell As Integer, kind As MosaicCellCommandKind)
        Me.Cell = cell
        Me.Kind = kind
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

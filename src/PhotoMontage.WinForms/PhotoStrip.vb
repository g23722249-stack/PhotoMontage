Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports PhotoMontage.Core

''' <summary>
''' 照片縮圖清單。只繪製可見範圍內的項目；支援多選、Delete 移除、拖曳調整順序、拖放檔案，
''' 拖出清單外可把照片放到畫布上，雙擊照片放進第一個空格。
''' </summary>
Friend Class PhotoStrip
    Inherits ScrollableControl

    Private NotInheritable Class Item
        Public ReadOnly Asset As PhotoAsset
        Public ReadOnly Sequence As Integer
        Public Tile As Bitmap
        Public Selected As Boolean

        Public Sub New(asset As PhotoAsset, sequence As Integer)
            Me.Asset = asset
            Me.Sequence = sequence
        End Sub
    End Class

    Private ReadOnly _items As New List(Of Item)
    Private ReadOnly _toolTip As New ToolTip()
    Private ReadOnly _menu As New ContextMenuStrip()
    Private _nextSequence As Integer
    Private _anchorIndex As Integer = -1
    Private _hoverIndex As Integer = -1
    Private _pressIndex As Integer = -1
    Private _pressPoint As Point
    Private _dragging As Boolean
    Private _dropIndex As Integer = -1

    ''' <summary>使用者移除了照片。</summary>
    Public Event ItemsRemoved As EventHandler(Of PhotosEventArgs)

    ''' <summary>順序改變（拖曳或排序）。</summary>
    Public Event OrderChanged As EventHandler

    ''' <summary>從檔案總管拖放了檔案或資料夾。</summary>
    Public Event FilesDropped As EventHandler(Of FilesDroppedEventArgs)

    ''' <summary>雙擊了照片。</summary>
    Public Event ItemActivated As EventHandler(Of PhotosEventArgs)

    Private _usedPhotoIds As ISet(Of String) = New HashSet(Of String)

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AutoScroll = True
        AllowDrop = True
        TabStop = True
        BackColor = Color.FromArgb(32, 32, 32)

        _menu.Items.Add("移除", Nothing, Sub() RemoveSelected())
        _menu.Items.Add(New ToolStripSeparator())
        _menu.Items.Add("依匯入順序排列", Nothing, Sub() Sort(Function(a, b) 0, bySequence:=True))
        _menu.Items.Add("依檔名排列", Nothing, Sub() Sort(AddressOf PhotoSorting.ByFileName))
        _menu.Items.Add("依拍攝時間排列", Nothing, Sub() Sort(AddressOf PhotoSorting.ByDateTaken))
        AddHandler _menu.Opening, Sub(s, e) _menu.Items(0).Enabled = _items.Exists(Function(i) i.Selected)
        ContextMenuStrip = _menu
    End Sub

    ''' <summary>目前順序的照片。</summary>
    Public ReadOnly Property Assets As IReadOnlyList(Of PhotoAsset)
        Get
            Return _items.Select(Function(i) i.Asset).ToList()
        End Get
    End Property

    ''' <summary>已放進畫布的照片 Id；這些照片會顯示勾選標記。</summary>
    Public Property UsedPhotoIds As ISet(Of String)
        Get
            Return _usedPhotoIds
        End Get
        Set(value As ISet(Of String))
            _usedPhotoIds = If(value, New HashSet(Of String))
            Invalidate()
        End Set
    End Property

    Public ReadOnly Property TileSize As Integer
        Get
            Return LogicalToDeviceUnits(96)
        End Get
    End Property

    Private ReadOnly Property Gap As Integer
        Get
            Return LogicalToDeviceUnits(6)
        End Get
    End Property

    Public Sub AddRange(assets As IEnumerable(Of PhotoAsset))
        For Each a In assets
            _items.Add(New Item(a, _nextSequence))
            _nextSequence += 1
        Next
        UpdateScrollSize()
        Invalidate()
    End Sub

    ''' <summary>設定縮圖；清單接管 <paramref name="tile"/> 的生命週期。</summary>
    Public Sub SetTile(asset As PhotoAsset, tile As Bitmap)
        Dim item = _items.Find(Function(i) i.Asset Is asset)
        If item Is Nothing Then
            tile?.Dispose()
            Return
        End If
        item.Tile?.Dispose()
        item.Tile = tile
        InvalidateItem(_items.IndexOf(item))
    End Sub

    Public Sub RefreshItem(asset As PhotoAsset)
        InvalidateItem(_items.FindIndex(Function(i) i.Asset Is asset))
    End Sub

    ''' <summary>移除照片，不觸發 <see cref="ItemsRemoved"/>。</summary>
    Public Sub RemoveRange(assets As IEnumerable(Of PhotoAsset))
        Dim set_ = New HashSet(Of PhotoAsset)(assets)
        For Each item In _items.Where(Function(i) set_.Contains(i.Asset)).ToList()
            item.Tile?.Dispose()
            _items.Remove(item)
        Next
        _anchorIndex = -1
        UpdateScrollSize()
        Invalidate()
    End Sub

    Public Sub RemoveSelected()
        Dim removed = _items.Where(Function(i) i.Selected).Select(Function(i) i.Asset).ToList()
        If removed.Count = 0 Then Return
        RemoveRange(removed)
        RaiseEvent ItemsRemoved(Me, New PhotosEventArgs(removed))
    End Sub

    ''' <summary>依指定順序重排（未列出的照片維持原相對順序、排在後面），不觸發 <see cref="OrderChanged"/>。</summary>
    Public Sub SetOrder(order As IEnumerable(Of PhotoAsset))
        Dim rank As New Dictionary(Of PhotoAsset, Integer)
        For Each a In order
            If Not rank.ContainsKey(a) Then rank(a) = rank.Count
        Next
        Dim sorted = _items.Select(Function(item, i) (item, i)).
            OrderBy(Function(x) If(rank.ContainsKey(x.item.Asset), rank(x.item.Asset), Integer.MaxValue)).
            ThenBy(Function(x) x.i).
            Select(Function(x) x.item).ToList()
        _items.Clear()
        _items.AddRange(sorted)
        Invalidate()
    End Sub

    Private Sub Sort(comparison As Comparison(Of PhotoAsset), Optional bySequence As Boolean = False)
        Dim sorted As List(Of Item)
        If bySequence Then
            sorted = _items.OrderBy(Function(i) i.Sequence).ToList()
        Else
            Dim order = PhotoSorting.StableSort(_items.Select(Function(i) i.Asset), comparison)
            Dim lookup = _items.ToDictionary(Function(i) i.Asset)
            sorted = order.Select(Function(a) lookup(a)).ToList()
        End If
        _items.Clear()
        _items.AddRange(sorted)
        Invalidate()
        RaiseEvent OrderChanged(Me, EventArgs.Empty)
    End Sub

#Region "版面計算"

    Private ReadOnly Property Columns As Integer
        Get
            Return Math.Max(1, (ClientSize.Width - Gap) \ (TileSize + Gap))
        End Get
    End Property

    Private Function GetItemBounds(index As Integer) As Rectangle
        Dim cols = Columns
        Dim pitch = TileSize + Gap
        Return New Rectangle(Gap + (index Mod cols) * pitch,
                             Gap + (index \ cols) * pitch + AutoScrollPosition.Y,
                             TileSize, TileSize)
    End Function

    Private Function HitTest(pt As Point) As Integer
        For i = 0 To _items.Count - 1
            If GetItemBounds(i).Contains(pt) Then Return i
        Next
        Return -1
    End Function

    ''' <summary>拖曳時的插入位置（0 ~ Count）。</summary>
    Private Function GetInsertIndex(pt As Point) As Integer
        Dim cols = Columns
        Dim pitch = TileSize + Gap
        Dim row = Math.Max(0, (pt.Y - AutoScrollPosition.Y - Gap) \ pitch)
        Dim col = CInt(Math.Round((pt.X - Gap) / pitch))
        col = Math.Max(0, Math.Min(cols, col))
        Return Math.Max(0, Math.Min(_items.Count, row * cols + col))
    End Function

    Private Sub UpdateScrollSize()
        Dim rows = (_items.Count + Columns - 1) \ Columns
        AutoScrollMinSize = New Size(0, Gap + rows * (TileSize + Gap))
    End Sub

    Private Sub InvalidateItem(index As Integer)
        If index >= 0 Then Invalidate(GetItemBounds(index))
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        UpdateScrollSize()
    End Sub

#End Region

#Region "繪製"

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        If _items.Count = 0 Then
            TextRenderer.DrawText(g, "將照片或資料夾拖放到這裡", Font, ClientRectangle, Color.Gray,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
            Return
        End If

        Dim pitch = TileSize + Gap
        Dim cols = Columns
        Dim firstRow = Math.Max(0, (-AutoScrollPosition.Y + e.ClipRectangle.Top - Gap) \ pitch)
        Dim lastRow = (-AutoScrollPosition.Y + e.ClipRectangle.Bottom) \ pitch
        Dim first = firstRow * cols
        Dim last = Math.Min(_items.Count - 1, (lastRow + 1) * cols - 1)

        g.InterpolationMode = InterpolationMode.HighQualityBicubic
        For i = first To last
            DrawItem(g, _items(i), GetItemBounds(i))
        Next

        If _dragging AndAlso _dropIndex >= 0 Then DrawInsertionMark(g)
    End Sub

    Private Sub DrawItem(g As Graphics, item As Item, bounds As Rectangle)
        Using back As New SolidBrush(Color.FromArgb(56, 56, 56))
            g.FillRectangle(back, bounds)
        End Using

        Select Case item.Asset.Status
            Case PhotoStatus.Ready
                If item.Tile IsNot Nothing Then
                    Dim w = item.Tile.Width, h = item.Tile.Height
                    Dim scale = Math.Min(bounds.Width / w, bounds.Height / h)
                    Dim dw = CInt(w * scale), dh = CInt(h * scale)
                    g.DrawImage(item.Tile, bounds.X + (bounds.Width - dw) \ 2, bounds.Y + (bounds.Height - dh) \ 2, dw, dh)
                End If
            Case PhotoStatus.Pending
                TextRenderer.DrawText(g, "…", Font, bounds, Color.Silver, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Case PhotoStatus.Failed
                Using pen As New Pen(Color.IndianRed, LogicalToDeviceUnits(2))
                    g.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1))
                End Using
                TextRenderer.DrawText(g, "!" & Environment.NewLine & item.Asset.FileName, Font, Rectangle.Inflate(bounds, -4, -4), Color.IndianRed,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak Or TextFormatFlags.EndEllipsis)
        End Select

        If _usedPhotoIds.Contains(item.Asset.Id) Then DrawUsedBadge(g, bounds)

        If item.Selected Then
            Using pen As New Pen(SystemColors.Highlight, LogicalToDeviceUnits(3))
                g.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1))
            End Using
        End If
    End Sub

    Private Sub DrawUsedBadge(g As Graphics, bounds As Rectangle)
        Dim size = LogicalToDeviceUnits(18)
        Dim badge As New Rectangle(bounds.Right - size - 3, bounds.Top + 3, size, size)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using back As New SolidBrush(Color.FromArgb(220, 46, 160, 67))
            g.FillEllipse(back, badge)
        End Using
        Using pen As New Pen(Color.White, LogicalToDeviceUnits(2))
            g.DrawLines(pen, {
                New PointF(badge.Left + size * 0.25F, badge.Top + size * 0.52F),
                New PointF(badge.Left + size * 0.43F, badge.Top + size * 0.7F),
                New PointF(badge.Left + size * 0.76F, badge.Top + size * 0.32F)})
        End Using
        g.SmoothingMode = SmoothingMode.Default
    End Sub

    Private Sub DrawInsertionMark(g As Graphics)
        Dim cols = Columns
        Dim pitch = TileSize + Gap
        Dim x = Gap \ 2 + (_dropIndex Mod cols) * pitch
        Dim y = Gap + (_dropIndex \ cols) * pitch + AutoScrollPosition.Y
        ' 插在列尾時畫在上一列最後一格的右邊
        If _dropIndex > 0 AndAlso _dropIndex Mod cols = 0 Then
            x = Gap \ 2 + cols * pitch
            y -= pitch
        End If
        Using pen As New Pen(SystemColors.Highlight, LogicalToDeviceUnits(3))
            g.DrawLine(pen, x, y, x, y + TileSize)
        End Using
    End Sub

#End Region

#Region "滑鼠與鍵盤"

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        Dim index = HitTest(e.Location)

        If index < 0 Then
            If e.Button = MouseButtons.Left Then SetSelection(Function(i) False)
            Return
        End If

        If e.Button = MouseButtons.Right Then
            If Not _items(index).Selected Then SelectOnly(index)
            Return
        End If
        If e.Button <> MouseButtons.Left Then Return

        If (ModifierKeys And Keys.Control) = Keys.Control Then
            _items(index).Selected = Not _items(index).Selected
            _anchorIndex = index
            Invalidate()
        ElseIf (ModifierKeys And Keys.Shift) = Keys.Shift AndAlso _anchorIndex >= 0 Then
            Dim lo = Math.Min(_anchorIndex, index), hi = Math.Max(_anchorIndex, index)
            For i = 0 To _items.Count - 1
                _items(i).Selected = i >= lo AndAlso i <= hi
            Next
            Invalidate()
        ElseIf Not _items(index).Selected Then
            SelectOnly(index)
        End If

        _pressIndex = index
        _pressPoint = e.Location
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)

        If e.Button = MouseButtons.Left AndAlso _pressIndex >= 0 Then
            If Not _dragging Then
                Dim drag = SystemInformation.DragSize
                _dragging = Math.Abs(e.X - _pressPoint.X) > drag.Width OrElse Math.Abs(e.Y - _pressPoint.Y) > drag.Height
            End If
            If _dragging AndAlso Not ClientRectangle.Contains(e.Location) Then
                StartDragOut()
                Return
            End If
            If _dragging Then
                Dim index = GetInsertIndex(e.Location)
                If index <> _dropIndex Then
                    _dropIndex = index
                    Invalidate()
                End If
            End If
            Return
        End If

        Dim hover = HitTest(e.Location)
        If hover <> _hoverIndex Then
            _hoverIndex = hover
            _toolTip.SetToolTip(Me, If(hover >= 0, Describe(_items(hover).Asset), Nothing))
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If _dragging Then
            MoveSelected(_dropIndex)
        ElseIf _pressIndex >= 0 AndAlso e.Button = MouseButtons.Left AndAlso ModifierKeys = Keys.None Then
            SelectOnly(_pressIndex) ' 點一下已選取的多張之一 → 只選它
        End If
        _pressIndex = -1
        _dragging = False
        _dropIndex = -1
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        Dim index = HitTest(e.Location)
        If e.Button = MouseButtons.Left AndAlso index >= 0 Then
            RaiseEvent ItemActivated(Me, New PhotosEventArgs({_items(index).Asset}))
        End If
    End Sub

    ''' <summary>拖出清單範圍：改用 OLE 拖放，讓畫布可以接收。</summary>
    Private Sub StartDragOut()
        Dim asset = _items(_pressIndex).Asset
        _pressIndex = -1
        _dragging = False
        _dropIndex = -1
        Invalidate()
        If asset.Status <> PhotoStatus.Ready Then Return

        Dim data As New DataObject()
        data.SetData(CollageCanvas.PhotoDragFormat, asset.Id)
        DoDragDrop(data, DragDropEffects.Move)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        If e.KeyCode = Keys.Delete Then
            RemoveSelected()
            e.Handled = True
        ElseIf e.KeyCode = Keys.A AndAlso e.Control Then
            SetSelection(Function(i) True)
            e.Handled = True
        End If
    End Sub

    Protected Overrides Sub OnDragEnter(e As DragEventArgs)
        MyBase.OnDragEnter(e)
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
    End Sub

    Protected Overrides Sub OnDragDrop(e As DragEventArgs)
        MyBase.OnDragDrop(e)
        Dim files = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing AndAlso files.Length > 0 Then RaiseEvent FilesDropped(Me, New FilesDroppedEventArgs(files))
    End Sub

    Private Sub SelectOnly(index As Integer)
        SetSelection(Function(i) i = index)
        _anchorIndex = index
    End Sub

    Private Sub SetSelection(predicate As Func(Of Integer, Boolean))
        For i = 0 To _items.Count - 1
            _items(i).Selected = predicate(i)
        Next
        Invalidate()
    End Sub

    ''' <summary>把選取的項目（維持相對順序）移到插入位置。</summary>
    Private Sub MoveSelected(insertIndex As Integer)
        If insertIndex < 0 Then Return
        Dim moving = _items.Where(Function(i) i.Selected).ToList()
        If moving.Count = 0 Then Return

        Dim before = _items.Take(insertIndex).Count(Function(i) i.Selected)
        Dim target = insertIndex - before
        Dim original = _items.ToList()
        _items.RemoveAll(Function(i) i.Selected)
        _items.InsertRange(Math.Min(target, _items.Count), moving)

        If Not original.SequenceEqual(_items) Then RaiseEvent OrderChanged(Me, EventArgs.Empty)
    End Sub

    Private Shared Function Describe(asset As PhotoAsset) As String
        Select Case asset.Status
            Case PhotoStatus.Failed
                Return $"{asset.FileName}{Environment.NewLine}{ImportFailure.Describe(asset.FailureReason.GetValueOrDefault())}"
            Case PhotoStatus.Pending
                Return $"{asset.FileName}{Environment.NewLine}讀取中…"
            Case Else
                Dim lines As New List(Of String) From {
                    asset.FileName,
                    $"{asset.PixelSize.Width} × {asset.PixelSize.Height}"
                }
                If asset.DateTaken.HasValue Then lines.Add($"拍攝：{asset.DateTaken.Value:yyyy/MM/dd HH:mm}")
                Return String.Join(Environment.NewLine, lines)
        End Select
    End Function

#End Region

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            For Each item In _items
                item.Tile?.Dispose()
            Next
            _items.Clear()
            _toolTip.Dispose()
            _menu.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

Friend Class PhotosEventArgs
    Inherits EventArgs

    Public ReadOnly Property Photos As IReadOnlyList(Of PhotoAsset)

    Public Sub New(photos As IReadOnlyList(Of PhotoAsset))
        Me.Photos = photos
    End Sub
End Class

Friend Class FilesDroppedEventArgs
    Inherits EventArgs

    Public ReadOnly Property Paths As IReadOnlyList(Of String)

    Public Sub New(paths As IReadOnlyList(Of String))
        Me.Paths = paths
    End Sub
End Class

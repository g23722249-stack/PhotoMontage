Imports System.Drawing
Imports System.Windows.Forms
Imports System.Windows.Forms.VisualStyles

''' <summary>
''' 「選取照片」畫面（Aqua 視窗），外觀與 iPhoto 的相簿相同：左邊是 <see cref="IPhotoSource"/> 的樹，
''' 相簿前有勾選框（可勾多本，勾 = 整本），右邊是點選的相簿的照片（Aqua MediaList），可逐張勾選；
''' 選取跨相簿累積。也用來選擇作品存到哪本相簿（<paramref name="pickFolder"/>）。
''' </summary>
Friend Class PhotoPickerDialog
    Inherits Aqua.AquaForm

    ''' <summary>樹節點的勾選圖示（StateImageIndex）。</summary>
    Private Const StateNone As Integer = 0, StateAll As Integer = 1, StateSome As Integer = 2, StateBlank As Integer = 3
    Private Const AquaResizeGripSize As Integer = 16
    Private Const LoadingText As String = "讀取中…"

    Private ReadOnly _source As IPhotoSource
    Private ReadOnly _pickFolder As Boolean
    Private ReadOnly _limit As Integer
    Private ReadOnly _tree As TreeView
    Private ReadOnly _list As Aqua.MediaList
    Private ReadOnly _header As System.Windows.Forms.Label
    Private ReadOnly _summary As System.Windows.Forms.Label
    Private ReadOnly _allButton As PillButton
    Private ReadOnly _noneButton As PillButton
    Private ReadOnly _okButton As PillButton

    ''' <summary>讀過的照片清單，依節點。</summary>
    Private ReadOnly _photos As New Dictionary(Of PhotoSourceNode, IReadOnlyList(Of String))
    ''' <summary>整個勾選的節點。</summary>
    Private ReadOnly _whole As New HashSet(Of PhotoSourceNode)
    ''' <summary>只勾了部分照片的節點，以及勾了哪些。</summary>
    Private ReadOnly _picked As New Dictionary(Of PhotoSourceNode, HashSet(Of String))
    Private _shown As TreeNode
    Private _suppressChecks As Boolean

    ''' <summary>勾選的照片（依樹的順序，不重複）。</summary>
    Public ReadOnly Property SelectedPhotos As New List(Of String)

    ''' <summary>選擇存放位置時選的資料夾。</summary>
    Public Property SelectedFolder As String

    ''' <param name="limit">最多可加入幾張（只用來提示）；0 = 不提示。</param>
    ''' <param name="pickFolder">選擇作品存到哪裡：只能選一個有 <see cref="PhotoSourceNode.Folder"/> 的節點，不勾選照片。</param>
    Public Sub New(source As IPhotoSource, accent As Aqua.ColorConstants, Optional limit As Integer = 0, Optional pickFolder As Boolean = False)
        _source = source
        _pickFolder = pickFolder
        _limit = limit

        Text = If(pickFolder, "作品存到哪裡", $"從 {source.Title} 選取照片")
        TitleFont = New Font("Microsoft JhengHei UI", 10.0F, FontStyle.Bold)
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        MinButton = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        MinimumSize = New Size(700, 480)
        ClientSize = New Size(1040, 680)
        KeyPreview = True

        _tree = New TreeView() With {
            .Dock = DockStyle.Fill, .HideSelection = False, .ShowLines = False, .FullRowSelect = True,
            .ItemHeight = 24, .BorderStyle = BorderStyle.FixedSingle, .Font = New Font("Microsoft JhengHei UI", 10.0F)}
        If Not pickFolder Then _tree.StateImageList = CreateStateImages()
        AddHandler _tree.BeforeExpand, Sub(s, e) EnsureChildren(e.Node)
        AddHandler _tree.AfterSelect, Sub(s, e) ShowNode(e.Node)
        AddHandler _tree.NodeMouseClick, AddressOf OnTreeMouseClick
        AddHandler _tree.KeyDown, AddressOf OnTreeKeyDown

        Dim hint As New System.Windows.Forms.Label() With {
            .Dock = DockStyle.Top, .AutoSize = False, .Height = 40, .BackColor = Color.Transparent,
            .Text = If(pickFolder, "點選要存放作品的相簿或資料夾。", "勾選相簿 = 整本加入；點相簿可在右邊逐張勾選。")}
        Dim left As New Panel() With {.Dock = DockStyle.Left, .Width = 300, .Padding = New Padding(0, 0, 8, 0), .BackColor = Color.Transparent}
        left.Controls.Add(_tree)
        left.Controls.Add(hint)

        _header = New System.Windows.Forms.Label() With {
            .Dock = DockStyle.Top, .AutoSize = False, .Height = 28, .BackColor = Color.Transparent,
            .Font = New Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold), .TextAlign = System.Drawing.ContentAlignment.MiddleLeft}
        _list = New Aqua.MediaList() With {
            .Dock = DockStyle.Fill, .ShowRating = False, .ShowCheckBox = Not pickFolder, .MultiSelect = True,
            .ItemSize = New Size(170, 160), .DragItem = False, .DropItem = False, .BackColor = SystemColors.Window}
        AddHandler _list.ItemCheckedChanged, AddressOf OnItemChecked
        AddHandler _list.ItemDblClick, AddressOf OnItemDoubleClick
        AddHandler _list.ItemKeyDown, AddressOf OnItemKeyDown
        Dim right As New Panel() With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        right.Controls.Add(_list)
        right.Controls.Add(_header)

        _okButton = New PillButton() With {.Text = If(pickFolder, "存到這裡", "加入"), .Width = 100, .Enabled = False}
        AddHandler _okButton.Click, AddressOf OnOkClick
        Dim cancelButton As New PillButton() With {.Text = "取消", .Width = 90}
        AddHandler cancelButton.Click, Sub(s, e)
                                           DialogResult = DialogResult.Cancel
                                           Close()
                                       End Sub
        _allButton = New PillButton() With {.Text = "全選", .Width = 80, .Enabled = False, .Visible = Not pickFolder}
        AddHandler _allButton.Click, Sub(s, e) CheckShown(True)
        _noneButton = New PillButton() With {.Text = "全不選", .Width = 80, .Enabled = False, .Visible = Not pickFolder}
        AddHandler _noneButton.Click, Sub(s, e) CheckShown(False)
        _summary = New System.Windows.Forms.Label() With {.AutoSize = True, .Margin = New Padding(12, 7, 0, 0), .BackColor = Color.Transparent}

        Dim buttons As New FlowLayoutPanel() With {.Dock = DockStyle.Right, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .WrapContents = False, .BackColor = Color.Transparent}
        buttons.Controls.Add(cancelButton)
        buttons.Controls.Add(_okButton)
        Dim tools As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = False, .BackColor = Color.Transparent}
        tools.Controls.Add(_allButton)
        tools.Controls.Add(_noneButton)
        tools.Controls.Add(_summary)
        Dim bottom As New Panel() With {.Dock = DockStyle.Bottom, .Height = PillButton.DefaultHeight + 16, .Padding = New Padding(0, 10, 0, 0), .BackColor = Color.Transparent}
        bottom.Controls.Add(tools)
        bottom.Controls.Add(buttons)

        Controls.Add(right)
        Controls.Add(left)
        Controls.Add(bottom)

        Dim help As New HelpToolTip(Me)
        If Not pickFolder Then
            help.SetHelp(HelpTexts.PickerTree, _tree)
            help.SetHelp(HelpTexts.PickerAll, _allButton)
            help.SetHelp(HelpTexts.PickerNone, _noneButton)
            help.SetHelp(HelpTexts.PickerAdd, _okButton)
        End If

        FillTree()
        UpdateSummary()
        AquaTheme.Apply(Me, accent)
    End Sub

    ''' <summary>內容放在 Aqua 標題列下方，並留出右下角的調整大小把手。在自動縮放之後設定，確保是實際像素。</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Padding = New Padding(12, MontageEditorForm.AquaTitleBarHeight + 8, 12, AquaResizeGripSize)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            DialogResult = DialogResult.Cancel
            Close()
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Private Shared Function CreateStateImages() As ImageList
        Dim images As New ImageList() With {.ImageSize = New Size(16, 16), .ColorDepth = ColorDepth.Depth32Bit}
        For Each state In {CheckBoxState.UncheckedNormal, CheckBoxState.CheckedNormal, CheckBoxState.MixedNormal}
            Dim bmp As New Bitmap(16, 16)
            Using g = Graphics.FromImage(bmp)
                Dim glyph = CheckBoxRenderer.GetGlyphSize(g, state)
                CheckBoxRenderer.DrawCheckBox(g, New Point((16 - glyph.Width) \ 2, (16 - glyph.Height) \ 2), state)
            End Using
            images.Images.Add(bmp)
        Next
        images.Images.Add(New Bitmap(16, 16))   ' StateBlank：不能勾選的節點
        Return images
    End Function

#Region "樹"

    Private Shared Function NodeOf(t As TreeNode) As PhotoSourceNode
        Return TryCast(t?.Tag, PhotoSourceNode)
    End Function

    Private Sub FillTree()
        _tree.BeginUpdate()
        For Each root In _source.Roots()
            Dim t = AddTreeNode(_tree.Nodes, root)
            If root.Expanded Then t.Expand()
        Next
        _tree.EndUpdate()

        ' 開啟時先顯示的節點
        Dim path = _source.StartPath
        If path Is Nothing Then Return
        Dim nodes = _tree.Nodes
        Dim found As TreeNode = Nothing
        For Each key In path
            Dim match = nodes.Cast(Of TreeNode)().FirstOrDefault(Function(n) String.Equals(NodeOf(n)?.Key, key, StringComparison.OrdinalIgnoreCase))
            If match Is Nothing Then Exit For
            If found IsNot Nothing Then found.Expand()
            found = match
            EnsureChildren(found)
            nodes = found.Nodes
        Next
        If found IsNot Nothing Then
            _tree.SelectedNode = found
            found.EnsureVisible()
        End If
    End Sub

    Private Function AddTreeNode(parent As TreeNodeCollection, node As PhotoSourceNode) As TreeNode
        Dim t As New TreeNode(node.Text) With {.Tag = node}
        If node.Children IsNot Nothing Then t.Nodes.Add(New TreeNode(LoadingText))   ' 展開時才讀子節點
        parent.Add(t)
        UpdateStateImage(t)
        Return t
    End Function

    ''' <summary>第一次展開時讀子節點。</summary>
    Private Sub EnsureChildren(t As TreeNode)
        If t.Nodes.Count <> 1 OrElse t.Nodes(0).Tag IsNot Nothing Then Return
        Dim node = NodeOf(t)
        If node?.Children Is Nothing Then Return
        Dim children As IReadOnlyList(Of PhotoSourceNode)
        Using New WaitCursorScope(Me)
            children = node.Children.Invoke()
        End Using
        _tree.BeginUpdate()
        t.Nodes.Clear()
        For Each c In If(children, CType(Array.Empty(Of PhotoSourceNode)(), IReadOnlyList(Of PhotoSourceNode)))
            AddTreeNode(t.Nodes, c)
        Next
        _tree.EndUpdate()
    End Sub

    Private Function PhotosOf(node As PhotoSourceNode) As IReadOnlyList(Of String)
        Dim list As IReadOnlyList(Of String) = Nothing
        If _photos.TryGetValue(node, list) Then Return list
        If node.Photos Is Nothing Then Return Array.Empty(Of String)()
        Using New WaitCursorScope(Me)
            list = If(node.Photos.Invoke(), CType(Array.Empty(Of String)(), IReadOnlyList(Of String)))
        End Using
        _photos(node) = list
        Return list
    End Function

    Private Function StateOf(t As TreeNode) As Integer
        Dim node = NodeOf(t)
        If node Is Nothing Then Return StateBlank
        Dim picked As HashSet(Of String) = Nothing
        If node.Photos IsNot Nothing Then
            If _whole.Contains(node) Then Return StateAll
            If _picked.TryGetValue(node, picked) AndAlso picked.Count > 0 Then Return StateSome
            Return If(node.Checkable, StateNone, StateBlank)
        End If
        ' 分組：依已讀的子節點
        If Not node.Checkable Then Return StateBlank
        Dim states = t.Nodes.Cast(Of TreeNode)().Select(Function(c) StateOf(c)).Where(Function(s) s <> StateBlank).ToList()
        If states.Count > 0 AndAlso states.All(Function(s) s = StateAll) Then Return StateAll
        If states.Any(Function(s) s <> StateNone) Then Return StateSome
        Return StateNone
    End Function

    Private Sub UpdateStateImage(t As TreeNode)
        If _pickFolder Then Return
        Dim state = StateOf(t)
        If t.StateImageIndex <> state Then t.StateImageIndex = state
    End Sub

    ''' <summary>節點與它底下已讀的節點、以及上層各節點的勾選圖示。</summary>
    Private Sub RefreshStates(t As TreeNode)
        RefreshSubtree(t)
        Dim p = t.Parent
        While p IsNot Nothing
            UpdateStateImage(p)
            p = p.Parent
        End While
    End Sub

    Private Sub RefreshSubtree(t As TreeNode)
        For Each c As TreeNode In t.Nodes
            RefreshSubtree(c)
        Next
        UpdateStateImage(t)
    End Sub

    Private Sub OnTreeMouseClick(sender As Object, e As TreeNodeMouseClickEventArgs)
        If _pickFolder OrElse e.Button <> MouseButtons.Left Then Return
        If _tree.HitTest(e.Location).Location = TreeViewHitTestLocations.StateImage Then ToggleNode(e.Node)
    End Sub

    Private Sub OnTreeKeyDown(sender As Object, e As KeyEventArgs)
        If _pickFolder OrElse e.KeyCode <> Keys.Space OrElse _tree.SelectedNode Is Nothing Then Return
        ToggleNode(_tree.SelectedNode)
        e.Handled = True
        e.SuppressKeyPress = True
    End Sub

    ''' <summary>勾選框：沒勾或勾了部分 → 整個勾選；整個勾選 → 取消。</summary>
    Private Sub ToggleNode(t As TreeNode)
        Dim node = NodeOf(t)
        If node Is Nothing OrElse Not node.Checkable Then Return
        SetChecked(t, StateOf(t) <> StateAll)
        RefreshStates(t)
        If _shown IsNot Nothing AndAlso (_shown Is t OrElse IsUnder(_shown, t)) Then SyncListChecks()
        UpdateSummary()
    End Sub

    Private Shared Function IsUnder(t As TreeNode, ancestor As TreeNode) As Boolean
        Dim p = t.Parent
        While p IsNot Nothing
            If p Is ancestor Then Return True
            p = p.Parent
        End While
        Return False
    End Function

    Private Sub SetChecked(t As TreeNode, check As Boolean)
        Dim node = NodeOf(t)
        If node Is Nothing OrElse Not node.Checkable Then Return
        If node.Photos IsNot Nothing Then
            _picked.Remove(node)
            If check AndAlso PhotosOf(node).Count > 0 Then _whole.Add(node) Else _whole.Remove(node)
        Else
            EnsureChildren(t)
            For Each c As TreeNode In t.Nodes
                SetChecked(c, check)
            Next
        End If
    End Sub

#End Region

#Region "照片清單"

    Private Sub ShowNode(t As TreeNode)
        _shown = t
        Dim node = NodeOf(t)
        _suppressChecks = True
        _list.BeginUpdate()
        _list.Clear()
        If node?.Photos IsNot Nothing Then
            For Each photo In PhotosOf(node)
                _list.AddItem(photo, 0, Not _pickFolder, IsPicked(node, photo), "").ToolTipTitle = IO.Path.GetFileName(photo)
            Next
        End If
        _list.EndUpdate()
        _suppressChecks = False
        _allButton.Enabled = node?.Photos IsNot Nothing AndAlso _list.Count > 0
        _noneButton.Enabled = _allButton.Enabled
        If _pickFolder Then _okButton.Enabled = node?.Folder IsNot Nothing
        UpdateHeader()
    End Sub

    Private Function IsPicked(node As PhotoSourceNode, photo As String) As Boolean
        If _whole.Contains(node) Then Return True
        Dim picked As HashSet(Of String) = Nothing
        Return _picked.TryGetValue(node, picked) AndAlso picked.Contains(photo)
    End Function

    Private Sub UpdateHeader()
        Dim node = NodeOf(_shown)
        If node Is Nothing Then
            _header.Text = ""
        ElseIf node.Photos Is Nothing Then
            _header.Text = node.Text & If(_pickFolder, "", "（勾選左邊的勾選框加入全部；點下層的相簿挑選照片）")
        ElseIf _pickFolder Then
            _header.Text = $"{node.Text}（{_list.Count} 張）"
        Else
            Dim picked = If(_whole.Contains(node), _list.Count, If(_picked.ContainsKey(node), _picked(node).Count, 0))
            _header.Text = $"{node.Text}（已選 {picked} / {_list.Count}）"
        End If
    End Sub

    ''' <summary>清單的勾選跟著目前的選取（整本勾選或取消之後）。</summary>
    Private Sub SyncListChecks()
        Dim node = NodeOf(_shown)
        If node?.Photos Is Nothing Then Return
        _suppressChecks = True
        For i = 0 To _list.Count - 1
            Dim item = _list.Item(i)
            Dim want = IsPicked(node, item.FileName)
            If item.Checked <> want Then item.Checked = want
        Next
        _suppressChecks = False
        UpdateHeader()
    End Sub

    ''' <summary>目前清單全選或全不選。</summary>
    Private Sub CheckShown(check As Boolean)
        Dim node = NodeOf(_shown)
        If node?.Photos Is Nothing Then Return
        _picked.Remove(node)
        _whole.Remove(node)
        If check Then
            If node.Checkable Then
                _whole.Add(node)
            Else
                _picked(node) = New HashSet(Of String)(PhotosOf(node), StringComparer.OrdinalIgnoreCase)
            End If
        End If
        SyncListChecks()
        RefreshStates(_shown)
        UpdateSummary()
    End Sub

    Private Sub OnItemChecked(item As Aqua.MediaItem)
        If _suppressChecks Then Return
        Dim node = NodeOf(_shown)
        If node?.Photos Is Nothing Then Return
        Dim all = PhotosOf(node)
        Dim picked As HashSet(Of String) = Nothing
        If _whole.Remove(node) Then
            picked = New HashSet(Of String)(all, StringComparer.OrdinalIgnoreCase)
            _picked(node) = picked
        ElseIf Not _picked.TryGetValue(node, picked) Then
            picked = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            _picked(node) = picked
        End If
        If item.Checked Then picked.Add(item.FileName) Else picked.Remove(item.FileName)

        ' 全部勾了 = 整本；一張都沒有 = 沒選
        If picked.Count = 0 Then
            _picked.Remove(node)
        ElseIf node.Checkable AndAlso picked.Count >= all.Count AndAlso all.All(Function(p) picked.Contains(p)) Then
            _picked.Remove(node)
            _whole.Add(node)
        End If
        RefreshStates(_shown)
        UpdateHeader()
        UpdateSummary()
    End Sub

    Private Sub OnItemDoubleClick(index As Integer)
        If _pickFolder OrElse index < 0 OrElse index >= _list.Count Then Return
        _list.Item(index).Checked = Not _list.Item(index).Checked
    End Sub

    ''' <summary>空白鍵：反白的照片一起勾選（或一起取消）。</summary>
    Private Sub OnItemKeyDown(index As Integer, e As KeyEventArgs)
        If _pickFolder OrElse e.KeyCode <> Keys.Space Then Return
        Dim targets = _list.SelectedIndices
        If targets.Count = 0 AndAlso index >= 0 Then targets = New List(Of Integer) From {index}
        If targets.Count = 0 Then Return
        Dim check = Not _list.Item(targets(0)).Checked
        For Each i In targets
            _list.Item(i).Checked = check
        Next
        e.Handled = True
    End Sub

#End Region

#Region "結果"

    ''' <summary>勾選的照片，依樹的順序（已讀的節點），不重複。</summary>
    Private Function CollectSelected() As List(Of String)
        Dim result As New List(Of String)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim visit As Action(Of TreeNodeCollection) = Nothing
        visit = Sub(nodes)
                    For Each t As TreeNode In nodes
                        Dim node = NodeOf(t)
                        If node Is Nothing Then Continue For
                        If node.Photos IsNot Nothing Then
                            Dim picked As HashSet(Of String) = Nothing
                            Dim whole = _whole.Contains(node)
                            If whole OrElse _picked.TryGetValue(node, picked) Then
                                For Each p In PhotosOf(node)
                                    If (whole OrElse picked.Contains(p)) AndAlso seen.Add(p) Then result.Add(p)
                                Next
                            End If
                        End If
                        visit(t.Nodes)
                    Next
                End Sub
        visit(_tree.Nodes)
        Return result
    End Function

    Private Sub UpdateSummary()
        If _pickFolder Then
            _summary.Text = ""
            Return
        End If
        Dim count = CollectSelected().Count
        _summary.Text = If(count = 0, "還沒有勾選照片", $"已選 {count} 張") &
                        If(_limit > 0 AndAlso count > _limit, $"（最多 {_limit} 張，超過的不會加入）", "")
        _okButton.Enabled = count > 0
    End Sub

    Private Sub OnOkClick(sender As Object, e As EventArgs)
        If _pickFolder Then
            SelectedFolder = NodeOf(_shown)?.Folder
            If SelectedFolder Is Nothing Then Return
        Else
            SelectedPhotos.Clear()
            SelectedPhotos.AddRange(CollectSelected())
            If SelectedPhotos.Count = 0 Then Return
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

#End Region

    ''' <summary>讀取相簿時顯示等待游標。</summary>
    Private NotInheritable Class WaitCursorScope
        Implements IDisposable

        Private ReadOnly _owner As Control
        Private ReadOnly _previous As Cursor

        Public Sub New(owner As Control)
            _owner = owner
            _previous = owner.Cursor
            owner.Cursor = Cursors.WaitCursor
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _owner.Cursor = _previous
        End Sub
    End Class
End Class

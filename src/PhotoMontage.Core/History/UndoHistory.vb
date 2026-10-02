''' <summary>
''' 以設計快照實作的復原／重做。每次變更「之前」呼叫 <see cref="Record"/>；
''' 連續操作（拖曳、拉滑桿、打字）傳入相同的 coalesceKey，短時間內只記一筆。
''' </summary>
Public Class UndoHistory

    Public Const DefaultCapacity As Integer = 100

    Private ReadOnly _undo As New List(Of String)
    Private ReadOnly _redo As New List(Of String)
    Private ReadOnly _capacity As Integer
    Private ReadOnly _clock As Func(Of Date)
    Private _lastKey As String
    Private _lastTime As Date

    ''' <summary>相同 coalesceKey 的連續變更在此時間內合併。</summary>
    Public Shared ReadOnly CoalesceWindow As TimeSpan = TimeSpan.FromSeconds(1.5)

    Public Sub New(Optional capacity As Integer = DefaultCapacity, Optional clock As Func(Of Date) = Nothing)
        If capacity < 1 Then Throw New ArgumentOutOfRangeException(NameOf(capacity))
        _capacity = capacity
        _clock = If(clock, Function() Date.UtcNow)
    End Sub

    Public ReadOnly Property CanUndo As Boolean
        Get
            Return _undo.Count > 0
        End Get
    End Property

    Public ReadOnly Property CanRedo As Boolean
        Get
            Return _redo.Count > 0
        End Get
    End Property

    ''' <summary>復原／重做狀態改變。</summary>
    Public Event Changed As EventHandler

    ''' <summary>在變更之前記錄目前狀態。</summary>
    Public Sub Record(project As MontageProject, Optional coalesceKey As String = Nothing)
        Dim now = _clock()
        If coalesceKey IsNot Nothing AndAlso coalesceKey = _lastKey AndAlso now - _lastTime < CoalesceWindow Then
            _lastTime = now
            Return
        End If

        Dim snapshot = DesignState.Capture(project).ToJson()
        _lastKey = coalesceKey
        _lastTime = now
        If _undo.Count > 0 AndAlso _undo(_undo.Count - 1) = snapshot Then Return

        _undo.Add(snapshot)
        If _undo.Count > _capacity Then _undo.RemoveAt(0)
        _redo.Clear()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ''' <summary>回到上一個不同的狀態。沒有可復原的變更時回傳 False。</summary>
    Public Function Undo(project As MontageProject) As Boolean
        Return Move(project, _undo, _redo)
    End Function

    Public Function Redo(project As MontageProject) As Boolean
        Return Move(project, _redo, _undo)
    End Function

    Public Sub Clear()
        _undo.Clear()
        _redo.Clear()
        _lastKey = Nothing
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    Private Function Move(project As MontageProject, source As List(Of String), target As List(Of String)) As Boolean
        Dim current = DesignState.Capture(project).ToJson()
        ' 略過與目前相同的快照（例如點了一下但沒有真的拖動）
        While source.Count > 0 AndAlso source(source.Count - 1) = current
            source.RemoveAt(source.Count - 1)
        End While
        If source.Count = 0 Then
            RaiseEvent Changed(Me, EventArgs.Empty)
            Return False
        End If

        Dim snapshot = source(source.Count - 1)
        source.RemoveAt(source.Count - 1)
        target.Add(current)
        DesignState.FromJson(snapshot).ApplyTo(project)
        _lastKey = Nothing
        RaiseEvent Changed(Me, EventArgs.Empty)
        Return True
    End Function

End Class

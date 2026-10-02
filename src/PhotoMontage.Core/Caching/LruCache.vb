''' <summary>依容量預算淘汰最久未使用項目的快取。執行緒安全。</summary>
Public NotInheritable Class LruCache(Of TKey, TValue)

    Private ReadOnly _budget As Long
    Private ReadOnly _sizeOf As Func(Of TValue, Long)
    Private ReadOnly _map As New Dictionary(Of TKey, LinkedListNode(Of Entry))
    Private ReadOnly _order As New LinkedList(Of Entry) ' First = 最近使用
    Private ReadOnly _lock As New Object()
    Private _currentSize As Long

    Private Structure Entry
        Public Key As TKey
        Public Value As TValue
        Public Size As Long
    End Structure

    Public Sub New(budget As Long, sizeOf As Func(Of TValue, Long))
        If budget <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(budget))
        If sizeOf Is Nothing Then Throw New ArgumentNullException(NameOf(sizeOf))
        _budget = budget
        _sizeOf = sizeOf
    End Sub

    Public ReadOnly Property Budget As Long
        Get
            Return _budget
        End Get
    End Property

    Public ReadOnly Property CurrentSize As Long
        Get
            SyncLock _lock
                Return _currentSize
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Count As Integer
        Get
            SyncLock _lock
                Return _map.Count
            End SyncLock
        End Get
    End Property

    Public Function TryGet(key As TKey, ByRef value As TValue) As Boolean
        SyncLock _lock
            Dim node As LinkedListNode(Of Entry) = Nothing
            If Not _map.TryGetValue(key, node) Then
                value = Nothing
                Return False
            End If
            _order.Remove(node)
            _order.AddFirst(node)
            value = node.Value.Value
            Return True
        End SyncLock
    End Function

    ''' <summary>加入或取代項目。單一項目超過整體預算時不會存入。</summary>
    Public Sub Add(key As TKey, value As TValue)
        Dim size = _sizeOf(value)
        SyncLock _lock
            RemoveCore(key)
            If size > _budget Then Return

            Dim node = _order.AddFirst(New Entry With {.Key = key, .Value = value, .Size = size})
            _map(key) = node
            _currentSize += size

            While _currentSize > _budget
                RemoveCore(_order.Last.Value.Key)
            End While
        End SyncLock
    End Sub

    Public Function Remove(key As TKey) As Boolean
        SyncLock _lock
            Return RemoveCore(key)
        End SyncLock
    End Function

    Public Sub Clear()
        SyncLock _lock
            _map.Clear()
            _order.Clear()
            _currentSize = 0
        End SyncLock
    End Sub

    Private Function RemoveCore(key As TKey) As Boolean
        Dim node As LinkedListNode(Of Entry) = Nothing
        If Not _map.TryGetValue(key, node) Then Return False
        _map.Remove(key)
        _order.Remove(node)
        _currentSize -= node.Value.Size
        Return True
    End Function

End Class

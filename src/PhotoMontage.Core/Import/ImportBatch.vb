''' <summary><see cref="PhotoImporter.Prepare"/> 的結果：待處理的照片與已知失敗。</summary>
Public NotInheritable Class ImportBatch
    ''' <summary>待解碼的照片，狀態為 Pending，可先顯示佔位格。</summary>
    Public ReadOnly Property Assets As IReadOnlyList(Of PhotoAsset)

    ''' <summary>不需解碼就能判定的失敗（找不到、格式不支援、超過數量上限）。</summary>
    Public ReadOnly Property Failures As IReadOnlyList(Of ImportFailure)

    Public Sub New(assets As IReadOnlyList(Of PhotoAsset), failures As IReadOnlyList(Of ImportFailure))
        Me.Assets = assets
        Me.Failures = failures
    End Sub
End Class

''' <summary>每處理完一張照片回報一次。</summary>
Public NotInheritable Class ImportProgress
    Public ReadOnly Property Completed As Integer
    Public ReadOnly Property Total As Integer
    Public ReadOnly Property Asset As PhotoAsset

    ''' <summary>成功時為已轉正的縮圖；失敗時為 Nothing。</summary>
    Public ReadOnly Property Thumbnail As DecodedImage

    ''' <summary>失敗原因；成功時為 Nothing。</summary>
    Public ReadOnly Property Failure As ImportFailure

    Public Sub New(completed As Integer, total As Integer, asset As PhotoAsset, thumbnail As DecodedImage, failure As ImportFailure)
        Me.Completed = completed
        Me.Total = total
        Me.Asset = asset
        Me.Thumbnail = thumbnail
        Me.Failure = failure
    End Sub
End Class

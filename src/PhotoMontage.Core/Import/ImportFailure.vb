Public Enum ImportFailureReason
    NotFound
    AccessDenied
    ''' <summary>被其他程式獨占或讀取時發生 I/O 錯誤。</summary>
    FileInUse
    Unsupported
    Corrupt
    HeifCodecMissing
    TooLarge
    LimitReached
End Enum

''' <summary>一張照片匯入失敗的原因。</summary>
Public NotInheritable Class ImportFailure
    Public ReadOnly Property FilePath As String
    Public ReadOnly Property Reason As ImportFailureReason

    Public Sub New(filePath As String, reason As ImportFailureReason)
        Me.FilePath = filePath
        Me.Reason = reason
    End Sub

    Public ReadOnly Property Message As String
        Get
            Return Describe(Reason)
        End Get
    End Property

    Public Shared Function Describe(reason As ImportFailureReason) As String
        Select Case reason
            Case ImportFailureReason.NotFound : Return "找不到檔案"
            Case ImportFailureReason.AccessDenied : Return "沒有讀取權限"
            Case ImportFailureReason.FileInUse : Return "檔案正被其他程式使用或無法讀取"
            Case ImportFailureReason.Unsupported : Return "不支援的檔案格式"
            Case ImportFailureReason.Corrupt : Return "檔案已損壞"
            Case ImportFailureReason.HeifCodecMissing : Return "需從 Microsoft Store 安裝「HEIF 影像延伸模組」才能讀取 HEIC 照片"
            Case ImportFailureReason.TooLarge : Return "照片解析度過高（超過 2 億像素）"
            Case ImportFailureReason.LimitReached : Return "已達照片數量上限"
            Case Else : Return reason.ToString()
        End Select
    End Function

    Public Overrides Function ToString() As String
        Return $"{IO.Path.GetFileName(FilePath)}：{Message}"
    End Function
End Class

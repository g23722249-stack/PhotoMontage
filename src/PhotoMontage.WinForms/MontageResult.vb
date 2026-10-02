''' <summary>編輯器關閉後的結果。</summary>
Public NotInheritable Class MontageResult
    ''' <summary>是否已成功匯出作品。</summary>
    Public ReadOnly Property Success As Boolean

    ''' <summary>匯出檔案的完整路徑；未匯出時為 Nothing。</summary>
    Public ReadOnly Property OutputPath As String

    Private Sub New(success As Boolean, outputPath As String)
        Me.Success = success
        Me.OutputPath = outputPath
    End Sub

    Public Shared ReadOnly Cancelled As New MontageResult(False, Nothing)

    Public Shared Function Exported(outputPath As String) As MontageResult
        Return New MontageResult(True, outputPath)
    End Function
End Class

''' <summary><see cref="MontageEditorControl.Exported"/> 事件資料。</summary>
Public Class MontageExportedEventArgs
    Inherits EventArgs

    Public ReadOnly Property OutputPath As String

    Public Sub New(outputPath As String)
        Me.OutputPath = outputPath
    End Sub
End Class

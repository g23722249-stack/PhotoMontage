Imports System.Windows.Forms

''' <summary>蒙太奇編輯器的進入點。宿主程式（如 iPhoto.Net）與獨立執行檔都透過這裡開啟。</summary>
Public Module MontageEditor

    ''' <summary>以強制回應對話框開啟編輯器，關閉後回傳結果。</summary>
    ''' <param name="owner">擁有者視窗；獨立執行時可傳 Nothing。</param>
    Public Function ShowDialog(owner As IWin32Window, options As MontageOptions) As MontageResult
        If options Is Nothing Then options = New MontageOptions()

        Using form As New MontageEditorForm(options)
            form.ShowDialog(owner)
            Return form.Result
        End Using
    End Function

End Module

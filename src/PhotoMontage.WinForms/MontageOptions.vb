Imports PhotoMontage.Core

''' <summary>開啟蒙太奇編輯器時的參數。</summary>
Public Class MontageOptions
    ''' <summary>預先載入的照片完整路徑（例如宿主目前選取的照片）。</summary>
    Public Property InitialPhotos As IList(Of String) = New List(Of String)

    ''' <summary>匯出對話框預設的資料夾；空白則用「圖片」資料夾。</summary>
    Public Property DefaultExportFolder As String

    Public Property Mode As MontageMode = MontageMode.Collage

    ''' <summary>編輯器視窗標題；空白則用預設標題。</summary>
    Public Property Title As String

    ''' <summary>
    ''' 匯出成功後自動關閉編輯器，讓 <see cref="MontageEditor.ShowDialog"/> 立即回傳結果。
    ''' 宿主程式（例如要把作品加回相簿）通常設為 True。
    ''' </summary>
    Public Property CloseAfterExport As Boolean

    ''' <summary>匯出成功後顯示完成訊息，並可開啟檔案所在資料夾。</summary>
    Public Property ShowExportCompletedMessage As Boolean = True

    ''' <summary>Aqua 控制項的主題色；宿主可傳入自己的設定讓外觀一致。</summary>
    Public Property AquaColor As Aqua.ColorConstants = Aqua.ColorConstants.Blue
End Class

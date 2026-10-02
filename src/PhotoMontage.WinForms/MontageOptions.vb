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
End Class

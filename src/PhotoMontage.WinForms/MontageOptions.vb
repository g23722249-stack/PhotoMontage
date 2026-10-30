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

    ''' <summary>編輯器視窗開啟時最大化（還原後為 1200 × 800）。</summary>
    Public Property StartMaximized As Boolean = True

    ''' <summary>
    ''' 「選取照片」畫面的資料來源（宿主的相簿，或 <see cref="FolderPhotoSource"/>）。有設定時「選取照片…」
    ''' 開啟和相簿一樣的畫面，「加入檔案…」用檔案對話框；沒有設定時是「加入照片…」與「加入資料夾…」。
    ''' 沒有預先載入照片時，編輯器開啟後會直接顯示這個畫面。
    ''' </summary>
    Public Property PhotoSource As IPhotoSource

    ''' <summary>
    ''' 匯出時可以從 <see cref="PhotoSource"/> 選擇存放位置（有 <see cref="PhotoSourceNode.Folder"/> 的節點）；
    ''' 這是那個按鈕的文字（例如「選相簿…」）。空白則沒有這個按鈕。
    ''' </summary>
    Public Property ExportFolderPickerText As String

    ''' <summary>Aqua 控制項的主題色；宿主可傳入自己的設定讓外觀一致。</summary>
    Public Property AquaColor As Aqua.ColorConstants = Aqua.ColorConstants.Blue
End Class

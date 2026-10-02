Imports System.Drawing

Public Enum PhotoStatus
    ''' <summary>已加入，尚未讀取。</summary>
    Pending
    ''' <summary>已讀取資訊並產生縮圖。</summary>
    Ready
    ''' <summary>無法匯入，原因見 <see cref="PhotoAsset.FailureReason"/>。</summary>
    Failed
End Enum

''' <summary>專案中的一張來源照片。編輯時只用縮圖，匯出時才讀原圖。</summary>
Public Class PhotoAsset
    Public Property Id As String = Guid.NewGuid().ToString("N")

    ''' <summary>原圖完整路徑。</summary>
    Public Property FilePath As String

    Public Property Status As PhotoStatus = PhotoStatus.Pending

    Public Property FailureReason As ImportFailureReason?

    ''' <summary>已套用 EXIF 方向後的原圖尺寸；尚未讀取時為 Size.Empty。</summary>
    Public Property PixelSize As Size = Size.Empty

    Public Property Orientation As ExifOrientation = ExifOrientation.Normal

    ''' <summary>EXIF 拍攝時間；沒有時為 Nothing。</summary>
    Public Property DateTaken As Date?

    Public Property FileSize As Long

    Public Property LastWriteTimeUtc As Date

    ''' <summary>平均色（馬賽克配對用）；尚未分析時為 Nothing。</summary>
    Public Property AverageColor As Color?

    ''' <summary><see cref="ThumbnailCache"/> 的鍵；尚未匯入時為 Nothing。</summary>
    Public Property ThumbnailKey As String

    Public Sub New()
    End Sub

    Public Sub New(filePath As String)
        Me.FilePath = filePath
    End Sub

    ''' <summary>寬 / 高；尺寸未知時視為 1（正方形）。</summary>
    Public ReadOnly Property AspectRatio As Double
        Get
            If PixelSize.Width <= 0 OrElse PixelSize.Height <= 0 Then Return 1.0
            Return PixelSize.Width / PixelSize.Height
        End Get
    End Property

    Public ReadOnly Property FileName As String
        Get
            Return IO.Path.GetFileName(FilePath)
        End Get
    End Property
End Class

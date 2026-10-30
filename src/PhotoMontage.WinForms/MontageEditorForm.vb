Imports System.Drawing
Imports System.Windows.Forms

''' <summary>包住 <see cref="MontageEditorControl"/> 的 Aqua 視窗（可調整大小）。</summary>
Friend Class MontageEditorForm
    Inherits Aqua.AquaForm

    ''' <summary>Aqua 標題列高度（AquaForm 內部固定為 23 像素）與右下角調整大小把手的大小。</summary>
    Friend Const AquaTitleBarHeight As Integer = 23
    Private Const AquaResizeGripSize As Integer = 16

    Private ReadOnly _editor As MontageEditorControl
    Private ReadOnly _initialPhotos As IList(Of String)
    Private ReadOnly _closeAfterExport As Boolean
    Private ReadOnly _photoSource As IPhotoSource
    Private ReadOnly _startMaximized As Boolean

    Public Property Result As MontageResult = MontageResult.Cancelled

    Public Sub New(options As MontageOptions)
        Text = If(String.IsNullOrWhiteSpace(options.Title), "蒙太奇相片", options.Title)
        TitleFont = New Font("Microsoft JhengHei UI", 10.0F, FontStyle.Bold)
        WindowBorderStyle = Aqua.FormBorderStyle.Sizable
        AutoScaleMode = AutoScaleMode.Dpi
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(800, 600)
        Size = New Size(1200, 800)
        KeyPreview = True
        _startMaximized = options.StartMaximized

        _initialPhotos = options.InitialPhotos
        _closeAfterExport = options.CloseAfterExport
        _photoSource = options.PhotoSource
        _editor = New MontageEditorControl() With {.Dock = DockStyle.Fill}
        _editor.Initialize(options)
        AddHandler _editor.Exported, AddressOf OnExported
        Controls.Add(_editor)
    End Sub

    ''' <summary>
    ''' 內容放在 Aqua 標題列下方，並留出右下角的調整大小把手。在自動縮放之後設定，確保是實際像素。
    ''' 最大化在這裡才做：視窗已建立、最大化範圍（工作區域）已設定。在建構時就設 WindowState，
    ''' 視窗會以整個螢幕大小、偏離左上角的位置建立（宿主的對話框尤其明顯），範圍限制不會生效。
    ''' </summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        UpdateMaximizedBounds()
        MyBase.OnLoad(e)
        Padding = New Padding(1, AquaTitleBarHeight, 1, AquaResizeGripSize)
        If _startMaximized Then WindowState = FormWindowState.Maximized
    End Sub

    ''' <summary>移到別的螢幕時，最大化範圍跟著換成該螢幕。</summary>
    Protected Overrides Sub OnLocationChanged(e As EventArgs)
        MyBase.OnLocationChanged(e)
        If WindowState = FormWindowState.Normal Then UpdateMaximizedBounds()
    End Sub

    ''' <summary>
    ''' Aqua 視窗沒有系統邊框，最大化時預設會蓋住工作列；限制在目前螢幕的工作區域內。
    ''' 最大化範圍以該螢幕的左上角為原點。
    ''' </summary>
    Private Sub UpdateMaximizedBounds()
        Dim screen = If(IsHandleCreated, System.Windows.Forms.Screen.FromHandle(Handle),
                        System.Windows.Forms.Screen.FromPoint(Cursor.Position))
        Dim work = screen.WorkingArea
        MaximizedBounds = New Rectangle(work.X - screen.Bounds.X, work.Y - screen.Bounds.Y, work.Width, work.Height)
    End Sub

    ''' <summary>視窗顯示後才開始匯入，宿主一次傳入大量照片時編輯器也能立即開啟。</summary>
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If (_initialPhotos Is Nothing OrElse _initialPhotos.Count = 0) AndAlso _photoSource IsNot Nothing Then
            ' 沒有預先載入照片：直接選取（視窗先畫出來）
            BeginInvoke(New Action(Sub() _editor.ShowPhotoPicker()))
        Else
            _editor.AddPhotos(_initialPhotos)
        End If
    End Sub

    Private Sub OnExported(sender As Object, e As MontageExportedEventArgs)
        Result = MontageResult.Exported(e.OutputPath)
        If _closeAfterExport Then
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub
End Class

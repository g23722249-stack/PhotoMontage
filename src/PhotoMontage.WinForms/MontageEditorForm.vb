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

        _initialPhotos = options.InitialPhotos
        _closeAfterExport = options.CloseAfterExport
        _editor = New MontageEditorControl() With {.Dock = DockStyle.Fill}
        _editor.Initialize(options)
        AddHandler _editor.Exported, AddressOf OnExported
        Controls.Add(_editor)
    End Sub

    ''' <summary>內容放在 Aqua 標題列下方，並留出右下角的調整大小把手。在自動縮放之後設定，確保是實際像素。</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Padding = New Padding(1, AquaTitleBarHeight, 1, AquaResizeGripSize)
    End Sub

    ''' <summary>視窗顯示後才開始匯入，宿主一次傳入大量照片時編輯器也能立即開啟。</summary>
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _editor.AddPhotos(_initialPhotos)
    End Sub

    Private Sub OnExported(sender As Object, e As MontageExportedEventArgs)
        Result = MontageResult.Exported(e.OutputPath)
        If _closeAfterExport Then
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub
End Class

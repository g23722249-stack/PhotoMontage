Imports System.Drawing
Imports System.Windows.Forms

''' <summary>包住 <see cref="MontageEditorControl"/> 的對話框。</summary>
Friend Class MontageEditorForm
    Inherits Form

    Private ReadOnly _editor As MontageEditorControl
    Private ReadOnly _initialPhotos As IList(Of String)

    Public Property Result As MontageResult = MontageResult.Cancelled

    Public Sub New(options As MontageOptions)
        Text = If(String.IsNullOrWhiteSpace(options.Title), "蒙太奇相片", options.Title)
        AutoScaleMode = AutoScaleMode.Dpi
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(800, 600)
        Size = New Size(1200, 800)
        KeyPreview = True

        _initialPhotos = options.InitialPhotos
        _editor = New MontageEditorControl() With {.Dock = DockStyle.Fill}
        _editor.Initialize(options)
        AddHandler _editor.Exported, AddressOf OnExported
        Controls.Add(_editor)
    End Sub

    ''' <summary>視窗顯示後才開始匯入，宿主一次傳入大量照片時編輯器也能立即開啟。</summary>
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _editor.AddPhotos(_initialPhotos)
    End Sub

    Private Sub OnExported(sender As Object, e As MontageExportedEventArgs)
        Result = MontageResult.Exported(e.OutputPath)
    End Sub
End Class

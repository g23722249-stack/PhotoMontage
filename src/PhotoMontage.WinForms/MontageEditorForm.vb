Imports System.Drawing
Imports System.Windows.Forms

''' <summary>包住 <see cref="MontageEditorControl"/> 的對話框。</summary>
Friend Class MontageEditorForm
    Inherits Form

    Private ReadOnly _editor As MontageEditorControl

    Public Property Result As MontageResult = MontageResult.Cancelled

    Public Sub New(options As MontageOptions)
        Text = If(String.IsNullOrWhiteSpace(options.Title), "蒙太奇相片", options.Title)
        AutoScaleMode = AutoScaleMode.Dpi
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(800, 600)
        Size = New Size(1200, 800)
        KeyPreview = True

        _editor = New MontageEditorControl() With {.Dock = DockStyle.Fill}
        _editor.Initialize(options)
        AddHandler _editor.Exported, AddressOf OnExported
        Controls.Add(_editor)
    End Sub

    Private Sub OnExported(sender As Object, e As MontageExportedEventArgs)
        Result = MontageResult.Exported(e.OutputPath)
    End Sub
End Class

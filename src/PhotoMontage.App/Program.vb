Imports System.Windows.Forms

Friend Module Program

    ''' <summary>命令列參數視為要預先載入的照片路徑。</summary>
    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        PhotoMontage.MontageEditor.ShowDialog(Nothing, New PhotoMontage.MontageOptions With {
            .InitialPhotos = args
        })
    End Sub

End Module

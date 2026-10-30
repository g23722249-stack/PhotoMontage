Imports System.Windows.Forms

Friend Module Program

    ''' <summary>命令列參數視為要預先載入的照片路徑。</summary>
    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        ' 「選取照片」用磁碟上的資料夾，先顯示第一張照片所在的資料夾
        Dim startFolder As String = If(args.Length > 0, If(IO.Directory.Exists(args(0)), args(0), IO.Path.GetDirectoryName(args(0))), Nothing)
        PhotoMontage.MontageEditor.ShowDialog(Nothing, New PhotoMontage.MontageOptions With {
            .InitialPhotos = args,
            .PhotoSource = New PhotoMontage.FolderPhotoSource(startFolder)
        })
    End Sub

End Module

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO

''' <summary>寫出影像檔：一律先寫到同資料夾的暫存檔再改名，失敗不會留下半個檔案或覆蓋舊檔。</summary>
Friend Module ImageFileWriter

    ''' <summary>以 <paramref name="write"/> 寫入暫存檔，成功後改名為 <paramref name="path"/>。</summary>
    Public Sub WriteAtomically(path As String, write As Action(Of String))
        Dim folder = IO.Path.GetDirectoryName(IO.Path.GetFullPath(path))
        Directory.CreateDirectory(folder)
        Dim temp = IO.Path.Combine(folder, "." & Guid.NewGuid().ToString("N") & ".tmp")
        Try
            write(temp)
            File.Move(temp, path, overwrite:=True)
        Catch
            Try
                File.Delete(temp)
            Catch ex As IOException
            End Try
            Throw
        End Try
    End Sub

    ''' <summary>以 GDI+ 存成 JPEG 或 PNG。</summary>
    Public Sub SaveBitmap(image As Bitmap, settings As ExportSettings)
        WriteAtomically(settings.FilePath,
            Sub(temp)
                If settings.Format = ExportFormat.Png Then
                    image.Save(temp, ImageFormat.Png)
                Else
                    Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Jpeg.Guid)
                    Using parameters As New EncoderParameters(1)
                        parameters.Param(0) = New EncoderParameter(Encoder.Quality, CLng(Math.Max(1, Math.Min(100, settings.JpegQuality))))
                        image.Save(temp, codec, parameters)
                    End Using
                End If
            End Sub)
    End Sub

End Module

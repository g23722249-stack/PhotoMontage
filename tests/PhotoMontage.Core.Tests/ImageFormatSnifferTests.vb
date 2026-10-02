Imports System.IO
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class ImageFormatSnifferTests

    Private Shared Function Detect(ParamArray bytes As Byte()) As ImageFileFormat
        Return ImageFormatSniffer.Detect(bytes, bytes.Length)
    End Function

    Private Shared Function Ascii(value As String) As Byte()
        Return System.Text.Encoding.ASCII.GetBytes(value)
    End Function

    <TestMethod>
    Public Sub Detect_KnownFormats()
        Assert.AreEqual(ImageFileFormat.Jpeg, Detect(&HFF, &HD8, &HFF, &HE1))
        Assert.AreEqual(ImageFileFormat.Png, Detect(&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA))
        Assert.AreEqual(ImageFileFormat.Gif, Detect(Ascii("GIF89a")))
        Assert.AreEqual(ImageFileFormat.Bmp, Detect(Ascii("BM....")))
        Assert.AreEqual(ImageFileFormat.Tiff, Detect(&H49, &H49, &H2A, 0))
        Assert.AreEqual(ImageFileFormat.Tiff, Detect(&H4D, &H4D, 0, &H2A))
        Assert.AreEqual(ImageFileFormat.Heif, Detect(FakeCodec.HeifHeader))
        Assert.AreEqual(ImageFileFormat.Heif, Detect({0, 0, 0, &H1C}.Select(Function(b) CByte(b)).Concat(Ascii("ftypmif1")).ToArray()))
        Assert.AreEqual(ImageFileFormat.WebP, Detect(Ascii("RIFF0000WEBPVP8 ")))
    End Sub

    <TestMethod>
    Public Sub Detect_UnknownOrTooShort()
        Assert.AreEqual(ImageFileFormat.Unknown, Detect(Ascii("hello world")))
        Assert.AreEqual(ImageFileFormat.Unknown, Detect(&HFF, &HD8))
        Assert.AreEqual(ImageFileFormat.Unknown, Detect())
        ' MP4 也是 ftyp，但不是 HEIF
        Assert.AreEqual(ImageFileFormat.Unknown, Detect({0, 0, 0, &H18}.Select(Function(b) CByte(b)).Concat(Ascii("ftypisom")).ToArray()))
    End Sub

    <TestMethod>
    Public Sub Detect_Stream_RestoresPosition()
        Using ms As New MemoryStream({CByte(0), CByte(&HFF), CByte(&HD8), CByte(&HFF), CByte(0)})
            ms.Position = 1
            Assert.AreEqual(ImageFileFormat.Jpeg, ImageFormatSniffer.Detect(ms))
            Assert.AreEqual(1L, ms.Position)
        End Using
    End Sub

    <TestMethod>
    Public Sub IsSupportedExtension_IgnoresCase()
        Assert.IsTrue(ImageFormatSniffer.IsSupportedExtension("C:\a\IMG_0001.JPG"))
        Assert.IsTrue(ImageFormatSniffer.IsSupportedExtension("photo.heic"))
        Assert.IsFalse(ImageFormatSniffer.IsSupportedExtension("movie.mov"))
        Assert.IsFalse(ImageFormatSniffer.IsSupportedExtension(""))
    End Sub

End Class

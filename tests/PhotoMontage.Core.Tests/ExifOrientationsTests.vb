Imports System.Drawing
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class ExifOrientationsTests

    ''' <summary>3×2 影像，每個像素的 R=x、G=y，方便追蹤來源位置。</summary>
    Private Shared Function Source() As DecodedImage
        Dim px(3 * 2 * 4 - 1) As Byte
        For y = 0 To 1
            For x = 0 To 2
                Dim i = (y * 3 + x) * 4
                px(i + 2) = CByte(x)
                px(i + 1) = CByte(y)
                px(i + 3) = 255
            Next
        Next
        Return New DecodedImage(3, 2, px)
    End Function

    Private Shared Function SourceOf(img As DecodedImage, x As Integer, y As Integer) As Point
        Dim c = img.GetPixel(x, y)
        Return New Point(c.R, c.G)
    End Function

    <DataTestMethod>
    <DataRow(ExifOrientation.Normal, 3, 2, 0, 0, 2, 0)>
    <DataRow(ExifOrientation.FlipHorizontal, 3, 2, 2, 0, 0, 0)>
    <DataRow(ExifOrientation.Rotate180, 3, 2, 2, 1, 0, 1)>
    <DataRow(ExifOrientation.FlipVertical, 3, 2, 0, 1, 2, 1)>
    <DataRow(ExifOrientation.Transpose, 2, 3, 0, 0, 0, 1)>
    <DataRow(ExifOrientation.Rotate90, 2, 3, 0, 1, 0, 0)>
    <DataRow(ExifOrientation.Transverse, 2, 3, 2, 1, 2, 0)>
    <DataRow(ExifOrientation.Rotate270, 2, 3, 2, 0, 2, 1)>
    Public Sub Apply_MapsCornersCorrectly(orientation As ExifOrientation, w As Integer, h As Integer,
                                         topLeftX As Integer, topLeftY As Integer, topRightX As Integer, topRightY As Integer)
        Dim result = ExifOrientations.Apply(Source(), orientation)

        Assert.AreEqual(w, result.Width)
        Assert.AreEqual(h, result.Height)
        Assert.AreEqual(New Point(topLeftX, topLeftY), SourceOf(result, 0, 0), "左上")
        Assert.AreEqual(New Point(topRightX, topRightY), SourceOf(result, result.Width - 1, 0), "右上")
    End Sub

    <TestMethod>
    Public Sub Apply_Rotate90_SourceTopLeftEndsAtTopRight()
        ' 手機直拍最常見的情況：感光元件橫向，EXIF=6
        Dim result = ExifOrientations.Apply(Source(), ExifOrientation.Rotate90)
        Assert.AreEqual(New Point(0, 0), SourceOf(result, 1, 0))
        Assert.AreEqual(New Point(2, 0), SourceOf(result, 1, 2))
    End Sub

    <TestMethod>
    Public Sub Apply_IsBijective()
        For Each o As ExifOrientation In [Enum].GetValues(GetType(ExifOrientation))
            Dim result = ExifOrientations.Apply(Source(), o)
            Dim seen As New HashSet(Of Point)
            For y = 0 To result.Height - 1
                For x = 0 To result.Width - 1
                    Assert.IsTrue(seen.Add(SourceOf(result, x, y)), $"{o} 有重複的來源像素")
                Next
            Next
            Assert.AreEqual(6, seen.Count)
        Next
    End Sub

    <TestMethod>
    Public Sub FromValue_InvalidBecomesNormal()
        Assert.AreEqual(ExifOrientation.Normal, ExifOrientations.FromValue(0))
        Assert.AreEqual(ExifOrientation.Normal, ExifOrientations.FromValue(9))
        Assert.AreEqual(ExifOrientation.Rotate90, ExifOrientations.FromValue(6))
    End Sub

    <TestMethod>
    Public Sub GetOrientedSize_SwapsForRotations()
        Assert.AreEqual(New Size(4000, 3000), ExifOrientations.GetOrientedSize(4000, 3000, ExifOrientation.Rotate180))
        Assert.AreEqual(New Size(3000, 4000), ExifOrientations.GetOrientedSize(4000, 3000, ExifOrientation.Rotate90))
    End Sub

End Class

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports PhotoMontage.Core

<TestClass>
Public Class PhotoSortingTests

    <TestMethod>
    Public Sub NaturalCompare_OrdersNumbersByValue()
        Dim names = {"IMG_10.jpg", "img_2.jpg", "IMG_1.jpg", "IMG_002.jpg", "a.jpg"}
        Dim sorted = names.OrderBy(Function(n) n, Comparer(Of String).Create(AddressOf PhotoSorting.NaturalCompare)).ToArray()
        CollectionAssert.AreEqual({"a.jpg", "IMG_1.jpg", "img_2.jpg", "IMG_002.jpg", "IMG_10.jpg"}, sorted)
    End Sub

    <TestMethod>
    Public Sub ByDateTaken_PutsUndatedLastAndIsStable()
        Dim noDate1 As New PhotoAsset("C:\b.jpg")
        Dim noDate2 As New PhotoAsset("C:\a.jpg")
        Dim late As New PhotoAsset("C:\z.jpg") With {.DateTaken = New Date(2024, 2, 1)}
        Dim early As New PhotoAsset("C:\y.jpg") With {.DateTaken = New Date(2024, 1, 1)}

        Dim sorted = PhotoSorting.StableSort({noDate1, late, noDate2, early}, AddressOf PhotoSorting.ByDateTaken)

        CollectionAssert.AreEqual({early, late, noDate2, noDate1}, sorted)
    End Sub

End Class

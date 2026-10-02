Imports System.Drawing

Public Enum ExportFormat
    Jpeg = 0
    Png = 1
End Enum

''' <summary>匯出設定。輸出尺寸依畫布比例與長邊計算。</summary>
Public Class ExportSettings
    ''' <summary>輸出長邊（像素）。</summary>
    Public Property LongEdge As Integer = 2048

    Public Property Format As ExportFormat = ExportFormat.Jpeg

    ''' <summary>JPEG 品質 1～100。</summary>
    Public Property JpegQuality As Integer = 92

    ''' <summary>寫入檔案的解析度資訊（列印用 300）。</summary>
    Public Property Dpi As Integer = 96

    ''' <summary>輸出檔案完整路徑。</summary>
    Public Property FilePath As String

    Public Function GetOutputSize(canvasAspect As Double) As Size
        Return ExportPlanner.ComputeOutputSize(canvasAspect, LongEdge)
    End Function
End Class

''' <summary>匯出解析度選項。</summary>
Public NotInheritable Class ExportPreset
    Public ReadOnly Property Name As String
    ''' <summary>長邊像素；0 表示自訂。</summary>
    Public ReadOnly Property LongEdge As Integer
    Public ReadOnly Property Dpi As Integer

    Public Sub New(name As String, longEdge As Integer, dpi As Integer)
        Me.Name = name
        Me.LongEdge = longEdge
        Me.Dpi = dpi
    End Sub

    Public ReadOnly Property IsCustom As Boolean
        Get
            Return LongEdge = 0
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Name
    End Function

    Public Shared ReadOnly All As IReadOnlyList(Of ExportPreset) = New List(Of ExportPreset) From {
        New ExportPreset("社群分享（長邊 1080）", 1080, 96),
        New ExportPreset("高畫質（長邊 2048）", 2048, 96),
        New ExportPreset("4K（長邊 3840）", 3840, 96),
        New ExportPreset("列印 4×6 吋（300 dpi）", 1800, 300),
        New ExportPreset("列印 A4（300 dpi）", 3508, 300),
        New ExportPreset("自訂…", 0, 96)
    }.AsReadOnly()
End Class

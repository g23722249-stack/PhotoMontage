Imports System.Drawing
Imports System.Text.Json

''' <summary>
''' 作品「設計」部分的可序列化快照：畫布、背景、版型、格子與取景、文字圖層。
''' 不含照片清單本身（照片的匯入與移除由縮圖清單管理）。
''' 用於復原／重做，之後也可作為專案存檔格式的基礎。
''' </summary>
Public NotInheritable Class DesignState
    Public Property CanvasWidth As Integer
    Public Property CanvasHeight As Integer
    Public Property BackgroundArgb As Integer
    Public Property BackgroundImagePath As String
    Public Property TemplateId As String
    Public Property Gap As Single
    Public Property CornerRadius As Single
    Public Property Cells As New List(Of CellState)
    Public Property Texts As New List(Of TextState)

    Public NotInheritable Class CellState
        Public Property X As Single
        Public Property Y As Single
        Public Property Width As Single
        Public Property Height As Single
        Public Property PhotoId As String
        Public Property OffsetX As Single
        Public Property OffsetY As Single
        Public Property Scale As Single
    End Class

    Public NotInheritable Class TextState
        Public Property Id As String
        Public Property Text As String
        Public Property FontFamily As String
        Public Property FontSize As Single
        Public Property Bold As Boolean
        Public Property Italic As Boolean
        Public Property ColorArgb As Integer
        Public Property Alignment As Integer
        Public Property OutlineWidth As Single
        Public Property OutlineArgb As Integer
        Public Property ShadowEnabled As Boolean
        Public Property ShadowArgb As Integer
        Public Property ShadowOffsetX As Single
        Public Property ShadowOffsetY As Single
        Public Property X As Single
        Public Property Y As Single
        Public Property Rotation As Single
    End Class

    Public Shared Function Capture(project As MontageProject) As DesignState
        Return New DesignState With {
            .CanvasWidth = project.CanvasSize.Width,
            .CanvasHeight = project.CanvasSize.Height,
            .BackgroundArgb = project.BackgroundColor.ToArgb(),
            .BackgroundImagePath = project.BackgroundImagePath,
            .TemplateId = project.Collage.TemplateId,
            .Gap = project.Collage.Gap,
            .CornerRadius = project.Collage.CornerRadius,
            .Cells = project.Collage.Cells.Select(Function(c) New CellState With {
                .X = c.Bounds.X, .Y = c.Bounds.Y, .Width = c.Bounds.Width, .Height = c.Bounds.Height,
                .PhotoId = c.PhotoId, .OffsetX = c.Crop.OffsetX, .OffsetY = c.Crop.OffsetY, .Scale = c.Crop.Scale}).ToList(),
            .Texts = project.Texts.Select(Function(t) New TextState With {
                .Id = t.Id, .Text = t.Text, .FontFamily = t.FontFamily, .FontSize = t.FontSize,
                .Bold = t.Bold, .Italic = t.Italic, .ColorArgb = t.Color.ToArgb(), .Alignment = CInt(t.Alignment),
                .OutlineWidth = t.OutlineWidth, .OutlineArgb = t.OutlineColor.ToArgb(),
                .ShadowEnabled = t.ShadowEnabled, .ShadowArgb = t.ShadowColor.ToArgb(),
                .ShadowOffsetX = t.ShadowOffset.X, .ShadowOffsetY = t.ShadowOffset.Y,
                .X = t.Position.X, .Y = t.Position.Y, .Rotation = t.Rotation}).ToList()
        }
    End Function

    ''' <summary>把快照套回專案（照片清單不變）。</summary>
    Public Sub ApplyTo(project As MontageProject)
        project.CanvasSize = New Size(CanvasWidth, CanvasHeight)
        project.BackgroundColor = Color.FromArgb(BackgroundArgb)
        project.BackgroundImagePath = BackgroundImagePath
        project.Collage.TemplateId = TemplateId
        project.Collage.Gap = Gap
        project.Collage.CornerRadius = CornerRadius
        project.Collage.Cells = Cells.Select(Function(c) New Cell With {
            .Bounds = New RectangleF(c.X, c.Y, c.Width, c.Height),
            .PhotoId = c.PhotoId,
            .Crop = New CropInfo With {.OffsetX = c.OffsetX, .OffsetY = c.OffsetY, .Scale = c.Scale}}).ToList()
        project.Texts.Clear()
        project.Texts.AddRange(Texts.Select(Function(t) New TextLayer With {
            .Id = t.Id, .Text = t.Text, .FontFamily = t.FontFamily, .FontSize = t.FontSize,
            .Bold = t.Bold, .Italic = t.Italic, .Color = Color.FromArgb(t.ColorArgb),
            .Alignment = CType(t.Alignment, StringAlignment),
            .OutlineWidth = t.OutlineWidth, .OutlineColor = Color.FromArgb(t.OutlineArgb),
            .ShadowEnabled = t.ShadowEnabled, .ShadowColor = Color.FromArgb(t.ShadowArgb),
            .ShadowOffset = New PointF(t.ShadowOffsetX, t.ShadowOffsetY),
            .Position = New PointF(t.X, t.Y), .Rotation = t.Rotation}))
    End Sub

    Public Function ToJson() As String
        Return JsonSerializer.Serialize(Me)
    End Function

    Public Shared Function FromJson(json As String) As DesignState
        Return JsonSerializer.Deserialize(Of DesignState)(json)
    End Function
End Class

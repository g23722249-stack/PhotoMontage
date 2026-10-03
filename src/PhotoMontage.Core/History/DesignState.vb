Imports System.Drawing
Imports System.Text.Json

''' <summary>
''' 作品「設計」部分的可序列化快照：模式、畫布、背景、版型、格子與取景、馬賽克、文字圖層。
''' 不含照片清單本身（照片的匯入與移除由縮圖清單管理）。
''' 用於復原／重做，之後也可作為專案存檔格式的基礎。
''' </summary>
Public NotInheritable Class DesignState
    Public Property Mode As Integer
    Public Property CanvasWidth As Integer
    Public Property CanvasHeight As Integer
    Public Property BackgroundArgb As Integer
    Public Property BackgroundImagePath As String
    Public Property TemplateId As String
    Public Property Gap As Single
    Public Property CornerRadius As Single
    Public Property Cells As New List(Of CellState)
    Public Property CellsAdjusted As Boolean
    Public Property Texts As New List(Of TextState)
    Public Property Mosaic As New MosaicState
    Public Property Free As New FreeState

    Public NotInheritable Class FreeState
        Public Property Looseness As Single = 0.5F
        Public Property Style As Integer
        Public Property Overlap As Boolean = True
        Public Property Clockwise As Boolean = True
        Public Property Items As New List(Of FreeItemState)
    End Class

    Public NotInheritable Class FreeItemState
        Public Property Id As String
        Public Property PhotoId As String
        Public Property CenterX As Single
        Public Property CenterY As Single
        Public Property Width As Single
        Public Property InnerAspect As Single
        Public Property Rotation As Single
        Public Property Frame As Integer
        Public Property FrameWidth As Single
        Public Property Shadow As Boolean
        Public Property OffsetX As Single
        Public Property OffsetY As Single
        Public Property Scale As Single
        Public Property PhotoRotation As Integer
        Public Property Flip As Boolean
    End Class

    ''' <summary>馬賽克設定；格子結果以「素材 Id 表＋索引」儲存，避免每格重複完整 Id。</summary>
    Public NotInheritable Class MosaicState
        Public Property TargetPath As String
        Public Property Columns As Integer
        Public Property Rows As Integer
        Public Property Tint As Single
        Public Property MaxRepeat As Integer
        Public Property AvoidAdjacent As Boolean
        Public Property TileIds As New List(Of String)
        Public Property TileIndexes As New List(Of Integer)
    End Class

    Public NotInheritable Class CellState
        Public Property X As Single
        Public Property Y As Single
        Public Property Width As Single
        Public Property Height As Single
        Public Property PhotoId As String
        Public Property OffsetX As Single
        Public Property OffsetY As Single
        Public Property Scale As Single
        Public Property Rotation As Integer
        Public Property Flip As Boolean
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
        Dim m = project.Mosaic
        Dim ids As New List(Of String)
        Dim lookup As New Dictionary(Of String, Integer)
        Dim indexes As New List(Of Integer)(m.Tiles.Count)
        For Each id In m.Tiles
            If id Is Nothing Then
                indexes.Add(-1)
                Continue For
            End If
            Dim i As Integer
            If Not lookup.TryGetValue(id, i) Then
                i = ids.Count
                ids.Add(id)
                lookup(id) = i
            End If
            indexes.Add(i)
        Next

        Return New DesignState With {
            .Mode = CInt(project.Mode),
            .Mosaic = New MosaicState With {
                .TargetPath = m.TargetPath, .Columns = m.Columns, .Rows = m.Rows, .Tint = m.Tint,
                .MaxRepeat = m.MaxRepeat, .AvoidAdjacent = m.AvoidAdjacentDuplicates,
                .TileIds = ids, .TileIndexes = indexes},
            .Free = New FreeState With {
                .Looseness = project.Free.Looseness,
                .Style = CInt(project.Free.Style), .Overlap = project.Free.Overlap, .Clockwise = project.Free.Clockwise,
                .Items = project.Free.Items.Select(Function(i) New FreeItemState With {
                    .Id = i.Id, .PhotoId = i.PhotoId, .CenterX = i.CenterX, .CenterY = i.CenterY, .Width = i.Width,
                    .InnerAspect = i.InnerAspect, .Rotation = i.Rotation, .Frame = CInt(i.Frame), .FrameWidth = i.FrameWidth,
                    .Shadow = i.Shadow, .OffsetX = i.Crop.OffsetX, .OffsetY = i.Crop.OffsetY, .Scale = i.Crop.Scale,
                    .PhotoRotation = i.Crop.Rotation, .Flip = i.Crop.FlipHorizontal}).ToList()},
            .CanvasWidth = project.CanvasSize.Width,
            .CanvasHeight = project.CanvasSize.Height,
            .BackgroundArgb = project.BackgroundColor.ToArgb(),
            .BackgroundImagePath = project.BackgroundImagePath,
            .TemplateId = project.Collage.TemplateId,
            .Gap = project.Collage.Gap,
            .CornerRadius = project.Collage.CornerRadius,
            .CellsAdjusted = project.Collage.CellsAdjusted,
            .Cells = project.Collage.Cells.Select(Function(c) New CellState With {
                .X = c.Bounds.X, .Y = c.Bounds.Y, .Width = c.Bounds.Width, .Height = c.Bounds.Height,
                .PhotoId = c.PhotoId, .OffsetX = c.Crop.OffsetX, .OffsetY = c.Crop.OffsetY, .Scale = c.Crop.Scale,
                .Rotation = c.Crop.Rotation, .Flip = c.Crop.FlipHorizontal}).ToList(),
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
        project.Mode = CType(Mode, MontageMode)
        Dim fs = If(Free, New FreeState())
        project.Free = New FreeLayoutSettings With {
            .Looseness = fs.Looseness,
            .Style = CType(fs.Style, ArrangeStyle), .Overlap = fs.Overlap, .Clockwise = fs.Clockwise,
            .Items = fs.Items.Select(Function(i) New FreeItem With {
                .Id = i.Id, .PhotoId = i.PhotoId, .CenterX = i.CenterX, .CenterY = i.CenterY, .Width = i.Width,
                .InnerAspect = If(i.InnerAspect > 0, i.InnerAspect, 1.5F), .Rotation = i.Rotation, .Frame = CType(i.Frame, FrameStyle),
                .FrameWidth = i.FrameWidth, .Shadow = i.Shadow,
                .Crop = New CropInfo With {.OffsetX = i.OffsetX, .OffsetY = i.OffsetY, .Scale = If(i.Scale > 0, i.Scale, 1.0F),
                                           .Rotation = i.PhotoRotation, .FlipHorizontal = i.Flip}}).ToList()}
        Dim ms = If(Mosaic, New MosaicState())
        project.Mosaic = New MosaicSettings With {
            .TargetPath = ms.TargetPath,
            .Columns = If(ms.Columns > 0, ms.Columns, 60),
            .Rows = If(ms.Rows > 0, ms.Rows, 60),
            .Tint = ms.Tint, .MaxRepeat = ms.MaxRepeat, .AvoidAdjacentDuplicates = ms.AvoidAdjacent,
            .Tiles = ms.TileIndexes.Select(Function(i) If(i >= 0 AndAlso i < ms.TileIds.Count, ms.TileIds(i), Nothing)).ToList()}
        project.CanvasSize = New Size(CanvasWidth, CanvasHeight)
        project.BackgroundColor = Color.FromArgb(BackgroundArgb)
        project.BackgroundImagePath = BackgroundImagePath
        project.Collage.TemplateId = TemplateId
        project.Collage.Gap = Gap
        project.Collage.CornerRadius = CornerRadius
        project.Collage.CellsAdjusted = CellsAdjusted
        project.Collage.Cells = Cells.Select(Function(c) New Cell With {
            .Bounds = New RectangleF(c.X, c.Y, c.Width, c.Height),
            .PhotoId = c.PhotoId,
            .Crop = New CropInfo With {.OffsetX = c.OffsetX, .OffsetY = c.OffsetY, .Scale = c.Scale,
                                       .Rotation = c.Rotation, .FlipHorizontal = c.Flip}}).ToList()
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

Imports System.Drawing

''' <summary>把 0~1 相對座標的格子換算成實際像素位置（扣除間距）。</summary>
Public Module CellGeometry

    Private Const Epsilon As Single = 0.0001F

    ''' <summary>
    ''' 單一格子的像素位置。畫布外緣留完整間距，相鄰格子之間各留一半，
    ''' 因此所有可見間距都一樣寬。
    ''' </summary>
    ''' <param name="gap">間距，以畫布短邊的比例表示。</param>
    Public Function GetCellRect(cell As RectangleF, bounds As RectangleF, gap As Single) As RectangleF
        Dim g = Math.Max(0F, gap) * Math.Min(bounds.Width, bounds.Height)
        Dim half = g / 2

        Dim left = bounds.X + cell.Left * bounds.Width + If(cell.Left <= Epsilon, g, half)
        Dim top = bounds.Y + cell.Top * bounds.Height + If(cell.Top <= Epsilon, g, half)
        Dim right = bounds.X + cell.Right * bounds.Width - If(cell.Right >= 1 - Epsilon, g, half)
        Dim bottom = bounds.Y + cell.Bottom * bounds.Height - If(cell.Bottom >= 1 - Epsilon, g, half)

        Return RectangleF.FromLTRB(left, top, Math.Max(left, right), Math.Max(top, bottom))
    End Function

    ''' <summary>拼貼中所有格子的像素位置，順序與 <see cref="CollageSettings.Cells"/> 相同。</summary>
    Public Function GetCellRects(settings As CollageSettings, bounds As RectangleF) As List(Of RectangleF)
        Return settings.Cells.Select(Function(c) GetCellRect(c.Bounds, bounds, settings.Gap)).ToList()
    End Function

    ''' <summary>格子的圓角半徑（像素）。</summary>
    Public Function GetCornerRadius(settings As CollageSettings, cellRect As RectangleF) As Single
        Return Math.Max(0F, Math.Min(0.5F, settings.CornerRadius)) * Math.Min(cellRect.Width, cellRect.Height)
    End Function

    ''' <summary>格子在畫布上的長寬比（寬 / 高）。</summary>
    Public Function GetCellAspect(cell As RectangleF, canvasSize As SizeF) As Double
        Dim h = cell.Height * canvasSize.Height
        If h <= 0 Then Return 1.0
        Return cell.Width * canvasSize.Width / h
    End Function

End Module

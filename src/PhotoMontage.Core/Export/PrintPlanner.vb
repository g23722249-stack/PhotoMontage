Imports System.Drawing

''' <summary>作品放到紙上的方式。</summary>
Public Enum PrintFit
    ''' <summary>完整顯示整張作品，紙上可能留白。</summary>
    Fit = 0
    ''' <summary>填滿可列印範圍，超出的部分裁掉。</summary>
    Fill = 1
End Enum

Public Enum PrintOrientation
    ''' <summary>依作品比例：橫的作品印橫向。</summary>
    Auto = 0
    Portrait = 1
    Landscape = 2
End Enum

''' <summary>作品在紙上的位置。</summary>
Public Structure PrintPlacement
    ''' <summary>紙上要畫的範圍（與頁面相同單位，通常為 1/100 吋）。</summary>
    Public Destination As RectangleF
    ''' <summary>要取用的作品範圍，以 0～1 的比例表示（填滿時為置中裁切的範圍）。</summary>
    Public Source As RectangleF
End Structure

''' <summary>列印版面計算（不依賴印表機，可測試）。長度單位為 1/100 吋。</summary>
Public Module PrintPlanner

    ''' <summary>列印解析度上限；再高肉眼看不出差別，只會更慢、更耗記憶體。</summary>
    Public Const MaxPrintDpi As Integer = 300

    ''' <summary>列印用影像的長邊上限（像素）。</summary>
    Public Const MaxPrintLongEdge As Integer = 6000

    ''' <summary>邊界選項（1/100 吋）：無、窄（0.25 吋）、一般（0.5 吋）。</summary>
    Public ReadOnly MarginChoices As IReadOnlyList(Of Single) = New Single() {0, 25, 50}

    Public Function UseLandscape(orientation As PrintOrientation, canvasAspect As Double) As Boolean
        Select Case orientation
            Case PrintOrientation.Portrait
                Return False
            Case PrintOrientation.Landscape
                Return True
            Case Else
                Return canvasAspect > 1.0
        End Select
    End Function

    ''' <summary>
    ''' 可放作品的範圍：紙張扣掉邊界，再限制在印表機實際能印的範圍內。
    ''' </summary>
    ''' <param name="pageSize">紙張大小（已依方向轉好）。</param>
    ''' <param name="printable">印表機可列印範圍（頁面座標）；不知道時傳整張紙。</param>
    ''' <param name="margin">使用者選的邊界。</param>
    Public Function GetContentArea(pageSize As SizeF, printable As RectangleF, margin As Single) As RectangleF
        Dim area As New RectangleF(margin, margin, pageSize.Width - margin * 2, pageSize.Height - margin * 2)
        If printable.Width > 0 AndAlso printable.Height > 0 Then area.Intersect(printable)
        If area.Width <= 0 OrElse area.Height <= 0 Then Return RectangleF.Empty
        Return area
    End Function

    Public Function Place(area As RectangleF, canvasAspect As Double, fit As PrintFit) As PrintPlacement
        Dim result As New PrintPlacement With {.Destination = area, .Source = New RectangleF(0, 0, 1, 1)}
        If area.Width <= 0 OrElse area.Height <= 0 OrElse canvasAspect <= 0 Then Return result
        Dim areaAspect = area.Width / area.Height

        If fit = PrintFit.Fill Then
            ' 從作品中央裁出與可用範圍相同比例的區域
            If canvasAspect > areaAspect Then
                Dim w = CSng(areaAspect / canvasAspect)
                result.Source = New RectangleF((1 - w) / 2, 0, w, 1)
            Else
                Dim h = CSng(canvasAspect / areaAspect)
                result.Source = New RectangleF(0, (1 - h) / 2, 1, h)
            End If
        Else
            Dim w = area.Width
            Dim h = area.Height
            If canvasAspect > areaAspect Then h = CSng(w / canvasAspect) Else w = CSng(h * canvasAspect)
            result.Destination = New RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h)
        End If
        Return result
    End Function

    ''' <summary>
    ''' 要繪製的整張作品像素大小：讓紙上的部分達到 <paramref name="dpi"/>（上限 <see cref="MaxPrintDpi"/>），
    ''' 長邊不超過 <see cref="MaxPrintLongEdge"/>。
    ''' </summary>
    Public Function GetRenderSize(placement As PrintPlacement, canvasAspect As Double, dpi As Integer) As Size
        If dpi <= 0 OrElse dpi > MaxPrintDpi Then dpi = MaxPrintDpi
        Dim src = placement.Source
        If src.Width <= 0 OrElse src.Height <= 0 Then src = New RectangleF(0, 0, 1, 1)
        ' 紙上寬度（吋）× dpi = 裁切範圍的像素寬；換算回整張作品
        Dim fullWidth = placement.Destination.Width / 100.0 * dpi / src.Width
        Dim longEdge = CInt(Math.Round(If(canvasAspect >= 1, fullWidth, fullWidth / canvasAspect)))
        longEdge = Math.Max(64, Math.Min(MaxPrintLongEdge, longEdge))
        Return ExportPlanner.ComputeOutputSize(canvasAspect, longEdge)
    End Function

End Module

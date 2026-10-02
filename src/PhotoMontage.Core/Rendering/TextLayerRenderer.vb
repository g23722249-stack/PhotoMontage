Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text

''' <summary>文字圖層的繪製與量測。字級、位置、外框、陰影都依畫布大小等比例計算。</summary>
Public Module TextLayerRenderer

    ''' <summary>空白文字時外框的寬度（以字級為單位），讓使用者還點得到。</summary>
    Private Const EmptyWidthEm As Single = 2.0F

    ''' <summary>畫布上的字級（像素）。</summary>
    Public Function GetEmSize(layer As TextLayer, bounds As RectangleF) As Single
        Return Math.Max(1.0F, layer.FontSize * bounds.Height)
    End Function

    ''' <summary>文字中心點在畫布上的位置。</summary>
    Public Function GetCenter(layer As TextLayer, bounds As RectangleF) As PointF
        Return New PointF(bounds.X + layer.Position.X * bounds.Width, bounds.Y + layer.Position.Y * bounds.Height)
    End Function

    ''' <summary>文字外框（點選、顯示控制點用）。</summary>
    Public Function Measure(layer As TextLayer, bounds As RectangleF) As TextFrame
        Dim em = GetEmSize(layer, bounds)
        Dim size As SizeF
        Using path = CreateLocalPath(layer, em)
            If path Is Nothing Then
                size = New SizeF(em * EmptyWidthEm, em)
            Else
                Dim r = path.GetBounds()
                size = New SizeF(Math.Max(r.Width, em * 0.5F), Math.Max(r.Height, em * 0.5F))
            End If
        End Using
        Return New TextFrame(GetCenter(layer, bounds), size, layer.Rotation)
    End Function

    Public Sub Draw(g As Graphics, layer As TextLayer, bounds As RectangleF)
        Dim em = GetEmSize(layer, bounds)
        Using path = CreateLocalPath(layer, em)
            If path Is Nothing Then Return

            Dim center = GetCenter(layer, bounds)
            Dim state = g.Save()
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TranslateTransform(center.X, center.Y)
            g.RotateTransform(layer.Rotation)

            If layer.ShadowEnabled Then
                Using shadow = CType(path.Clone(), GraphicsPath),
                      m As New Matrix(),
                      brush As New SolidBrush(layer.ShadowColor)
                    m.Translate(layer.ShadowOffset.X * em, layer.ShadowOffset.Y * em)
                    shadow.Transform(m)
                    g.FillPath(brush, shadow)
                End Using
            End If

            If layer.OutlineWidth > 0 Then
                Using pen As New Pen(layer.OutlineColor, layer.OutlineWidth * em * 2) With {.LineJoin = LineJoin.Round}
                    ' 外框畫兩倍寬，再由填色蓋住內側一半，視覺上就是向外擴的外框
                    g.DrawPath(pen, path)
                End Using
            End If

            Using brush As New SolidBrush(layer.Color)
                g.FillPath(brush, path)
            End Using
            g.Restore(state)
        End Using
    End Sub

    ''' <summary>以原點為中心、未旋轉的文字路徑；空白文字回傳 Nothing。呼叫端負責 Dispose。</summary>
    Private Function CreateLocalPath(layer As TextLayer, em As Single) As GraphicsPath
        If String.IsNullOrWhiteSpace(layer.Text) Then Return Nothing

        Dim family = ResolveFamily(layer.FontFamily)
        Dim style = ResolveStyle(family, layer)
        Dim path As New GraphicsPath()
        Using format = CType(StringFormat.GenericTypographic.Clone(), StringFormat)
            format.Alignment = layer.Alignment
            format.FormatFlags = format.FormatFlags Or StringFormatFlags.MeasureTrailingSpaces
            path.AddString(layer.Text.Replace(vbCrLf, vbLf), family, CInt(style), em, PointF.Empty, format)
        End Using
        If Not family.Equals(FontFamily.GenericSansSerif) Then family.Dispose()

        Dim r = path.GetBounds()
        If r.Width <= 0 OrElse r.Height <= 0 Then
            path.Dispose()
            Return Nothing
        End If
        Using m As New Matrix()
            m.Translate(-(r.X + r.Width / 2), -(r.Y + r.Height / 2))
            path.Transform(m)
        End Using
        Return path
    End Function

    ''' <summary>找不到字型時改用系統預設的無襯線字型。</summary>
    Private Function ResolveFamily(name As String) As FontFamily
        If Not String.IsNullOrWhiteSpace(name) Then
            Try
                Return New FontFamily(name)
            Catch ex As ArgumentException
            End Try
        End If
        Return FontFamily.GenericSansSerif
    End Function

    Private Function ResolveStyle(family As FontFamily, layer As TextLayer) As FontStyle
        Dim style = FontStyle.Regular
        If layer.Bold Then style = style Or FontStyle.Bold
        If layer.Italic Then style = style Or FontStyle.Italic
        If family.IsStyleAvailable(style) Then Return style
        If family.IsStyleAvailable(FontStyle.Regular) Then Return FontStyle.Regular
        For Each s In {FontStyle.Bold, FontStyle.Italic, FontStyle.Bold Or FontStyle.Italic}
            If family.IsStyleAvailable(s) Then Return s
        Next
        Return FontStyle.Regular
    End Function

End Module

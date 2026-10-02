''' <summary>自動散佈與整齊排列。</summary>
Public Module FreeArrange

    ''' <summary>一張照片排好的位置（畫布比例）。</summary>
    Public Structure Placement
        Public CenterX As Single
        Public CenterY As Single
        Public Width As Single
        Public Rotation As Single
    End Structure

    ''' <summary>
    ''' 依照片數切成接近畫布比例的格子，每張照片放在一格中央；<paramref name="looseness"/> 越大，
    ''' 位置偏移、傾斜角度與照片放大（重疊）越多。0 時不傾斜、不重疊，即「整齊排列」。結果可重現。
    ''' </summary>
    ''' <param name="aspects">每張照片外框的長寬比（寬 / 高）。</param>
    Public Function Arrange(aspects As IReadOnlyList(Of Double), canvasAspect As Double, looseness As Single, seed As Integer) As List(Of Placement)
        Dim result As New List(Of Placement)
        Dim n = aspects.Count
        If n = 0 Then Return result
        If canvasAspect <= 0 Then canvasAspect = 1
        looseness = Math.Max(0F, Math.Min(1.0F, looseness))

        Dim cols = Math.Max(1, CInt(Math.Round(Math.Sqrt(n * canvasAspect))))
        Dim rows = CInt(Math.Ceiling(n / cols))
        Dim cellW = 1.0 / cols, cellH = 1.0 / rows
        Dim cellAspect = cellW * canvasAspect / cellH          ' 格子的實際長寬比
        Dim scale = 0.82 + 0.38 * looseness                    ' 隨性時放大，產生重疊
        Dim jitter = 0.2 * looseness
        Dim maxTilt = If(looseness > 0, 2 + 12 * looseness, 0)
        Dim rng As New Random(seed)

        For i = 0 To n - 1
            Dim r = i \ cols, c = i Mod cols
            Dim inRow = Math.Min(cols, n - r * cols)
            Dim shift = (cols - inRow) / 2.0                   ' 最後一列置中
            Dim a = If(aspects(i) > 0, aspects(i), 1.0)

            ' 照片放進格子（寬或高先碰到格子邊），換算成畫布寬度的比例
            Dim w = If(a >= cellAspect, cellW, cellH * a / canvasAspect) * scale
            Dim cx = (c + shift + 0.5) * cellW + (rng.NextDouble() * 2 - 1) * jitter * cellW
            Dim cy = (r + 0.5) * cellH + (rng.NextDouble() * 2 - 1) * jitter * cellH
            Dim rot = (rng.NextDouble() * 2 - 1) * maxTilt

            result.Add(New Placement With {
                .CenterX = CSng(Math.Max(0.04, Math.Min(0.96, cx))),
                .CenterY = CSng(Math.Max(0.04, Math.Min(0.96, cy))),
                .Width = CSng(Math.Max(FreeLayoutSettings.MinItemWidth, w)),
                .Rotation = CSng(rot)})
        Next
        Return result
    End Function

    ''' <summary>外框（含邊框）的長寬比，用於排版。</summary>
    Public Function OuterAspect(item As FreeItem) As Double
        Dim size = FreeGeometry.GetOuterSize(item, New Drawing.SizeF(1000, 1000))
        Return size.Width / size.Height
    End Function

    ''' <summary>
    ''' 把排版結果套用到照片上（依清單順序）；隨性時同時打散圖層順序。
    ''' </summary>
    Public Sub Apply(settings As FreeLayoutSettings, canvasAspect As Double, seed As Integer)
        Dim items = settings.Items
        ' 寬度以畫布寬度為準，外框比例要用畫布的實際寬高換算
        Dim aspects = items.Select(Function(i) OuterAspect(i)).ToList()
        Dim placements = Arrange(aspects, canvasAspect, settings.Looseness, seed)
        For i = 0 To items.Count - 1
            items(i).CenterX = placements(i).CenterX
            items(i).CenterY = placements(i).CenterY
            items(i).Width = placements(i).Width
            items(i).Rotation = placements(i).Rotation
        Next
        If settings.Looseness > 0 AndAlso items.Count > 1 Then
            Dim rng As New Random(seed Xor &H5A5A)
            Dim shuffled = items.OrderBy(Function(x) rng.Next()).ToList()
            items.Clear()
            items.AddRange(shuffled)
        End If
    End Sub

End Module

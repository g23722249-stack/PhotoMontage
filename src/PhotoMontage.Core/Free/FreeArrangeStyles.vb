Imports System.Drawing

''' <summary>自由拼貼的排列方式。</summary>
Public Enum ArrangeStyle
    ''' <summary>隨機散佈（依隨性程度偏移、傾斜）。</summary>
    Scatter = 0
    ''' <summary>整齊格狀，不傾斜。</summary>
    Grid = 1
    ''' <summary>從中央往外繞的螺旋。</summary>
    Spiral = 2
    ''' <summary>圍成一圈，照片多時中央放一張大圖。</summary>
    Ring = 3
    ''' <summary>排成愛心形。</summary>
    Heart = 4
    ''' <summary>像手上拿著一疊照片般攤開。</summary>
    Fan = 5
    ''' <summary>疊在中央、略為錯開的一堆照片。</summary>
    Pile = 6
    ''' <summary>由左上往右下斜向排列。</summary>
    Diagonal = 7
End Enum

''' <summary>排列選項。</summary>
Public Structure ArrangeOptions
    Public Style As ArrangeStyle
    ''' <summary>隨性程度 0~1：位置偏移與傾斜角度。</summary>
    Public Looseness As Single
    ''' <summary>照片可以互相重疊（照片較大）；False 時保持間隔。</summary>
    Public Overlap As Boolean
    ''' <summary>螺旋與圓環的方向。</summary>
    Public Clockwise As Boolean
End Structure

''' <summary>排列結果：每張照片的位置（與輸入順序相同），以及由下到上的圖層順序。</summary>
Public NotInheritable Class ArrangeResult
    Public Property Placements As New List(Of FreeArrange.Placement)
    Public Property ZOrder As New List(Of Integer)
End Class

''' <summary>
''' 各種排列方式。共同流程：依樣式產生每張照片的錨點（中心、相對大小、基本角度），
''' 加上隨性程度的偏移與傾斜，找出「互不重疊」時照片能有的最大尺寸（重疊時再放大），
''' 最後把整組照片等比例縮放、置中放進畫布。內部座標以畫布高度為 1，寬度為畫布長寬比。
''' </summary>
Public Module FreeArrangeStyles

    ''' <summary>畫布四周保留的邊界（畫布短邊的比例）。</summary>
    Private Const Margin As Double = 0.04

    ''' <summary>互不重疊時照片之間的間隔（照片大小的比例）。</summary>
    Private Const Spacing As Double = 0.05

    Private Structure Anchor
        Public X As Double
        Public Y As Double
        Public Size As Double
        Public Rotation As Double
    End Structure

    ''' <summary>哪些排列方式可以選「重疊／不重疊」。</summary>
    Public Function SupportsOverlap(style As ArrangeStyle) As Boolean
        Return style <> ArrangeStyle.Grid AndAlso style <> ArrangeStyle.Pile AndAlso style <> ArrangeStyle.Fan
    End Function

    ''' <summary>哪些排列方式可以選方向。</summary>
    Public Function SupportsDirection(style As ArrangeStyle) As Boolean
        Return style = ArrangeStyle.Spiral OrElse style = ArrangeStyle.Ring
    End Function

    ''' <param name="aspects">每張照片外框的長寬比（寬 / 高）。</param>
    Public Function Arrange(aspects As IReadOnlyList(Of Double), canvasAspect As Double, options As ArrangeOptions, seed As Integer) As ArrangeResult
        Dim result As New ArrangeResult()
        Dim n = aspects.Count
        If n = 0 Then Return result
        If canvasAspect <= 0 Then canvasAspect = 1
        Dim loose = Math.Max(0.0, Math.Min(1.0, options.Looseness))
        Dim rng As New Random(seed)

        ' 隨機散佈且可重疊：沿用原本的演算法（照片放大、圖層打散）
        If options.Style = ArrangeStyle.Scatter AndAlso options.Overlap Then
            result.Placements = FreeArrange.Arrange(aspects, canvasAspect, CSng(loose), seed)
            result.ZOrder = Enumerable.Range(0, n).ToList()
            If loose > 0 AndAlso n > 1 Then
                Dim zr As New Random(seed Xor &H5A5A)
                result.ZOrder = result.ZOrder.OrderBy(Function(x) zr.Next()).ToList()
            End If
            Return result
        End If

        Dim anchors = CreateAnchors(options.Style, n, canvasAspect, options.Clockwise, loose, rng)

        ' 照片面積相同、依比例決定寬高
        Dim widths(n - 1) As Double, heights(n - 1) As Double
        For i = 0 To n - 1
            Dim a = If(aspects(i) > 0, aspects(i), 1.0)
            widths(i) = Math.Sqrt(a) * anchors(i).Size
            heights(i) = anchors(i).Size / Math.Sqrt(a)
        Next

        Dim unit As Double
        If options.Style = ArrangeStyle.Pile OrElse options.Style = ArrangeStyle.Fan Then
            unit = 1.0
        Else
            unit = LargestWithoutOverlap(anchors, widths, heights)
            If options.Overlap AndAlso SupportsOverlap(options.Style) Then unit *= OverlapFactor(options.Style) + 0.3 * loose
        End If

        Dim centered = options.Style = ArrangeStyle.Spiral OrElse options.Style = ArrangeStyle.Ring
        result.Placements = FitToCanvas(anchors, widths, heights, unit, canvasAspect, centered)
        result.ZOrder = GetZOrder(options.Style, n)
        Return result
    End Function

#Region "錨點"

    Private Function CreateAnchors(style As ArrangeStyle, n As Integer, canvasAspect As Double, clockwise As Boolean,
                                   loose As Double, rng As Random) As List(Of Anchor)
        Dim anchors As List(Of Anchor)
        Dim jitter = 0.0, tilt = 0.0
        Select Case style
            Case ArrangeStyle.Grid
                anchors = GridAnchors(n, canvasAspect)
            Case ArrangeStyle.Scatter
                anchors = GridAnchors(n, canvasAspect)
                jitter = 0.2 * loose
                tilt = If(loose > 0, 2 + 12 * loose, 0)
            Case ArrangeStyle.Spiral
                anchors = SpiralAnchors(n, canvasAspect, clockwise)
                jitter = 0.1 * loose
                tilt = 3 + 10 * loose
            Case ArrangeStyle.Ring
                anchors = RingAnchors(n, canvasAspect, clockwise)
                jitter = 0.08 * loose
                tilt = 2 + 10 * loose
            Case ArrangeStyle.Heart
                anchors = HeartAnchors(n)
                jitter = 0.08 * loose
                tilt = 2 + 10 * loose
            Case ArrangeStyle.Fan
                anchors = FanAnchors(n)
                jitter = 0.04 * loose
                tilt = 2 * loose
            Case ArrangeStyle.Pile
                anchors = PileAnchors(n, canvasAspect, loose, rng)
            Case Else
                anchors = DiagonalAnchors(n, canvasAspect)
                jitter = 0.1 * loose
                tilt = 2 + 10 * loose
        End Select

        ' 隨性程度：位置偏移（以錨點間距為單位）與額外傾斜
        Dim stepSize = TypicalSpacing(anchors)
        For i = 0 To anchors.Count - 1
            Dim a = anchors(i)
            a.X += (rng.NextDouble() * 2 - 1) * jitter * stepSize
            a.Y += (rng.NextDouble() * 2 - 1) * jitter * stepSize
            a.Rotation += (rng.NextDouble() * 2 - 1) * tilt
            anchors(i) = a
        Next
        Return anchors
    End Function

    ''' <summary>相鄰錨點的典型距離（用來決定偏移量）。</summary>
    Private Function TypicalSpacing(anchors As List(Of Anchor)) As Double
        If anchors.Count < 2 Then Return 1
        Dim total = 0.0
        For i = 0 To anchors.Count - 1
            Dim best = Double.MaxValue
            For j = 0 To anchors.Count - 1
                If i = j Then Continue For
                Dim dx = anchors(i).X - anchors(j).X, dy = anchors(i).Y - anchors(j).Y
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy))
            Next
            total += best
        Next
        Return total / anchors.Count
    End Function

    Private Function NewAnchor(x As Double, y As Double, Optional size As Double = 1, Optional rotation As Double = 0) As Anchor
        Return New Anchor With {.X = x, .Y = y, .Size = size, .Rotation = rotation}
    End Function

    ''' <summary>接近畫布比例的格子，最後一列置中。</summary>
    Private Function GridAnchors(n As Integer, canvasAspect As Double) As List(Of Anchor)
        Dim cols = Math.Max(1, CInt(Math.Round(Math.Sqrt(n * canvasAspect))))
        Dim rows = CInt(Math.Ceiling(n / cols))
        Dim cellW = canvasAspect / cols, cellH = 1.0 / rows
        Dim list As New List(Of Anchor)
        For i = 0 To n - 1
            Dim r = i \ cols, c = i Mod cols
            Dim inRow = Math.Min(cols, n - r * cols)
            Dim shift = (cols - inRow) / 2.0
            list.Add(NewAnchor((c + shift + 0.5) * cellW, (r + 0.5) * cellH))
        Next
        Return list
    End Function

    ''' <summary>
    ''' 阿基米德螺旋：第一張在中央（較大），其餘從第一圈開始，沿螺旋等距排列，每圈間距 1。
    ''' 依畫布比例把水平方向拉寬。
    ''' </summary>
    Private Function SpiralAnchors(n As Integer, canvasAspect As Double, clockwise As Boolean) As List(Of Anchor)
        Dim list As New List(Of Anchor) From {NewAnchor(0, 0, If(n >= 3, 1.5, 1))}
        Dim stretch = Math.Sqrt(canvasAspect)
        Dim b = 1 / (2 * Math.PI)            ' r = b·θ → 每圈半徑增加 1
        Dim theta = 2 * Math.PI * 1.1        ' 第一圈留空間給中央的大圖
        Dim direction = If(clockwise, 1, -1)
        For i = 1 To n - 1
            Dim r = b * theta
            ' 螢幕的 Y 軸向下，角度遞增時為順時針
            list.Add(NewAnchor(r * Math.Cos(theta) * stretch, r * Math.Sin(theta) * direction))
            theta += 1.0 / Math.Sqrt(r * r + b * b)   ' 弧長 1
        Next
        Return list
    End Function

    ''' <summary>圍成一圈（從正上方開始）；4 張以上時第一張放在中央並放大。</summary>
    Private Function RingAnchors(n As Integer, canvasAspect As Double, clockwise As Boolean) As List(Of Anchor)
        Dim list As New List(Of Anchor)
        Dim stretch = Math.Sqrt(canvasAspect)
        Dim center = n >= 4
        Dim count = If(center, n - 1, n)
        ' 中央大圖隨圓環變大，填滿圈內的空間；圓環至少要留出中央大圖的位置
        Dim centerSize = Math.Max(1.2, Math.Min(4.0, count / (2 * Math.PI) * 0.9))
        If center Then list.Add(NewAnchor(0, 0, centerSize))
        Dim radius = Math.Max(If(center, centerSize * 0.75 + 0.9, 0.8), count / (2 * Math.PI))
        Dim direction = If(clockwise, 1, -1)
        For k = 0 To count - 1
            Dim angle = -Math.PI / 2 + direction * 2 * Math.PI * k / count
            list.Add(NewAnchor(radius * Math.Cos(angle) * stretch, radius * Math.Sin(angle)))
        Next
        Return list
    End Function

    ''' <summary>愛心曲線上等距排列（從上方凹處開始）。</summary>
    Private Function HeartAnchors(n As Integer) As List(Of Anchor)
        Const samples = 720
        Dim pts(samples) As PointF
        For i = 0 To samples
            Dim t = 2 * Math.PI * i / samples
            Dim x = 16 * Math.Pow(Math.Sin(t), 3)
            Dim y = -(13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t))
            pts(i) = New PointF(CSng(x), CSng(y))
        Next
        Dim lengths(samples) As Double
        For i = 1 To samples
            Dim dx = pts(i).X - pts(i - 1).X, dy = pts(i).Y - pts(i - 1).Y
            lengths(i) = lengths(i - 1) + Math.Sqrt(dx * dx + dy * dy)
        Next
        Dim total = lengths(samples)
        Dim scale = n / total                 ' 周長 = 照片數，相鄰間距約 1
        Dim list As New List(Of Anchor)
        Dim j = 0
        For k = 0 To n - 1
            Dim target = total * k / n
            While j < samples - 1 AndAlso lengths(j + 1) < target
                j += 1
            End While
            Dim seg = Math.Max(0.000001, lengths(j + 1) - lengths(j))
            Dim f = (target - lengths(j)) / seg
            Dim x = pts(j).X + (pts(j + 1).X - pts(j).X) * f
            Dim y = pts(j).Y + (pts(j + 1).Y - pts(j).Y) * f
            list.Add(NewAnchor(x * scale, y * scale))
        Next
        Return list
    End Function

    ''' <summary>
    ''' 扇形：像手上拿著的一疊照片。照片以下方同一點為軸攤開並跟著轉向，彼此大幅重疊。
    ''' </summary>
    Private Function FanAnchors(n As Integer) As List(Of Anchor)
        Dim list As New List(Of Anchor)
        Dim spread = If(n = 1, 0, Math.Min(110.0, 12.0 * (n - 1)))
        Const radius = 0.8                    ' 照片中心到軸心的距離（照片大小約 1）
        For k = 0 To n - 1
            Dim degrees = If(n = 1, 0, -spread / 2 + spread * k / (n - 1))
            Dim angle = degrees * Math.PI / 180
            list.Add(NewAnchor(radius * Math.Sin(angle), -radius * Math.Cos(angle), 1, degrees))
        Next
        Return list
    End Function

    ''' <summary>照片堆：集中在中央、隨機錯開與傾斜。</summary>
    Private Function PileAnchors(n As Integer, canvasAspect As Double, loose As Double, rng As Random) As List(Of Anchor)
        Dim list As New List(Of Anchor)
        Dim spreadX = (0.18 + 0.12 * loose) * Math.Sqrt(canvasAspect) * Math.Sqrt(Math.Min(n, 12) / 4.0)
        Dim spreadY = (0.18 + 0.12 * loose) * Math.Sqrt(Math.Min(n, 12) / 4.0)
        Dim tilt = 8 + 18 * loose
        For k = 0 To n - 1
            list.Add(NewAnchor(Gaussian(rng) * spreadX, Gaussian(rng) * spreadY, 1, (rng.NextDouble() * 2 - 1) * tilt))
        Next
        Return list
    End Function

    Private Function Gaussian(rng As Random) As Double
        Dim u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble()
        Return Math.Max(-2.5, Math.Min(2.5, Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)))
    End Function

    ''' <summary>
    ''' 沿畫布對角線（左上→右下）排列。6 張以內左右交錯成一道；更多時分成數道平行、前後錯開的斜線。
    ''' </summary>
    Private Function DiagonalAnchors(n As Integer, canvasAspect As Double) As List(Of Anchor)
        Dim list As New List(Of Anchor)
        Dim length = Math.Sqrt(canvasAspect * canvasAspect + 1)
        Dim dx = canvasAspect / length, dy = 1 / length     ' 沿對角線
        Dim px = -dy, py = dx                               ' 垂直於對角線
        If n <= 6 Then
            For k = 0 To n - 1
                Dim offset = If(n > 3, If(k Mod 2 = 0, -0.45, 0.45), 0)
                list.Add(NewAnchor(k * 0.8 * dx + offset * px, k * 0.8 * dy + offset * py))
            Next
            Return list
        End If

        Dim perLane = CInt(Math.Ceiling(Math.Sqrt(n * 2.0)))
        Dim lanes = CInt(Math.Ceiling(n / CDbl(perLane)))
        For k = 0 To n - 1
            Dim lane = k \ perLane, pos = k Mod perLane
            Dim inLane = Math.Min(perLane, n - lane * perLane)
            Dim along = (pos - (inLane - 1) / 2.0) + If(lane Mod 2 = 1, 0.5, 0)
            Dim across = (lane - (lanes - 1) / 2.0) * 1.15
            list.Add(NewAnchor(along * dx + across * px, along * dy + across * py))
        Next
        Return list
    End Function

#End Region

#Region "尺寸與擺放"

    Private Function OverlapFactor(style As ArrangeStyle) As Double
        Select Case style
            Case ArrangeStyle.Heart : Return 1.6
            Case ArrangeStyle.Scatter : Return 1.25
            Case Else : Return 1.45
        End Select
    End Function

    ''' <summary>旋轉後外接矩形的半寬、半高。</summary>
    Private Function HalfExtents(w As Double, h As Double, rotation As Double) As (X As Double, Y As Double)
        Dim r = rotation * Math.PI / 180
        Dim c = Math.Abs(Math.Cos(r)), s = Math.Abs(Math.Sin(r))
        Return (w / 2 * c + h / 2 * s, w / 2 * s + h / 2 * c)
    End Function

    ''' <summary>照片都不重疊（含間隔）時能用的最大尺寸，以二分搜尋求得。</summary>
    Private Function LargestWithoutOverlap(anchors As List(Of Anchor), widths As Double(), heights As Double()) As Double
        Dim n = anchors.Count
        If n < 2 Then Return 1
        Dim overlaps = Function(unit As Double) As Boolean
                           Dim gap = Spacing * unit
                           For i = 0 To n - 1
                               Dim ei = HalfExtents(widths(i) * unit, heights(i) * unit, anchors(i).Rotation)
                               For j = i + 1 To n - 1
                                   Dim ej = HalfExtents(widths(j) * unit, heights(j) * unit, anchors(j).Rotation)
                                   If Math.Abs(anchors(i).X - anchors(j).X) < ei.X + ej.X + gap AndAlso
                                      Math.Abs(anchors(i).Y - anchors(j).Y) < ei.Y + ej.Y + gap Then Return True
                               Next
                           Next
                           Return False
                       End Function
        Dim lo = 0.0001, hi = 20.0
        If Not overlaps(hi) Then Return hi
        For iteration = 1 To 40
            Dim mid = (lo + hi) / 2
            If overlaps(mid) Then hi = mid Else lo = mid
        Next
        Return lo
    End Function

    ''' <summary>整組照片等比例縮放、置中放進畫布（四周留邊界）。</summary>
    ''' <param name="centerOnFirst">以第一張（螺旋、圓環的中央那張）為中心，而不是整組範圍的中心。</param>
    Private Function FitToCanvas(anchors As List(Of Anchor), widths As Double(), heights As Double(), unit As Double,
                                 canvasAspect As Double, centerOnFirst As Boolean) As List(Of FreeArrange.Placement)
        Dim n = anchors.Count
        Dim minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue
        For i = 0 To n - 1
            Dim e = HalfExtents(widths(i) * unit, heights(i) * unit, anchors(i).Rotation)
            minX = Math.Min(minX, anchors(i).X - e.X) : maxX = Math.Max(maxX, anchors(i).X + e.X)
            minY = Math.Min(minY, anchors(i).Y - e.Y) : maxY = Math.Max(maxY, anchors(i).Y + e.Y)
        Next
        Dim edge = Margin * Math.Min(canvasAspect, 1.0)
        Dim cx = (minX + maxX) / 2, cy = (minY + maxY) / 2
        Dim halfW = (maxX - minX) / 2, halfH = (maxY - minY) / 2
        If centerOnFirst Then
            cx = anchors(0).X
            cy = anchors(0).Y
            halfW = Math.Max(maxX - cx, cx - minX)
            halfH = Math.Max(maxY - cy, cy - minY)
        End If
        Dim k = Math.Min((canvasAspect / 2 - edge) / Math.Max(0.0001, halfW), (0.5 - edge) / Math.Max(0.0001, halfH))

        Dim list As New List(Of FreeArrange.Placement)
        For i = 0 To n - 1
            Dim x = canvasAspect / 2 + (anchors(i).X - cx) * k
            Dim y = 0.5 + (anchors(i).Y - cy) * k
            Dim w = widths(i) * unit * k / canvasAspect
            list.Add(New FreeArrange.Placement With {
                .CenterX = CSng(x / canvasAspect),
                .CenterY = CSng(y),
                .Width = CSng(Math.Max(FreeLayoutSettings.MinItemWidth, Math.Min(FreeLayoutSettings.MaxItemWidth, w))),
                .Rotation = CSng(anchors(i).Rotation)})
        Next
        Return list
    End Function

    ''' <summary>圖層順序（由下到上）：螺旋與圓環讓中央那張在最上層；其餘依排列順序。</summary>
    Private Function GetZOrder(style As ArrangeStyle, n As Integer) As List(Of Integer)
        Dim order = Enumerable.Range(0, n).ToList()
        If style = ArrangeStyle.Spiral OrElse style = ArrangeStyle.Ring Then order.Reverse()
        Return order
    End Function

#End Region

End Module

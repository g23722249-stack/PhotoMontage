Imports System.Drawing

''' <summary>自由拼貼照片的幾何：外框大小、邊框、畫布上的位置與點選。</summary>
Public Module FreeGeometry

    ''' <summary>拍立得底部邊框相對於側邊的倍數。</summary>
    Public Const PolaroidBottomFactor As Single = 3.5F

    ''' <summary>邊框寬度（像素）：左、上、右、下。</summary>
    Public Function GetInsets(item As FreeItem, outerWidth As Single) As (Left As Single, Top As Single, Right As Single, Bottom As Single)
        If item.Frame = FrameStyle.None Then Return (0, 0, 0, 0)
        Dim b = Math.Max(0F, item.FrameWidth) * outerWidth
        Dim bottom = If(item.Frame = FrameStyle.Polaroid, b * PolaroidBottomFactor, b)
        Return (b, b, b, bottom)
    End Function

    ''' <summary>外框（含邊框）在畫布上的像素大小；高度由照片比例與邊框推得。</summary>
    Public Function GetOuterSize(item As FreeItem, canvasSize As SizeF) As SizeF
        Dim w = Math.Max(1.0F, item.Width * canvasSize.Width)
        Dim ins = GetInsets(item, w)
        Dim innerW = Math.Max(1.0F, w - ins.Left - ins.Right)
        Dim innerH = innerW / Math.Max(0.05F, item.InnerAspect)
        Return New SizeF(w, innerH + ins.Top + ins.Bottom)
    End Function

    ''' <summary>照片區域，以外框中心為原點的本地座標（未旋轉）。</summary>
    Public Function GetInnerRect(item As FreeItem, outerSize As SizeF) As RectangleF
        Dim ins = GetInsets(item, outerSize.Width)
        Return RectangleF.FromLTRB(-outerSize.Width / 2 + ins.Left, -outerSize.Height / 2 + ins.Top,
                                   outerSize.Width / 2 - ins.Right, outerSize.Height / 2 - ins.Bottom)
    End Function

    ''' <summary>照片在畫布上的外框（中心、未旋轉尺寸、角度），用於繪製、點選與控制點。</summary>
    Public Function GetFrame(item As FreeItem, bounds As RectangleF) As TextFrame
        Dim center As New PointF(bounds.X + item.CenterX * bounds.Width, bounds.Y + item.CenterY * bounds.Height)
        Return New TextFrame(center, GetOuterSize(item, bounds.Size), item.Rotation)
    End Function

    ''' <summary>旋轉後外框的軸對齊包圍框（對齊吸附與多選用）。</summary>
    Public Function GetAxisAlignedBounds(frame As TextFrame) As RectangleF
        Dim c = frame.GetCorners()
        Dim l = c.Min(Function(p) p.X), t = c.Min(Function(p) p.Y)
        Dim r = c.Max(Function(p) p.X), b = c.Max(Function(p) p.Y)
        Return RectangleF.FromLTRB(l, t, r, b)
    End Function

    ''' <summary>多個矩形的聯集。</summary>
    Public Function Union(rects As IEnumerable(Of RectangleF)) As RectangleF
        Dim list = rects.ToList()
        If list.Count = 0 Then Return RectangleF.Empty
        Return RectangleF.FromLTRB(list.Min(Function(r) r.Left), list.Min(Function(r) r.Top), list.Max(Function(r) r.Right), list.Max(Function(r) r.Bottom))
    End Function

    ''' <summary>最上層（清單中最後）且包含該點的照片；沒有時回傳 Nothing。</summary>
    Public Function HitTest(items As IReadOnlyList(Of FreeItem), bounds As RectangleF, pt As PointF, Optional padding As Single = 0) As FreeItem
        For i = items.Count - 1 To 0 Step -1
            If GetFrame(items(i), bounds).Contains(pt, padding) Then Return items(i)
        Next
        Return Nothing
    End Function

    ''' <summary>點是否落在外框的某個角落控制點上；回傳角落索引（0 左上、1 右上、2 右下、3 左下），否則 -1。</summary>
    Public Function HitTestCorner(frame As TextFrame, pt As PointF, radius As Single, Optional padding As Single = 0) As Integer
        Dim corners = frame.GetCorners(padding)
        For i = 0 To 3
            Dim dx = pt.X - corners(i).X, dy = pt.Y - corners(i).Y
            If dx * dx + dy * dy <= radius * radius Then Return i
        Next
        Return -1
    End Function

    ''' <summary>等比例縮放：依滑鼠到中心的距離與起始距離的比值調整寬度。</summary>
    Public Function ScaleWidth(startWidth As Single, center As PointF, startPoint As PointF, currentPoint As PointF) As Single
        Dim d0 = Distance(center, startPoint), d1 = Distance(center, currentPoint)
        If d0 < 1 Then Return startWidth
        Return Math.Max(FreeLayoutSettings.MinItemWidth, Math.Min(FreeLayoutSettings.MaxItemWidth, startWidth * d1 / d0))
    End Function

    Private Function Distance(a As PointF, b As PointF) As Single
        Dim dx = a.X - b.X, dy = a.Y - b.Y
        Return CSng(Math.Sqrt(dx * dx + dy * dy))
    End Function

End Module

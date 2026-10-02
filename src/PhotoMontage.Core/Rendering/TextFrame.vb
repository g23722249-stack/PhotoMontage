Imports System.Drawing

''' <summary>文字圖層在畫布上的外框（未旋轉的尺寸＋中心點＋旋轉角度），用於點選與控制點。</summary>
Public NotInheritable Class TextFrame
    Public ReadOnly Property Center As PointF
    Public ReadOnly Property Size As SizeF
    ''' <summary>順時針角度（度）。</summary>
    Public ReadOnly Property Rotation As Single

    Public Sub New(center As PointF, size As SizeF, rotation As Single)
        Me.Center = center
        Me.Size = size
        Me.Rotation = rotation
    End Sub

    ''' <summary>點是否在（旋轉後的）外框內；<paramref name="padding"/> 為外擴像素。</summary>
    Public Function Contains(pt As PointF, Optional padding As Single = 0) As Boolean
        Dim local = ToLocal(pt)
        Return Math.Abs(local.X) <= Size.Width / 2 + padding AndAlso Math.Abs(local.Y) <= Size.Height / 2 + padding
    End Function

    ''' <summary>四個角（左上、右上、右下、左下），已旋轉。</summary>
    Public Function GetCorners(Optional padding As Single = 0) As PointF()
        Dim w = Size.Width / 2 + padding, h = Size.Height / 2 + padding
        Return {ToCanvas(New PointF(-w, -h)), ToCanvas(New PointF(w, -h)), ToCanvas(New PointF(w, h)), ToCanvas(New PointF(-w, h))}
    End Function

    ''' <summary>旋轉控制點：外框上緣中點再往上 <paramref name="distance"/> 像素（隨外框旋轉）。</summary>
    Public Function GetRotateHandle(distance As Single, Optional padding As Single = 0) As PointF
        Return ToCanvas(New PointF(0, -(Size.Height / 2 + padding + distance)))
    End Function

    ''' <summary>畫布座標 → 外框本地座標（中心為原點、未旋轉）。</summary>
    Public Function ToLocal(pt As PointF) As PointF
        Dim rad = -Rotation * Math.PI / 180
        Dim dx = pt.X - Center.X, dy = pt.Y - Center.Y
        Return New PointF(CSng(dx * Math.Cos(rad) - dy * Math.Sin(rad)), CSng(dx * Math.Sin(rad) + dy * Math.Cos(rad)))
    End Function

    Private Function ToCanvas(local As PointF) As PointF
        Dim rad = Rotation * Math.PI / 180
        Return New PointF(CSng(Center.X + local.X * Math.Cos(rad) - local.Y * Math.Sin(rad)),
                          CSng(Center.Y + local.X * Math.Sin(rad) + local.Y * Math.Cos(rad)))
    End Function

    ''' <summary>從中心指向 <paramref name="pt"/> 的角度：正上方為 0，順時針為正，範圍 (-180, 180]。</summary>
    Public Shared Function AngleFromCenter(center As PointF, pt As PointF) As Single
        Return CSng(Math.Atan2(pt.X - center.X, -(pt.Y - center.Y)) * 180 / Math.PI)
    End Function

    ''' <summary>把角度吸附到 <paramref name="stepDegrees"/> 的倍數，並正規化到 (-180, 180]。</summary>
    Public Shared Function NormalizeAngle(angle As Single, Optional stepDegrees As Single = 0) As Single
        Dim a = angle
        If stepDegrees > 0 Then a = CSng(Math.Round(a / stepDegrees) * stepDegrees)
        a = a Mod 360
        If a <= -180 Then a += 360
        If a > 180 Then a -= 360
        Return a
    End Function
End Class

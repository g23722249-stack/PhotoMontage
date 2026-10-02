Imports System.Drawing

''' <summary>CIELAB 色彩（D65）。歐氏距離比 RGB 更接近人眼感受到的色差。</summary>
Public Structure LabColor
    Public ReadOnly L As Single
    Public ReadOnly A As Single
    Public ReadOnly B As Single

    Public Sub New(l As Single, a As Single, b As Single)
        Me.L = l
        Me.A = a
        Me.B = b
    End Sub

    Private Shared ReadOnly LinearTable As Double() = Enumerable.Range(0, 256).Select(
        Function(i)
            Dim c = i / 255.0
            Return If(c <= 0.04045, c / 12.92, Math.Pow((c + 0.055) / 1.055, 2.4))
        End Function).ToArray()

    Public Shared Function FromColor(color As Color) As LabColor
        Return FromRgb(color.R, color.G, color.B)
    End Function

    ''' <summary>sRGB（0～255，可為小數平均值）→ Lab。</summary>
    Public Shared Function FromRgb(r As Double, g As Double, b As Double) As LabColor
        Dim lr = ToLinear(r), lg = ToLinear(g), lb = ToLinear(b)
        ' sRGB → XYZ（D65），再除以白點
        Dim x = (0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb) / 0.95047
        Dim y = 0.2126729 * lr + 0.7151522 * lg + 0.072175 * lb
        Dim z = (0.0193339 * lr + 0.119192 * lg + 0.9503041 * lb) / 1.08883
        Dim fx = F(x), fy = F(y), fz = F(z)
        Return New LabColor(CSng(116 * fy - 16), CSng(500 * (fx - fy)), CSng(200 * (fy - fz)))
    End Function

    Private Shared Function ToLinear(v As Double) As Double
        Dim clamped = Math.Max(0.0, Math.Min(255.0, v))
        Dim i = CInt(Math.Floor(clamped))
        If i >= 255 Then Return 1.0
        ' 平均值可能有小數：在表格兩點間內插
        Dim t = clamped - i
        Return LinearTable(i) * (1 - t) + LinearTable(i + 1) * t
    End Function

    Private Shared Function F(t As Double) As Double
        Return If(t > 0.008856, Math.Pow(t, 1 / 3.0), 7.787 * t + 16 / 116.0)
    End Function

    Public Shared Function DistanceSquared(x As LabColor, y As LabColor) As Single
        Dim dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B
        Return dl * dl + da * da + db * db
    End Function

    Public Overrides Function ToString() As String
        Return $"Lab({L:0.0}, {A:0.0}, {B:0.0})"
    End Function
End Structure

Imports System.Windows.Forms

''' <summary>一則說明提示：標題（功能名稱）與操作說明。</summary>
Friend NotInheritable Class HelpTip
    Public ReadOnly Property Title As String
    Public ReadOnly Property Text As String

    ''' <param name="lines">說明的每一行；提示框只在換行處斷行，所以每行保持簡短。</param>
    Public Sub New(title As String, ParamArray lines As String())
        Me.Title = title
        Text = String.Join(vbLf, lines)
    End Sub
End Class

''' <summary>
''' iPhoto 風格的說明提示（與 Aqua.Net MediaItem 的 ToolTipTitle／ToolTipText 相同）：
''' 標題為功能名稱，內容為操作說明，滑鼠停留 0.5 秒後出現。
''' WinForms 的 ToolTip 只有一個標題，所以在滑鼠進入控制項時換成該控制項的標題。
''' </summary>
Friend NotInheritable Class HelpToolTip
    Inherits ToolTip

    ''' <param name="owner">擁有者；擁有者釋放時一併釋放。</param>
    Public Sub New(owner As Control)
        InitialDelay = 500
        ReshowDelay = 100
        AutoPopDelay = 20000
        ShowAlways = True
        AddHandler owner.Disposed, Sub(s, e) Dispose()
    End Sub

    ''' <summary>
    ''' 為控制項（連同它的子控制項，例如滑桿的數值標籤）設定說明。
    ''' 同時傳入欄位標籤，滑鼠停在標籤上也會顯示。
    ''' </summary>
    Public Sub SetHelp(tip As HelpTip, ParamArray controls As Control())
        For Each c In controls
            If c IsNot Nothing Then Attach(c, tip)
        Next
    End Sub

    ''' <summary>
    ''' 依滑鼠位置顯示不同內容的自繪控制項（縮圖清單、畫布）用：直接換標題與內容；
    ''' <paramref name="text"/> 為 Nothing 時隱藏提示。
    ''' </summary>
    Public Sub ShowFor(control As Control, title As String, text As String)
        If text IsNot Nothing Then ToolTipTitle = If(title, "")
        SetToolTip(control, text)
    End Sub

    Private Sub Attach(control As Control, tip As HelpTip)
        SetToolTip(control, tip.Text)
        AddHandler control.MouseEnter, Sub(s, e) If ToolTipTitle <> tip.Title Then ToolTipTitle = tip.Title
        For Each child As Control In control.Controls
            Attach(child, tip)
        Next
    End Sub
End Class

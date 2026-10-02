Imports System.Threading
Imports PhotoMontage.Core
Imports PhotoMontage.Imaging

''' <summary>整個行程共用的解碼器與縮圖快取（多個編輯器、多次開啟共用同一份記憶體快取）。</summary>
Friend Module ImportServices

    Private ReadOnly _codec As New Lazy(Of IImageCodec)(Function() New WicImageCodec(), LazyThreadSafetyMode.ExecutionAndPublication)

    Private ReadOnly _cache As New Lazy(Of ThumbnailCache)(
        Function()
            Dim cache As New ThumbnailCache(_codec.Value, ThumbnailCache.DefaultDirectory)
            ' 啟動時在背景整理一次磁碟快取
            Task.Run(Function() cache.TrimDisk())
            Return cache
        End Function, LazyThreadSafetyMode.ExecutionAndPublication)

    Public Function CreateImporter() As PhotoImporter
        Return New PhotoImporter(_codec.Value, _cache.Value)
    End Function

End Module

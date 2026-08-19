Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic.Serialization.JSON

''' <summary>
''' 每日日记条目：Agent 对当日对话内容的总结成篇（第一人称陪伴日记）。
''' 每天一份，按 <c>data\diary\yyyy-MM-dd.json</c> 持久化。
''' </summary>
Public Class DiaryEntry

    ''' <summary>日记日期（yyyy-MM-dd）</summary>
    Public Property [date] As String = ""

    ''' <summary>日记标题</summary>
    Public Property title As String = ""

    ''' <summary>日记正文（第一人称）</summary>
    Public Property content As String = ""

    ''' <summary>生成时间（yyyy-MM-dd HH:mm:ss）</summary>
    Public Property generatedAt As String = ""

    ''' <summary>写日记时已覆盖的当日对话轮次（信息性字段）</summary>
    Public Property turnCount As Integer
End Class

''' <summary>
''' 日记持久化仓库：基于 data\diary\ 目录的按日 JSON 文件读写。
''' 文件损坏/缺失均安全容错（返回 Nothing / 跳过），不中断主流程。
''' </summary>
Public Module DiaryStore

    ''' <summary>日记文件名日期格式</summary>
    Public Const DATE_FORMAT As String = "yyyy-MM-dd"

    ''' <summary>单个日记文件路径（不校验存在性）。</summary>
    Public Function GetFilePath(diaryDir As String, [date] As String) As String
        Return Path.Combine(diaryDir, $"{[date]}.json")
    End Function

    ''' <summary>
    ''' 加载指定日期的日记；文件不存在或损坏时返回 Nothing。
    ''' </summary>
    Public Function Load(diaryDir As String, [date] As String) As DiaryEntry
        Dim filePath As String = GetFilePath(diaryDir, [date])
        If Not File.Exists(filePath) Then
            Return Nothing
        End If

        Try
            Dim entry As DiaryEntry = LoadJsonFile(Of DiaryEntry)(file:=filePath, simpleDict:=True)
            If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.content) Then
                Return Nothing
            End If
            If String.IsNullOrWhiteSpace(entry.[date]) Then entry.[date] = [date]
            Return entry
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记文件加载失败 ({[date]}): {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 保存日记（覆盖写）；目录不存在时自动创建。
    ''' </summary>
    Public Function Save(diaryDir As String, entry As DiaryEntry) As Boolean
        Try
            Call Directory.CreateDirectory(diaryDir)
            Call File.WriteAllText(GetFilePath(diaryDir, entry.[date]),
                                   entry.GetJson(indent:=True, simpleDict:=True),
                                   Text.Encoding.UTF8)
            Return True
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记保存失败 ({entry.[date]}): {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 列出全部日记摘要（按日期倒序）；无日记时返回空列表。
    ''' </summary>
    Public Function ListAll(diaryDir As String) As List(Of DiaryEntry)
        Dim result As New List(Of DiaryEntry)

        If Not Directory.Exists(diaryDir) Then
            Return result
        End If

        Try
            For Each file As String In Directory.GetFiles(diaryDir, "*.json")
                Dim [date] As String = Path.GetFileNameWithoutExtension(file)
                Dim entry As DiaryEntry = Load(diaryDir, [date])
                If entry IsNot Nothing Then
                    ' 只保留摘要（正文不进入列表内存）
                    Call result.Add(New DiaryEntry With {
                        .[date] = entry.[date],
                        .title = entry.title,
                        .generatedAt = entry.generatedAt,
                        .turnCount = entry.turnCount,
                        .content = ""
                    })
                End If
            Next

            Return result.OrderByDescending(Function(e) e.[date]).ToList()
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记列表读取失败: {ex.Message}")
            Return result
        End Try
    End Function
End Module

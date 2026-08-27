Imports System.IO
Imports Microsoft.VisualBasic.Serialization.JSON

''' <summary>
''' 日记持久化仓库：基于 data\diary\ 目录的多文件 JSON 读写。
''' 每篇日记独立文件 <c>diary/&lt;date&gt;/&lt;id&gt;.json</c>，同日多篇共存。
''' 文件损坏/缺失均安全容错（返回 Nothing / 跳过），不中断主流程。
''' </summary>
Public Module DiaryStore

    ''' <summary>日记文件名日期格式</summary>
    Public Const DATE_FORMAT As String = "yyyy-MM-dd"

    ''' <summary>旧版单日单文件路径（兼容读取，不再写入）</summary>
    Public Function GetLegacyFilePath(diaryDir As String, [date] As String) As String
        Return Path.Combine(diaryDir, $"{[date]}.json")
    End Function

    ''' <summary>某日日记所在子目录路径</summary>
    Public Function GetDateDir(diaryDir As String, [date] As String) As String
        Return Path.Combine(diaryDir, [date])
    End Function

    ''' <summary>单篇日记文件路径（不校验存在性）</summary>
    Public Function GetFilePath(diaryDir As String, [date] As String, id As String) As String
        Return Path.Combine(GetDateDir(diaryDir, [date]), $"{id}.json")
    End Function

    ''' <summary>
    ''' 为缺少 id 的日记派生一个稳定 id（用于旧版单文件兼容迁移）。
    ''' </summary>
    Private Function DeriveLegacyId([date] As String) As String
        Return "legacy-" & [date]
    End Function

    ''' <summary>
    ''' 加载指定日期的单篇日记（兼容旧版单文件）；文件不存在或损坏时返回 Nothing。
    ''' 优先读取旧版 <c>diary/&lt;date&gt;.json</c>；若不存在则尝试该日目录下最新一篇。
    ''' </summary>
    Public Function Load(diaryDir As String, [date] As String) As DiaryEntry
        ' 旧版单文件优先（向后兼容）
        Dim legacyPath As String = GetLegacyFilePath(diaryDir, [date])
        If File.Exists(legacyPath) Then
            Dim legacy As DiaryEntry = ReadEntry(legacyPath, [date])
            If legacy IsNot Nothing Then
                If String.IsNullOrWhiteSpace(legacy.id) Then legacy.id = DeriveLegacyId([date])
                Return legacy
            End If
        End If

        ' 新多文件结构：取该日最新一篇
        Dim all As List(Of DiaryEntry) = LoadAll(diaryDir, [date])
        If all IsNot Nothing AndAlso all.Count > 0 Then
            Return all.OrderByDescending(Function(e) e.generatedAt).First()
        End If

        Return Nothing
    End Function

    ''' <summary>
    ''' 加载指定日期、指定 id 的单篇日记；不存在或损坏时返回 Nothing。
    ''' </summary>
    Public Function Load(diaryDir As String, [date] As String, id As String) As DiaryEntry
        Dim filePath As String = GetFilePath(diaryDir, [date], id)
        If Not File.Exists(filePath) Then
            Return Nothing
        End If
        Return ReadEntry(filePath, [date])
    End Function

    ''' <summary>
    ''' 读取单个日记 JSON 文件（内部）；缺 id 时按文件名派生，缺 date 时补全。
    ''' </summary>
    Private Function ReadEntry(filePath As String, fallbackDate As String) As DiaryEntry
        Try
            Dim entry As DiaryEntry = LoadJsonFile(Of DiaryEntry)(file:=filePath, simpleDict:=True)
            If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.content) Then
                Return Nothing
            End If
            If String.IsNullOrWhiteSpace(entry.[date]) Then entry.[date] = fallbackDate
            If String.IsNullOrWhiteSpace(entry.id) Then entry.id = Path.GetFileNameWithoutExtension(filePath)
            Return entry
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记文件加载失败 ({filePath}): {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 加载指定日期的全部日记（多篇）；按生成时间倒序；无则返回空列表。
    ''' </summary>
    Public Function LoadAll(diaryDir As String, [date] As String) As List(Of DiaryEntry)
        Dim result As New List(Of DiaryEntry)

        Try
            ' 新结构：diary/<date>/<id>.json
            Dim dateDir As String = GetDateDir(diaryDir, [date])
            If Directory.Exists(dateDir) Then
                For Each file As String In Directory.GetFiles(dateDir, "*.json")
                    Dim entry As DiaryEntry = ReadEntry(file, [date])
                    If entry IsNot Nothing Then Call result.Add(entry)
                Next
            End If

            ' 旧结构兼容：diary/<date>.json（派生一个稳定 id，避免与其他篇冲突）
            Dim legacyPath As String = GetLegacyFilePath(diaryDir, [date])
            If File.Exists(legacyPath) Then
                Dim legacy As DiaryEntry = ReadEntry(legacyPath, [date])
                If legacy IsNot Nothing Then
                    If String.IsNullOrWhiteSpace(legacy.id) Then legacy.id = DeriveLegacyId([date])
                    Call result.Add(legacy)
                End If
            End If

            Return result.OrderByDescending(Function(e) e.generatedAt).ToList()
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记读取失败 ({[date]}): {ex.Message}")
            Return result
        End Try
    End Function

    ''' <summary>
    ''' 保存单篇日记（按 id 独立文件，绝不覆盖同日其他篇）；目录不存在时自动创建。
    ''' </summary>
    Public Function Save(diaryDir As String, entry As DiaryEntry) As Boolean
        If String.IsNullOrWhiteSpace(entry.id) Then
            entry.id = DateTime.Now.ToString("yyyyMMddHHmmssfff")
        End If
        Try
            Dim filePath As String = GetFilePath(diaryDir, entry.[date], entry.id)
            Call Directory.CreateDirectory(Path.GetDirectoryName(filePath))
            Call File.WriteAllText(filePath,
                                   entry.GetJson(indent:=True, simpleDict:=True),
                                   Text.Encoding.UTF8)
            Return True
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记保存失败 ({entry.[date]}/{entry.id}): {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 列出全部日记摘要（按日期倒序）；无日记时返回空列表。
    ''' 兼容旧版单文件与新版多文件；同日多篇均列出（各自独立条目）。
    ''' 列表项仅保留摘要（正文不进入内存）。
    ''' </summary>
    Public Function ListAll(diaryDir As String) As List(Of DiaryEntry)
        Dim result As New List(Of DiaryEntry)

        If Not Directory.Exists(diaryDir) Then
            Return result
        End If

        Try
            ' 旧版单文件：diary/<date>.json
            For Each file As String In Directory.GetFiles(diaryDir, "*.json")
                If Not Path.GetFileName(file).Contains(Path.DirectorySeparatorChar) Then
                    ' 仅处理 diary 根目录下直接的 *.json（旧版单文件）；子目录由下方遍历
                End If
                Dim [date] As String = Path.GetFileNameWithoutExtension(file)
                ' 跳过非日期命名的文件
                Dim parsed As DateTime
                If Not DateTime.TryParseExact([date], DATE_FORMAT, Nothing, Globalization.DateTimeStyles.None, parsed) Then
                    Continue For
                End If
                Dim entry As DiaryEntry = Load(diaryDir, [date])
                If entry IsNot Nothing Then
                    Call result.Add(Summarize(entry))
                End If
            Next

            ' 新版多文件：diary/<date>/<id>.json（仅补充根目录未覆盖的子目录）
            For Each subDir As String In Directory.GetDirectories(diaryDir)
                Dim dirName As String = Path.GetFileName(subDir)
                Dim parsed As DateTime
                If Not DateTime.TryParseExact(dirName, DATE_FORMAT, Nothing, Globalization.DateTimeStyles.None, parsed) Then
                    Continue For
                End If
                For Each file As String In Directory.GetFiles(subDir, "*.json")
                    Dim entry As DiaryEntry = ReadEntry(file, dirName)
                    If entry IsNot Nothing Then
                        ' 避免与旧版单文件 Load 读取的同一篇重复
                        If Not result.Any(Function(e) e.[date] = entry.[date] AndAlso e.id = entry.id) Then
                            Call result.Add(Summarize(entry))
                        End If
                    End If
                Next
            Next

            Return result.OrderByDescending(Function(e) e.[date]).ThenByDescending(Function(e) e.generatedAt).ToList()
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Diary] 日记列表读取失败: {ex.Message}")
            Return result
        End Try
    End Function

    ''' <summary>构造仅含摘要的日记条目（剔除正文以节省列表内存）</summary>
    Private Function Summarize(entry As DiaryEntry) As DiaryEntry
        Return New DiaryEntry With {
            .id = entry.id,
            .[date] = entry.[date],
            .title = entry.title,
            .generatedAt = entry.generatedAt,
            .turnCount = entry.turnCount,
            .content = ""
        }
    End Function
End Module

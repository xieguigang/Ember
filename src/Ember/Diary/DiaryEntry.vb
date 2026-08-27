
''' <summary>
''' 每篇日记条目：Agent 对当日对话内容的总结成篇（第一人称陪伴日记）。
''' 每篇拥有唯一 id，按 <c>data\diary\yyyy-MM-dd\&lt;id&gt;.json</c> 独立持久化，
''' 因此同一天可共存多篇日记（手动多次生成会累积，不再相互覆盖）。
''' 旧版本的单文件 <c>data\diary\yyyy-MM-dd.json</c> 仍可被兼容读取。
''' </summary>
Public Class DiaryEntry

    ''' <summary>日记唯一标识（生成时赋值，如 yyyyMMddHHmmssfff 时间戳）；用于独立存储与共存</summary>
    Public Property id As String = ""

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
Imports Microsoft.VisualBasic.Data.IO.MessagePack
Imports Microsoft.VisualBasic.Data.IO.MessagePack.Serialization
Imports Ollama

Public Class MessageSchema : Inherits SchemaProvider(Of ChatMessage)

    Shared Sub New()
        Call MsgPackSerializer.DefaultContext.RegisterSerializer(New MessageSchema)
    End Sub

    Protected Overrides Iterator Function GetObjectSchema() As IEnumerable(Of (obj As Type, schema As Dictionary(Of String, NilImplication)))
        Yield (GetType(ChatMessage), New Dictionary(Of String, NilImplication) From {
            {NameOf(ChatMessage.Role), NilImplication.Null},
            {NameOf(ChatMessage.Content), NilImplication.Null},
            {NameOf(ChatMessage.ToolCallId), NilImplication.Null},
            {NameOf(ChatMessage.ToolCalls), NilImplication.Null}
        })
        Yield (GetType(ToolCallInfo), New Dictionary(Of String, NilImplication) From {
            {NameOf(ToolCallInfo.DeepSeekDSMLLeak), NilImplication.Null},
            {NameOf(ToolCallInfo.Id), NilImplication.Null},
            {NameOf(ToolCallInfo.FunctionName), NilImplication.Null},
            {NameOf(ToolCallInfo.FunctionArguments), NilImplication.Null}
        })
    End Function
End Class

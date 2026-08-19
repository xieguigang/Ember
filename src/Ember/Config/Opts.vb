
Imports Microsoft.VisualBasic.CommandLine
Imports Microsoft.VisualBasic.CommandLine.Reflection

Namespace AgentRuntime

    ''' <summary>
    ''' Commandline options
    ''' </summary>
    Public Class Opts

        <Opt("--http")> Public Property http_mode As Boolean
        <Opt("--help")> Public Property help As Boolean
        <Opt("--port", "-p")> Public Property port As Integer
        <Opt("--wwwroot", "-d")> Public Property wwwroot As String

        Public Shared Function Build(args As String()) As Opts
            Return CommandLine.BuildFromArguments(args, NoSubCommand:=True).CreateOpts(Of Opts)
        End Function
    End Class
End Namespace
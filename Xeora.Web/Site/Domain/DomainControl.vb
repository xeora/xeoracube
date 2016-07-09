Option Strict On

Namespace Xeora.Web.Site
    Public Class DomainControl
        Implements IDisposable

        Private _RequestID As String
        Private Shared _DomainTable As Hashtable

        Private _ServiceID As String
        Private _ServiceType As [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes
        Private _IsAuthenticationRequired As Boolean
        Private _IsWorkingAsStandAlone As Boolean

        Private _MimeType As String
        Private _ExecuteIn As String
        Private _ServiceResult As String

        Public Sub New(ByVal RequestID As String, ByVal DomainIDAccessTree As String(), ByVal LanguageID As String)
            Me._RequestID = RequestID

            If DomainControl._DomainTable Is Nothing Then _
                DomainControl._DomainTable = Hashtable.Synchronized(New Hashtable())

            Threading.Monitor.Enter(DomainControl._DomainTable.SyncRoot)
            Try
                DomainControl._DomainTable.Item(RequestID) = New Domain(DomainIDAccessTree, LanguageID)
            Finally
                Threading.Monitor.Exit(DomainControl._DomainTable.SyncRoot)
            End Try

            Me._ServiceID = String.Empty
            Me._MimeType = String.Empty
            Me._ExecuteIn = String.Empty
            Me._IsAuthenticationRequired = False
            Me._IsWorkingAsStandAlone = False

            Me._ServiceResult = String.Empty

            [Shared].Helpers.CurrentDomainIDAccessTree = DomainControl.Domain(Me._RequestID).IDAccessTree
            [Shared].Helpers.CurrentDomainLanguageID = DomainControl.Domain(Me._RequestID).Language.ID
            [Shared].Globals.PageCaching.DefaultType = DomainControl.Domain(Me._RequestID).Settings.Configurations.DefaultCaching
        End Sub

        Public Shared ReadOnly Property Domain(ByVal RequestID As String) As [Shared].IDomain
            Get
                If String.IsNullOrEmpty(RequestID) OrElse
                    Not DomainControl._DomainTable.ContainsKey(RequestID) Then _
                    Return Nothing

                Return CType(DomainControl._DomainTable.Item(RequestID), [Shared].IDomain)
            End Get
        End Property

        Public ReadOnly Property XeoraJSVersion() As String
            Get
                Return My.Settings.ScriptVersion
            End Get
        End Property

        Public Sub ProvideXeoraJSStream(ByRef FileStream As IO.Stream)
            Dim CurrentAssembly As Reflection.Assembly =
                Reflection.Assembly.GetExecutingAssembly()

            FileStream = CurrentAssembly.GetManifestResourceStream(
                                String.Format("_sps_v{0}.js", Me.XeoraJSVersion))
        End Sub

        Public Property ServiceID() As String
            Get
                Return Me._ServiceID
            End Get
            Set(ByVal Value As String)
                Me._ServiceID = Value

                Me.PrepareServiceSettings()
            End Set
        End Property

        Public ReadOnly Property ServiceType() As [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes
            Get
                Return Me._ServiceType
            End Get
        End Property

        Public ReadOnly Property ServiceMimeType() As String
            Get
                Return Me._MimeType
            End Get
        End Property

        Public ReadOnly Property SocketEndPoint() As [Shared].Execution.BindInfo
            Get
                Dim rBindInfo As [Shared].Execution.BindInfo = Nothing

                If Me._ServiceType = [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xSocket AndAlso
                    Not String.IsNullOrEmpty(Me._ExecuteIn) Then _
                    rBindInfo = [Shared].Execution.BindInfo.Make(Me._ExecuteIn)

                Return rBindInfo
            End Get
        End Property

        Public ReadOnly Property IsAuthenticationRequired() As Boolean
            Get
                Return Me._IsAuthenticationRequired
            End Get
        End Property

        Public ReadOnly Property IsWorkingAsStandAlone() As Boolean
            Get
                Return Me._IsWorkingAsStandAlone
            End Get
        End Property

        Public ReadOnly Property URLMapping() As [Shared].URLMapping
            Get
                Dim rURLMapping As New [Shared].URLMapping

                rURLMapping.IsActive = DomainControl.Domain(Me._RequestID).Settings.URLMappings.IsActive
                rURLMapping.Items.AddRange(DomainControl.Domain(Me._RequestID).Settings.URLMappings.Items)

                For Each ChildDI As [Shared].DomainInfo In DomainControl.Domain(Me._RequestID).Children
                    Dim ChildDomainIDAccessTree As String() =
                        New String(DomainControl.Domain(Me._RequestID).IDAccessTree.Length) {}
                    Array.Copy(DomainControl.Domain(Me._RequestID).IDAccessTree, 0, ChildDomainIDAccessTree, 0, DomainControl.Domain(Me._RequestID).IDAccessTree.Length)
                    ChildDomainIDAccessTree(ChildDomainIDAccessTree.Length - 1) = ChildDI.ID

                    Dim WorkingInstance As [Shared].IDomain =
                        New Domain(ChildDomainIDAccessTree, DomainControl.Domain(Me._RequestID).Language.ID)

                    rURLMapping.IsActive = rURLMapping.IsActive Or WorkingInstance.Settings.URLMappings.IsActive

                    If WorkingInstance.Settings.URLMappings.IsActive Then
                        For Each mItem As [Shared].URLMapping.URLMappingItem In WorkingInstance.Settings.URLMappings.Items
                            Dim sItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                    [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                                    mItem.ResolveInfo.TemplateID
                                )

                            If sItem Is Nothing Then
                                rURLMapping.Items.Add(mItem)
                            Else
                                If sItem.Overridable Then
                                    For mIC_r As Integer = 0 To rURLMapping.Items.Count - 1
                                        If String.Compare(rURLMapping.Items.Item(mIC_r).ResolveInfo.TemplateID, mItem.ResolveInfo.TemplateID, True) = 0 Then
                                            rURLMapping.Items.RemoveAt(mIC_r)

                                            Exit For
                                        End If
                                    Next

                                    rURLMapping.Items.Add(mItem)
                                End If
                            End If
                        Next
                    End If

                    WorkingInstance.Dispose()
                Next

                If rURLMapping.IsActive Then
                    If rURLMapping.Items.Count = 0 Then rURLMapping.IsActive = False
                Else
                    rURLMapping.Items.Clear()
                End If

                Return rURLMapping
            End Get
        End Property

        Public Sub RenderService(ByVal MessageResult As [Shared].ControlResult.Message, ByVal UpdateBlockControlID As String)
            If String.IsNullOrEmpty(Me._ServiceID) Then
                Dim SystemMessage As String = DomainControl.Domain(Me._RequestID).Language.Get("TEMPLATE_IDMUSTBESET")

                If String.IsNullOrEmpty(SystemMessage) Then SystemMessage = [Global].SystemMessages.TEMPLATE_IDMUSTBESET

                Throw New System.Exception(SystemMessage & "!")
            End If

            Select Case Me._ServiceType
                Case [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template
                    Me._ServiceResult = DomainControl.Domain(Me._RequestID).Render(Me._ServiceID, MessageResult, UpdateBlockControlID)
                Case [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xService
                    If Me._IsAuthenticationRequired Then
                        Dim PostedExecuteParameters As [Shared].xService.Parameters =
                            New [Shared].xService.Parameters([Shared].Helpers.Context.Request.Form.Item("execParams"))

                        If Not PostedExecuteParameters.PublicKey Is Nothing Then
                            Me._IsAuthenticationRequired = False

                            Dim WorkingInstance As [Shared].IDomain = DomainControl.Domain(Me._RequestID)

                            Dim ServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem = Nothing
                            Do
                                ServiceItem =
                                    WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xService,
                                        Me._ServiceID
                                    )

                                If ServiceItem Is Nothing Then WorkingInstance = WorkingInstance.Parent
                            Loop Until WorkingInstance Is Nothing OrElse Not ServiceItem Is Nothing

                            If Not ServiceItem Is Nothing Then
                                If ServiceItem.Authentication AndAlso
                                    ServiceItem.AuthenticationKeys.Length = 0 Then

                                    ServiceItem.AuthenticationKeys =
                                        DomainControl.Domain(Me._RequestID).Settings.Services.ServiceItems.GetAuthenticationKeys()
                                End If

                                For Each AuthKey As String In ServiceItem.AuthenticationKeys
                                    If DomainControl.Domain(Me._RequestID).xService.ReadSessionVariable(PostedExecuteParameters.PublicKey, AuthKey) Is Nothing Then
                                        Me._IsAuthenticationRequired = True

                                        Exit For
                                    End If
                                Next
                            Else
                                Throw New NullReferenceException("Xeora Configuration does not contain any xService definition for this request!")
                            End If
                        End If
                    End If

                    If Not Me._IsAuthenticationRequired Then
                        Me._ServiceResult = DomainControl.Domain(Me._RequestID).xService.RenderxService(Me._ExecuteIn, Me._ServiceID)
                    Else
                        Dim MethodResult As Object =
                            New Security.SecurityException(
                                [Global].SystemMessages.XSERVICE_AUTH
                            )

                        Me._ServiceResult = DomainControl.Domain(Me._RequestID).xService.GeneratexServiceXML(MethodResult)
                    End If
            End Select
        End Sub

        Public ReadOnly Property ServiceResult As String
            Get
                Return Me._ServiceResult
            End Get
        End Property

        Public Shared Sub ProvideFileStream(ByRef FileStream As IO.Stream, ByVal RequestedFilePath As String)
            Dim WorkingInstance As Domain =
                CType(DomainControl.Domain([Shared].Helpers.CurrentRequestID), Domain)
            Do
                WorkingInstance.ProvideFileStream(FileStream, RequestedFilePath)

                If FileStream Is Nothing Then WorkingInstance = CType(WorkingInstance.Parent, Domain)
            Loop Until WorkingInstance Is Nothing OrElse Not FileStream Is Nothing
        End Sub

        Public Shared Function GetAvailableDomains() As [Shared].DomainInfo.DomainInfoCollection
            Dim rDomainInfoCollection As New [Shared].DomainInfo.DomainInfoCollection

            Try
                Dim DomainDI As IO.DirectoryInfo =
                    New IO.DirectoryInfo(
                        IO.Path.Combine(
                            [Shared].Configurations.PyhsicalRoot,
                            String.Format("{0}Domains", [Shared].Configurations.ApplicationRoot.FileSystemImplementation)
                        )
                    )

                For Each DI As IO.DirectoryInfo In DomainDI.GetDirectories()
                    Dim Languages As [Shared].DomainInfo.LanguageInfo() =
                        Deployment.DomainDeployment.AvailableLanguageInfos(New String() {DI.Name})

                    Dim DomainDeployment As Deployment.DomainDeployment =
                        New Deployment.DomainDeployment(New String() {DI.Name}, Languages(0).ID)

                    Dim DomainInfo As New [Shared].DomainInfo(DomainDeployment.DeploymentType, DI.Name, Languages)
                    DomainInfo.Children.AddRange(DomainDeployment.Children)

                    rDomainInfoCollection.Add(DomainInfo)
                Next
            Catch ex As System.Exception
                ' Just Handle Exceptions No Action Taken
            End Try

            Return rDomainInfoCollection
        End Function

        Public Sub ClearCache()
            CType(DomainControl.Domain(Me._RequestID), Domain).ClearCache()
        End Sub

        Private Sub PrepareServiceSettings()
            If String.IsNullOrEmpty(Me._ServiceID) Then _
                Me._ServiceID = DomainControl.Domain(Me._RequestID).Settings.Configurations.DefaultPage

            Dim WorkingInstance As [Shared].IDomain = DomainControl.Domain(Me._RequestID)

            ' Check ServiceID is for Template
            If CType(DomainControl.Domain(Me._RequestID), Domain).CheckTemplateExists(Me._ServiceID) Then
                ' This is a Template Request
                If String.Compare(Me._ServiceID, DomainControl.Domain(Me._RequestID).Settings.Configurations.AuthenticationPage, True) <> 0 Then
                    ' This is not an AuthenticationPage Request
                    Dim ServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                        DomainControl.Domain(Me._RequestID).Settings.Services.ServiceItems.GetServiceItem(
                            [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                            Me._ServiceID)

                    If Not ServiceItem Is Nothing Then
                        If ServiceItem.Overridable Then
                            WorkingInstance = Me.SearchChildrenThatOverrides(WorkingInstance, Me._ServiceID)

                            ' If not null, it means WorkingInstance contains a service definition
                            If Not WorkingInstance Is Nothing Then
                                Dim OverridableServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                    WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                                        Me._ServiceID
                                    )

                                ' Check overriding serviceitem requires authentication but does not have authenticationkeys. So add the current one to the new one
                                If OverridableServiceItem.Authentication AndAlso OverridableServiceItem.AuthenticationKeys.Length = 0 Then
                                    OverridableServiceItem.AuthenticationKeys = ServiceItem.AuthenticationKeys
                                End If

                                ServiceItem = OverridableServiceItem
                            End If
                        End If

                        Me._ServiceType = ServiceItem.ServiceType
                        If ServiceItem.Authentication Then
                            For Each AuthKey As String In ServiceItem.AuthenticationKeys
                                If [Shared].Helpers.Context.Session.Contents.Item(AuthKey) Is Nothing Then
                                    Me._IsAuthenticationRequired = True

                                    Exit For
                                End If
                            Next
                        End If
                        Me._IsWorkingAsStandAlone = ServiceItem.StandAlone
                        Me._ExecuteIn = ServiceItem.ExecuteIn
                        Me._MimeType = ServiceItem.MimeType
                    Else
                        Throw New NullReferenceException("Xeora Configuration does not contain any service definition for this request!")
                    End If
                Else
                    ' This is an AuthenticationPage Request
                    Dim ServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                        DomainControl.Domain(Me._RequestID).Settings.Services.ServiceItems.GetServiceItem(
                            [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                            Me._ServiceID)

                    If Not ServiceItem Is Nothing Then
                        Me._ServiceType = ServiceItem.ServiceType
                        ' Overrides that page does not need authentication even it's been marked as authentication required in Configuration definition
                        Me._IsAuthenticationRequired = False
                        Me._IsWorkingAsStandAlone = ServiceItem.StandAlone
                        Me._ExecuteIn = ServiceItem.ExecuteIn
                        Me._MimeType = ServiceItem.MimeType

                        If ServiceItem.Overridable Then
                            WorkingInstance = Me.SearchChildrenThatOverrides(WorkingInstance, Me._ServiceID)

                            ' If not null, it means WorkingInstance contains a service definition
                            If Not WorkingInstance Is Nothing Then
                                Dim OverridableServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                    WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                                        Me._ServiceID
                                    )

                                ' Check overriding serviceitem requires authentication but does not have authenticationkeys. So add the current one to the new one
                                If OverridableServiceItem.Authentication AndAlso OverridableServiceItem.AuthenticationKeys.Length = 0 Then
                                    OverridableServiceItem.AuthenticationKeys = ServiceItem.AuthenticationKeys
                                End If

                                ServiceItem = OverridableServiceItem
                            End If
                        End If
                    Else
                        Throw New NullReferenceException("Xeora Configuration does not contain any service definition for this request!")
                    End If
                End If
            Else
                ' This is a xSocket or xService request or ChildDomain Template, xSocket or xService Request
                ' Check first if it is a xService or not
                Dim ServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                    DomainControl.Domain(Me._RequestID).Settings.Services.ServiceItems.GetServiceItem(
                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xService,
                        Me._ServiceID)

                If Not ServiceItem Is Nothing Then
                    ' This is a xService Request
                    If ServiceItem.Overridable Then
                        WorkingInstance = Me.SearchChildrenThatOverrides(WorkingInstance, Me._ServiceID)

                        ' If not null, it means WorkingInstance contains a service definition
                        If Not WorkingInstance Is Nothing Then
                            Dim OverridableServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                    [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xService,
                                    Me._ServiceID
                                )

                            ' Overrides xService Definition
                            If Not OverridableServiceItem Is Nothing Then ServiceItem = OverridableServiceItem
                        End If
                    End If

                    Me._ServiceType = ServiceItem.ServiceType
                    Me._IsAuthenticationRequired = ServiceItem.Authentication
                    Me._IsWorkingAsStandAlone = ServiceItem.StandAlone
                    Me._ExecuteIn = ServiceItem.ExecuteIn
                    Me._MimeType = ServiceItem.MimeType
                Else
                    ServiceItem =
                        DomainControl.Domain(Me._RequestID).Settings.Services.ServiceItems.GetServiceItem(
                            [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xSocket,
                            Me._ServiceID)

                    If Not ServiceItem Is Nothing Then
                        ' This is a xService Request
                        If ServiceItem.Overridable Then
                            WorkingInstance = Me.SearchChildrenThatOverrides(WorkingInstance, Me._ServiceID)

                            ' If not null, it means WorkingInstance contains a service definition
                            If Not WorkingInstance Is Nothing Then
                                Dim OverridableServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                    [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xSocket,
                                    Me._ServiceID
                                )

                                ' Overrides xService Definition
                                If Not OverridableServiceItem Is Nothing Then ServiceItem = OverridableServiceItem
                            End If
                        End If

                        Me._ServiceType = ServiceItem.ServiceType
                        Me._IsAuthenticationRequired = ServiceItem.Authentication
                        Me._IsWorkingAsStandAlone = ServiceItem.StandAlone
                        Me._ExecuteIn = ServiceItem.ExecuteIn
                        Me._MimeType = ServiceItem.MimeType
                    Else
                        ' This is not xService or socket but it can be a template, xSocket or xService in Children
                        ' First Check if related Service Request exists in Children.
                        ' TODO: First most deep match returns. However, there should be some priority in the same depth
                        WorkingInstance = Me.SearchChildrenThatOverrides(WorkingInstance, Me._ServiceID)

                        If Not WorkingInstance Is Nothing Then
                            ' Set the Working domain as child domain for this call because call requires the child domain access!
                            Threading.Monitor.Enter(DomainControl._DomainTable.SyncRoot)
                            Try
                                If DomainControl._DomainTable.ContainsKey(Me._RequestID) Then _
                                    DomainControl._DomainTable.Item(Me._RequestID) = WorkingInstance
                            Finally
                                Threading.Monitor.Exit(DomainControl._DomainTable.SyncRoot)
                            End Try

                            ' Okay Something Exists. But is it a Template or xService
                            ' First Check if it is a Template
                            Dim ChildServiceItem As [Shared].IDomain.ISettings.IServices.IServiceItem =
                                WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                    [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                                    Me._ServiceID
                                )

                            If Not ChildServiceItem Is Nothing Then
                                ' Okay this is a child Template
                                ServiceItem = ChildServiceItem
                            Else
                                ' Hmm Let me check for xService and xSocket So
                                ChildServiceItem =
                                    WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xService,
                                        Me._ServiceID
                                    )

                                If Not ChildServiceItem Is Nothing Then
                                    ' Okay this is a child xService
                                    ServiceItem = ChildServiceItem
                                Else
                                    ChildServiceItem =
                                        WorkingInstance.Settings.Services.ServiceItems.GetServiceItem(
                                            [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.xSocket,
                                            Me._ServiceID
                                        )

                                    If Not ChildServiceItem Is Nothing Then
                                        ' Okay this is a child Socket
                                        ServiceItem = ChildServiceItem
                                    Else
                                        '' Nothing found Anywhere!
                                        '[Shared].Helpers.Context.Response.StatusCode = 404

                                        Me._ServiceID = String.Empty

                                        Exit Sub
                                    End If
                                End If
                            End If
                        End If

                        If Not ServiceItem Is Nothing Then
                            ' Let work on Found Service Item in Children

                            Me._ServiceType = ServiceItem.ServiceType
                            If ServiceItem.Authentication Then
                                For Each AuthKey As String In ServiceItem.AuthenticationKeys
                                    If [Shared].Helpers.Context.Session.Contents.Item(AuthKey) Is Nothing Then
                                        Me._IsAuthenticationRequired = True

                                        Exit For
                                    End If
                                Next
                            End If
                            Me._IsWorkingAsStandAlone = ServiceItem.StandAlone
                            Me._ExecuteIn = ServiceItem.ExecuteIn
                            Me._MimeType = ServiceItem.MimeType
                        Else
                            Me._ServiceID = String.Empty
                        End If
                    End If
                End If
            End If
        End Sub

        Private Function SearchChildrenThatOverrides(ByRef WorkingInstance As [Shared].IDomain, ByVal ServiceID As String) As [Shared].IDomain
            Dim rDomainInstance As [Shared].IDomain = Nothing

            For Each ChildDI As [Shared].DomainInfo In WorkingInstance.Children
                Dim ChildDomainIDAccessTree As String() =
                        New String(DomainControl.Domain(Me._RequestID).IDAccessTree.Length) {}
                Array.Copy(DomainControl.Domain(Me._RequestID).IDAccessTree, 0, ChildDomainIDAccessTree, 0, DomainControl.Domain(Me._RequestID).IDAccessTree.Length)
                ChildDomainIDAccessTree(ChildDomainIDAccessTree.Length - 1) = ChildDI.ID

                rDomainInstance = New Domain(ChildDomainIDAccessTree, DomainControl.Domain(Me._RequestID).Language.ID)

                If rDomainInstance.Settings.Services.ServiceItems.GetServiceItem(
                        [Shared].IDomain.ISettings.IServices.IServiceItem.ServiceTypes.Template,
                        ServiceID) Is Nothing Then

                    If rDomainInstance.Children.Count > 0 Then
                        rDomainInstance = Me.SearchChildrenThatOverrides(rDomainInstance, ServiceID)

                        If Not rDomainInstance Is Nothing Then Exit For
                    End If
                Else
                    If rDomainInstance.Children.Count > 0 Then _
                        rDomainInstance = Me.SearchChildrenThatOverrides(rDomainInstance, ServiceID)

                    If rDomainInstance Is Nothing Then _
                        rDomainInstance = New Domain(rDomainInstance.IDAccessTree, WorkingInstance.Language.ID)

                    Exit For
                End If
            Next

            Return rDomainInstance
        End Function

        Private disposedValue As Boolean = False        ' To detect redundant calls

        ' IDisposable
        Protected Overridable Sub Dispose(ByVal disposing As Boolean)
            If Not Me.disposedValue Then
                DomainControl.Domain(Me._RequestID).Dispose()

                Threading.Monitor.Enter(DomainControl._DomainTable.SyncRoot)
                Try
                    If DomainControl._DomainTable.ContainsKey(Me._RequestID) Then _
                        DomainControl._DomainTable.Remove(Me._RequestID)
                Finally
                    Threading.Monitor.Exit(DomainControl._DomainTable.SyncRoot)
                End Try
            End If

            Me.disposedValue = True
        End Sub

#Region " IDisposable Support "
        ' This code added by Visual Basic to correctly implement the disposable pattern.
        Public Sub Dispose() Implements IDisposable.Dispose
            ' Do not change this code.  Put cleanup code in Dispose(ByVal disposing As Boolean) above.
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

    End Class
End Namespace
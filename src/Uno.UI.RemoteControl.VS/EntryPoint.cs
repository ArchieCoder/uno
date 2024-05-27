using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Internal.VisualStudio.Shell;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Telemetry;
using Microsoft.VisualStudio.Telemetry.SessionChannel;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;
using Uno.UI.RemoteControl.Messaging.IdeChannel;
using Uno.UI.RemoteControl.VS.DebuggerHelper;
using Uno.UI.RemoteControl.VS.Helpers;
using Uno.UI.RemoteControl.VS.IdeChannel;
using Debugger = System.Diagnostics.Debugger;
using ILogger = Uno.UI.RemoteControl.VS.Helpers.ILogger;
using Process = EnvDTE.Process;
using StackFrame = EnvDTE.StackFrame;
using Task = System.Threading.Tasks.Task;
using Thread = EnvDTE.Thread;

#pragma warning disable VSTHRD010
#pragma warning disable VSTHRD109

namespace Uno.UI.RemoteControl.VS;

public partial class EntryPoint : IDisposable
{
	private const string UnoPlatformOutputPane = "Uno Platform";
	private const string RemoteControlServerPortProperty = "UnoRemoteControlPort";
	private const string UnoVSExtensionLoadedProperty = "_UnoVSExtensionLoaded";

	private readonly CancellationTokenSource _ct = new();
	private readonly DTE _dte;
	private readonly DTE2 _dte2;
	private readonly string _toolsPath;
	private readonly AsyncPackage _asyncPackage;
	private Action<string>? _debugAction;
	private Action<string>? _infoAction;
	private Action<string>? _verboseAction;
	private Action<string>? _warningAction;
	private Action<string>? _errorAction;
	private int _msBuildLogLevel;
	private System.Diagnostics.Process? _process;

	private int RemoteControlServerPort;
	private bool _closing;
	private bool _isDisposed;
	private IdeChannelClient? _ideChannelClient;
	private ProfilesObserver _debuggerObserver;
	private readonly Func<Task> _globalPropertiesChanged;
	private readonly _dispSolutionEvents_BeforeClosingEventHandler _closeHandler;
	private readonly _dispBuildEvents_OnBuildDoneEventHandler _onBuildDoneHandler;
	private readonly _dispBuildEvents_OnBuildProjConfigBeginEventHandler _onBuildProjConfigBeginHandler;

	public EntryPoint(
		DTE2 dte2
		, string toolsPath
		, AsyncPackage asyncPackage
		, Action<Func<Task<Dictionary<string, string>>>> globalPropertiesProvider
		, Func<Task> globalPropertiesChanged)
	{
		_dte = dte2 as DTE;
		_dte2 = dte2;
		_toolsPath = toolsPath;
		_asyncPackage = asyncPackage;
		globalPropertiesProvider(OnProvideGlobalPropertiesAsync);
		_globalPropertiesChanged = globalPropertiesChanged;

		SetupOutputWindow();

		_closeHandler = () => SolutionEvents_BeforeClosing();
		_dte.Events.SolutionEvents.BeforeClosing += _closeHandler;

		_onBuildDoneHandler = (s, a) => BuildEvents_OnBuildDone(s, a);
		_dte.Events.BuildEvents.OnBuildDone += _onBuildDoneHandler;

		_onBuildProjConfigBeginHandler = (string project, string projectConfig, string platform, string solutionConfig) => _ = BuildEvents_OnBuildProjConfigBeginAsync(project, projectConfig, platform, solutionConfig);
		_dte.Events.BuildEvents.OnBuildProjConfigBegin += _onBuildProjConfigBeginHandler;

		// Start the RC server early, as iOS and Android projects capture the globals early
		// and don't recreate it unless out-of-process msbuild.exe instances are terminated.
		//
		// This will can possibly be removed when all projects are migrated to the sdk project system.
		_ = UpdateProjectsAsync();

		_debuggerObserver = new ProfilesObserver(
			asyncPackage
			, _dte
			, (previous, newFramework) => OnDebugFrameworkChangedAsync(previous, newFramework)
			, OnDebugProfileChangedAsync
			, OnStartupProjectChangedAsync
			, _debugAction);

		_ = _debuggerObserver.ObserveProfilesAsync();

		_ = _globalPropertiesChanged();


		TelemetryHelper.DataModelTelemetrySession.AddSessionChannel(new DevServerChannel(this));

//		var commands = dte2
//			.Commands
//			.Cast<Command>()
//			.GroupBy(c => c.Name)
//			.OrderBy(g => g.Key ?? "--no-name--")
//			.ToDictionary(g => g.Key ?? "--no-name--", g => g.ToArray());


//		var hrCommand = commands["Debug.ApplyCodeChanges"];
//		var hrCmdGuid = hrCommand.First().Guid;
//		var hrCmdID = hrCommand.First().ID;

//		_CommandEvents_BeforeExecute = (string guid, int id, object customin, object customout, ref bool canceldefault) => CommandEvents_BeforeExecute(guid, id, customin, customout, ref canceldefault);
//		_CommandEvents_AfterExecute = (string Guid, int ID, object CustomIn, object CustomOut) => CommandEvents_AfterExecute(Guid, ID, CustomIn, CustomOut);
//		_OutputWindowEvents_PaneAdded = OutputWindowEvents_PaneAdded;
//		_OutputWindowEvents_PaneUpdated = OutputWindowEvents_PaneUpdated;
//		_OnHotReloadEvent = OnHotReloadEvent;
//		_BuildEvents_OnBuildBegin = BuildEvents_OnBuildBegin;
//		_OnDebuggerCtxChnaged = OnDebuggerCtxChnaged;
//		_OnDebuggerDesignMode = OnDebuggerDesignMode;


//		dte2.Events.CommandEvents["{C9DD4A59-47FB-11D2-83E7-00C04F9902C1}", 261].BeforeExecute += _CommandEvents_BeforeExecute;
//		dte2.Events.CommandEvents["{C9DD4A59-47FB-11D2-83E7-00C04F9902C1}", 261].AfterExecute += _CommandEvents_AfterExecute;
//		dte2.Events.CommandEvents[Guid: "{C9DD4A59-47FB-11D2-83E7-00C04F9902C1}"].BeforeExecute += _CommandEvents_BeforeExecute;
//		dte2.Events.CommandEvents[Guid: "{C9DD4A59-47FB-11D2-83E7-00C04F9902C1}"].AfterExecute += _CommandEvents_AfterExecute;
//		dte2.Events.CommandEvents[ID: 261].BeforeExecute += _CommandEvents_BeforeExecute;
//		dte2.Events.CommandEvents[ID: 261].AfterExecute += _CommandEvents_AfterExecute;
//		dte2.Events.CommandEvents.BeforeExecute += _CommandEvents_BeforeExecute;
//		dte2.Events.CommandEvents.AfterExecute += _CommandEvents_AfterExecute;
//		dte2.Events.OutputWindowEvents.PaneAdded += _OutputWindowEvents_PaneAdded;
//		dte2.Events.OutputWindowEvents.PaneUpdated += _OutputWindowEvents_PaneUpdated;
//		dte2.Events.OutputWindowEvents["Hot Reload"].PaneUpdated += _OnHotReloadEvent;// .PaneUpdated .PaneAdded += OutputWindowEvents_PaneAdded;

//		dte2.Events.BuildEvents.OnBuildBegin += _BuildEvents_OnBuildBegin;

//		dte2.Events.DebuggerEvents.OnContextChanged += _OnDebuggerCtxChnaged;
//		dte2.Events.DebuggerEvents.OnEnterDesignMode += _OnDebuggerDesignMode;


//		var outputs = _dte2.ToolWindows.OutputWindow
//			.OutputWindowPanes
//			.OfType<OutputWindowPane>()
//			.ToDictionary(p => p.Name ?? Guid.NewGuid().ToString());

//		outputs.ToString();

//		//_dte2.ToolWindows.OutputWindow.OutputWindowPanes["Hot Reload"].
//#pragma warning disable
//		//_ = ready.ContinueWith(t =>
//		//{
			
//		//});
	}

	//_dispCommandEvents_BeforeExecuteEventHandler _CommandEvents_BeforeExecute;
	//_dispCommandEvents_AfterExecuteEventHandler _CommandEvents_AfterExecute;
	//_dispOutputWindowEvents_PaneAddedEventHandler _OutputWindowEvents_PaneAdded;
	//_dispOutputWindowEvents_PaneUpdatedEventHandler _OutputWindowEvents_PaneUpdated;
	//_dispOutputWindowEvents_PaneUpdatedEventHandler _OnHotReloadEvent;
	//_dispBuildEvents_OnBuildBeginEventHandler _BuildEvents_OnBuildBegin;
	//_dispDebuggerEvents_OnContextChangedEventHandler _OnDebuggerCtxChnaged;
	//_dispDebuggerEvents_OnEnterDesignModeEventHandler _OnDebuggerDesignMode;



	//private void OnDebuggerDesignMode(dbgEventReason reason)
	//{
	//	"".ToString();
	//}

	//private bool _isSet;

	//private void OnDebuggerCtxChnaged(Process newprocess, Program newprogram, Thread newthread, StackFrame newstackframe)
	//{
	//	if (_isSet)
	//	{
	//		return;
	//	}

	//	"".ToString();

	//	var notif = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadNotificationService, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	TryResolve();


	//	if (!_isSet && notif.GetType().GetEvent("HotReloadStateChangedAsync") is { } evt)
	//	{
	//		_isSet = true;

	//		TelemetryHelper.DataModelTelemetrySession.AddSessionChannel(new MyChannel("data-model"));
	//		if (Microsoft.Internal.VisualStudio.Shell.TelemetryHelper.DefaultTelemetrySession is TelemetrySession session)
	//		{
	//			session.AddSessionChannel(new MyChannel("default"));
	//		}

	//		if (Microsoft.Internal.VisualStudio.Shell.TelemetryHelper.DefaultTelemetrySession is {} vsDefaultSession
	//			&& vsDefaultSession.GetType().GetProperty("SessionAdaptee", BindingFlags.Instance | BindingFlags.NonPublic) is {GetMethod: {} getAdaptee } 
	//			&& getAdaptee.Invoke(vsDefaultSession, null) is TelemetrySession defaultSession)
	//		{
	//			defaultSession.AddSessionChannel(new MyChannel("default"));
	//		}
			

	//		var svc = Microsoft.Internal.VisualStudio.Shell.TelemetryHelper.TelemetryService.GetType();
	//		svc.ToString();

	//		evt.AddEventHandler(notif, new AsyncEventHandler<bool>(async (s, e) =>
	//		{
	//			"".ToString();

	//			TryResolve();

	//			//_dte2.Events.OutputWindowEvents["Hot Reload"]
	//			var outputs = _dte2.ToolWindows.OutputWindow
	//				.OutputWindowPanes
	//				.OfType<OutputWindowPane>()
	//				.ToDictionary(p => p.Name ?? Guid.NewGuid().ToString());

	//			var abc = outputs["Hot Reload"];

	//			//abc.TextDocument.
	//		}));
	//	}

	//	//applier?.ToString();

	//	//var outputs = _dte2.ToolWindows.OutputWindow
	//	//	.OutputWindowPanes
	//	//	.OfType<OutputWindowPane>()
	//	//	.ToDictionary(p => p.Name ?? Guid.NewGuid().ToString());

	//	//var abc = outputs["Hot Reload"];
	//}

	//private class MyChannel : ISessionChannel
	//{
	//	private readonly string _name;

	//	public MyChannel(string name)
	//	{
	//		_name = name;
	//	}

	//	/// <inheritdoc />
	//	public void PostEvent(TelemetryEvent telemetryEvent)
	//	{
	//		if (telemetryEvent.Name.StartsWith("vs/diagnostics/debugger") || telemetryEvent.Name.StartsWith("vs/core/command"))
	//		{
	//			Debug.WriteLine($"********* ({_name}) {telemetryEvent.Name}: src:{telemetryEvent.DataSource} {telemetryEvent.Severity} {telemetryEvent.EventType} \r\n\t{string.Join("\r\n\t-", telemetryEvent.Properties.Select(p => $"{p.Key}: {p.Value}"))} ");
	//		}
	//		else
	//		{
	//			Debug.WriteLine($"********* ({_name}) {telemetryEvent.Name}");
	//		}

	//		switch (telemetryEvent.Name)
	//		{
	//			case "vs/diagnostics/debugger/enccomplete":
	//				// Hot relaod completed! 
	//				// WRANING: ERROR MAY COME AFTER
	//				break;

	//			case "vs/diagnostics/debugger/enc/nochanges":
	//				break;

	//			case "vs/diagnostics/debugger/enc/error":
	//				break;

	//			case "vs/diagnostics/debugger/hotreloaddialog/buttonclick":
	//				break;
	//		}

	//		if (telemetryEvent.Name.Equals("VS/Diagnostics/Debugger/Enc/Error", StringComparison.OrdinalIgnoreCase))
	//		{
	//			"".ToString();
	//		}
	//	}

	//	/// <inheritdoc />
	//	public void PostEvent(TelemetryEvent telemetryEvent, IEnumerable<ITelemetryManifestRouteArgs> args)
	//	{
	//		if (telemetryEvent.Name.Equals("VS/Diagnostics/Debugger/Enc/Error", StringComparison.OrdinalIgnoreCase))
	//		{
	//			"".ToString();
	//		}
	//	}

	//	/// <inheritdoc />
	//	public void Start(string sessionId)
	//	{

	//	}

	//	/// <inheritdoc />
	//	public string ChannelId { get; } = "Uno platform hot-reload client application";

	//	/// <inheritdoc />
	//	public string TransportUsed { get; } = "Local_TCP";

	//	/// <inheritdoc />
	//	public ChannelProperties Properties { get; set; } = ChannelProperties.DevChannel;

	//	/// <inheritdoc />
	//	public bool IsStarted { get; } = true;
	//}


	//private void TryResolve()
	//{
	//	var agent = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadAgent, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	var notif = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadNotificationService, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	var applier = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadUpdateApplier, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	var sessMan = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadSessionManager, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	var session = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadSession, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
	//	var sessionCallback = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IProjectHotReloadSessionCallback, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");

	//	var outputSvc = _debuggerObserver.GetService("Microsoft.VisualStudio.ProjectSystem.VS.HotReload.IHotReloadDiagnosticOutputService, Microsoft.VisualStudio.ProjectSystem.Managed.VS, Version=17.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");


	//	var log = _debuggerObserver.GetService("Microsoft.VisualStudio.Debugger.Contracts.HotReload.IHotReloadLogger, Microsoft.VisualStudio.Debugger.Contracts, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
	//	var debugAgent = _debuggerObserver.GetService("Microsoft.VisualStudio.Debugger.Contracts.HotReload.IManagedHotReloadAgent, Microsoft.VisualStudio.Debugger.Contracts, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
	//	var debugAgentMan = _debuggerObserver.GetService("Microsoft.VisualStudio.Debugger.Contracts.HotReload.IHotReloadAgentManagerClient, Microsoft.VisualStudio.Debugger.Contracts, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
	//}

	//private void OutputWindowEvents_PaneUpdated(OutputWindowPane ppane)
	//{
	//	ppane.Name.ToString();
	//}

	//private void CommandEvents_BeforeExecute(string guid, int id, object customin, object customout, ref bool canceldefault)
	//{
	//	"".ToString();
	//}

	//private void CommandEvents_AfterExecute(string Guid, int ID, object CustomIn, object CustomOut)
	//{
	//	"".ToString();
	//}

	//private void OnHotReloadEvent(OutputWindowPane pane)
	//{
	//	Debugger.Break();
	//}

	//private void BuildEvents_OnBuildBegin(vsBuildScope scope, vsBuildAction action)
	//{
	//	Debugger.Break();
	//}

	//private void OutputWindowEvents_PaneAdded(OutputWindowPane pPane)
	//{
	//	Debugger.Break();
	//}

	private async Task<Dictionary<string, string>> OnProvideGlobalPropertiesAsync()
	{
		Dictionary<string, string> properties = new()
		{
			[UnoVSExtensionLoadedProperty] = "true"
		};

		if (RemoteControlServerPort != 0)
		{
			properties.Add(RemoteControlServerPortProperty, RemoteControlServerPort.ToString(CultureInfo.InvariantCulture));
		}

		await Task.Yield();

		return properties;
	}

	[MemberNotNull(
		nameof(_debugAction)
		, nameof(_infoAction)
		, nameof(_verboseAction)
		, nameof(_warningAction)
		, nameof(_errorAction))]
	private void SetupOutputWindow()
	{
		var ow = _dte2.ToolWindows.OutputWindow;

		_msBuildLogLevel = _dte2.GetMSBuildOutputVerbosity();

		// Add a new pane to the Output window.
		var owPane = ow
			.OutputWindowPanes
			.OfType<OutputWindowPane>()
			.FirstOrDefault(p => p.Name == UnoPlatformOutputPane);

		if (owPane == null)
		{
			owPane = ow
			.OutputWindowPanes
			.Add(UnoPlatformOutputPane);
		}

		_debugAction = s =>
		{
			if (!_closing && _msBuildLogLevel >= 3 /* MSBuild Log Detailed */)
			{
				owPane.OutputString("[DEBUG] " + s + "\r\n");
			}
		};
		_infoAction = s =>
		{
			if (!_closing && _msBuildLogLevel >= 2 /* MSBuild Log Normal */)
			{
				owPane.OutputString("[INFO] " + s + "\r\n");
			}
		};
		_verboseAction = s =>
		{
			if (!_closing && _msBuildLogLevel >= 4 /* MSBuild Log Diagnostic */)
			{
				owPane.OutputString("[VERBOSE] " + s + "\r\n");
			}
		};
		_warningAction = s =>
		{
			if (!_closing && _msBuildLogLevel >= 1 /* MSBuild Log Minimal */)
			{
				owPane.OutputString("[WARNING] " + s + "\r\n");
			}
		};
		_errorAction = e =>
		{
			if (!_closing && _msBuildLogLevel >= 0 /* MSBuild Log Quiet */)
			{
				owPane.OutputString("[ERROR] " + e + "\r\n");
			}
		};

		_infoAction($"Uno Remote Control initialized ({GetAssemblyVersion()})");
	}

	private object GetAssemblyVersion()
	{
		var assembly = GetType().GetTypeInfo().Assembly;

		if (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is AssemblyInformationalVersionAttribute aiva)
		{
			return aiva.InformationalVersion;
		}
		else if (assembly.GetCustomAttribute<AssemblyVersionAttribute>() is AssemblyVersionAttribute ava)
		{
			return ava.Version;
		}
		else
		{
			return "Unknown";
		}
	}

	private async Task BuildEvents_OnBuildProjConfigBeginAsync(string project, string projectConfig, string platform, string solutionConfig)
	{
		await UpdateProjectsAsync();
	}

	private async Task UpdateProjectsAsync()
	{
		try
		{
			StartServer();
			var portString = RemoteControlServerPort.ToString(CultureInfo.InvariantCulture);
			foreach (var p in await _dte.GetProjectsAsync())
			{
				var filename = string.Empty;
				try
				{
					filename = p.FileName;
				}
				catch (Exception ex)
				{
					_debugAction?.Invoke($"Exception on retrieving {p.UniqueName} details. Err: {ex}.");
					_warningAction?.Invoke($"Cannot read {p.UniqueName} project details (It may be unloaded).");
				}
				if (string.IsNullOrWhiteSpace(filename) == false
					&& GetMsbuildProject(filename) is Microsoft.Build.Evaluation.Project msbProject
					&& IsApplication(msbProject))
				{
					SetGlobalProperty(filename, RemoteControlServerPortProperty, portString);
				}
			}
		}
		catch (Exception e)
		{
			_debugAction?.Invoke($"UpdateProjectsAsync failed: {e}");
		}
	}

	private void BuildEvents_OnBuildDone(vsBuildScope Scope, vsBuildAction Action)
	{
		StartServer();
	}

	private void SolutionEvents_BeforeClosing()
	{
		// Detach event handler to avoid this being called multiple times
		_dte.Events.SolutionEvents.BeforeClosing -= _closeHandler;

		if (_process is not null)
		{
			try
			{
				_debugAction?.Invoke($"Terminating Remote Control server (pid: {_process.Id})");
				_process.Kill();
				_debugAction?.Invoke($"Terminated Remote Control server (pid: {_process.Id})");

				_ideChannelClient?.Dispose();
				_ideChannelClient = null;
			}
			catch (Exception e)
			{
				_debugAction?.Invoke($"Failed to terminate Remote Control server (pid: {_process.Id}): {e}");
			}
			finally
			{
				_closing = true;
				_process = null;

				// Invoke Dispose to make sure other event handlers are detached
				Dispose();
			}
		}
	}

	private int GetDotnetMajorVersion()
	{
		var result = ProcessHelpers.RunProcess("dotnet", "--version", Path.GetDirectoryName(_dte.Solution.FileName));

		if (result.exitCode != 0)
		{
			throw new InvalidOperationException($"Unable to detect current dotnet version (\"dotnet --version\" exited with code {result.exitCode})");
		}

		if (result.output.Contains("."))
		{
			if (int.TryParse(result.output.Substring(0, result.output.IndexOf('.')), out int majorVersion))
			{
				return majorVersion;
			}
		}

		throw new InvalidOperationException($"Unable to detect current dotnet version (\"dotnet --version\" returned \"{result.output}\")");
	}

	private void StartServer()
	{
		if (_process?.HasExited ?? true)
		{
			RemoteControlServerPort = GetTcpPort();

			var version = GetDotnetMajorVersion();
			if (version < 7)
			{
				throw new InvalidOperationException($"Unsupported dotnet version ({version}) detected");
			}
			var runtimeVersionPath = $"net{version}.0";

			var pipeGuid = Guid.NewGuid();

			var hostBinPath = Path.Combine(_toolsPath, "host", runtimeVersionPath, "Uno.UI.RemoteControl.Host.dll");
			var arguments = $"\"{hostBinPath}\" --httpPort {RemoteControlServerPort} --ppid {System.Diagnostics.Process.GetCurrentProcess().Id} --ideChannel \"{pipeGuid}\"";
			var pi = new ProcessStartInfo("dotnet", arguments)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.Combine(_toolsPath, "host"),

				// redirect the output
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			_process = new System.Diagnostics.Process();

			// hookup the event handlers to capture the data that is received
			_process.OutputDataReceived += (sender, args) => _debugAction?.Invoke(args.Data);
			_process.ErrorDataReceived += (sender, args) => _errorAction?.Invoke(args.Data);

			_process.StartInfo = pi;
			_process.Start();

			// start our event pumps
			_process.BeginOutputReadLine();
			_process.BeginErrorReadLine();

			_ideChannelClient = new IdeChannelClient(pipeGuid, new Logger(this));
			_ideChannelClient.ForceHotReloadRequested += OnForceHotReloadRequestedAsync;
			_ideChannelClient.ConnectToHost();

			_ = _globalPropertiesChanged();
		}
	}

	private async Task OnForceHotReloadRequestedAsync(object? sender, ForceHotReloadIdeMessage request)
	{
		try
		{
			_dte.ExecuteCommand("Debug.ApplyCodeChanges");

			// Send a message back to indicate that the request has been received and acted upon.
			if (_ideChannelClient is not null)
			{
				await _ideChannelClient.SendToDevServerAsync(new HotReloadRequestedIdeMessage(request.Id, Result.Success()), _ct.Token);
			}
		}
		catch (Exception e) when (_ideChannelClient is not null)
		{
			await _ideChannelClient.SendToDevServerAsync(new HotReloadRequestedIdeMessage(request.Id, Result.Fail(e)), _ct.Token);

			throw;
		}
	}

	private static int GetTcpPort()
	{
		var l = new TcpListener(IPAddress.Loopback, 0);
		l.Start();
		var port = ((IPEndPoint)l.LocalEndpoint).Port;
		l.Stop();
		return port;
	}

	public void SetGlobalProperty(string projectFullName, string propertyName, string propertyValue)
	{
		var msbuildProject = GetMsbuildProject(projectFullName);
		if (msbuildProject == null)
		{
			_debugAction?.Invoke($"Failed to find project {projectFullName}, cannot provide listen port to the app.");
		}
		else
		{
			SetGlobalProperty(msbuildProject, propertyName, propertyValue);
		}
	}

	private static Microsoft.Build.Evaluation.Project GetMsbuildProject(string projectFullName)
		=> ProjectCollection.GlobalProjectCollection.GetLoadedProjects(projectFullName).FirstOrDefault();

	public void SetGlobalProperties(string projectFullName, IDictionary<string, string> properties)
	{
		var msbuildProject = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(projectFullName).FirstOrDefault();
		if (msbuildProject == null)
		{
			_debugAction?.Invoke($"Failed to find project {projectFullName}, cannot provide listen port to the app.");
		}
		else
		{
			foreach (var property in properties)
			{
				SetGlobalProperty(msbuildProject, property.Key, property.Value);
			}
		}
	}

	private void SetGlobalProperty(Microsoft.Build.Evaluation.Project msbuildProject, string propertyName, string propertyValue)
	{
		msbuildProject.SetGlobalProperty(propertyName, propertyValue);

	}

	private bool IsApplication(Microsoft.Build.Evaluation.Project project)
	{
		var outputType = project.GetPropertyValue("OutputType");
		return outputType is not null &&
   				(outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase));
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;

		try
		{
			_ct.Cancel(false);
			_dte.Events.BuildEvents.OnBuildDone -= _onBuildDoneHandler;
			_dte.Events.BuildEvents.OnBuildProjConfigBegin -= _onBuildProjConfigBeginHandler;
		}
		catch (Exception e)
		{
			_debugAction?.Invoke($"Failed to dispose Remote Control server: {e}");
		}
	}

	private class Logger(EntryPoint entryPoint) : ILogger
	{
		public void Debug(string message) => entryPoint._debugAction?.Invoke(message);
		public void Error(string message) => entryPoint._errorAction?.Invoke(message);
		public void Info(string message) => entryPoint._infoAction?.Invoke(message);
		public void Warn(string message) => entryPoint._warningAction?.Invoke(message);
		public void Verbose(string message) => entryPoint._verboseAction?.Invoke(message);
	}

	private class DevServerChannel(EntryPoint ide) : ISessionChannel
	{
		/// <inheritdoc />
		public string ChannelId => "Uno platform hot-reload client application";

		/// <inheritdoc />
		public string TransportUsed => "Local_TCP";

		/// <inheritdoc />
		public ChannelProperties Properties { get; set; } = ChannelProperties.DevChannel;

		/// <inheritdoc />
		public bool IsStarted => true;

		/// <inheritdoc />
		public void Start(string sessionId) { }

		/// <inheritdoc />
		public void PostEvent(TelemetryEvent telemetryEvent)
			=> TryForward(telemetryEvent);

		/// <inheritdoc />
		public void PostEvent(TelemetryEvent telemetryEvent, IEnumerable<ITelemetryManifestRouteArgs> args)
			=> TryForward(telemetryEvent);

		private void TryForward(TelemetryEvent telemetryEvent)
		{
			if (ide is not { _ideChannelClient: { } client, _isDisposed: false, _ct: { IsCancellationRequested: false } ct })
			{
				return;
			}

			switch (telemetryEvent.Name)
			{
				case "vs/diagnostics/debugger/enccomplete":
					_ = client.SendToDevServerAsync(new HotReloadEventIdeMessage(HotReloadEvent.Completed), ct.Token);
					break;

				case "vs/diagnostics/debugger/enc/nochanges":
					_ = client.SendToDevServerAsync(new HotReloadEventIdeMessage(HotReloadEvent.NoChanges), ct.Token);
					break;

				case "vs/diagnostics/debugger/enc/error":
					_ = client.SendToDevServerAsync(new HotReloadEventIdeMessage(HotReloadEvent.Failed), ct.Token);
					break;

				case "vs/diagnostics/debugger/hotreloaddialog/buttonclick":
					_ = client.SendToDevServerAsync(new HotReloadEventIdeMessage(HotReloadEvent.CannotApplyDialogButton), ct.Token);
					break;
			}
		}
	}
}

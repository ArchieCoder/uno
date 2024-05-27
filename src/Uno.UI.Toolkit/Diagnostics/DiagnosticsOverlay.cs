#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Uno.Diagnostics.UI;



//public class EventSink : Uno.Services.Diagnostics.Eventing.IEventSink
//{

//}

public sealed partial class DiagnosticsOverlay
{
	private static readonly ConditionalWeakTable<XamlRoot, DiagnosticsOverlay> _overlays = new();
	//private static ImmutableList<GlobalProviderRegistration> _globalProviders = ImmutableList<GlobalProviderRegistration>.Empty;

	/// <summary>
	/// Gets the <see cref="DiagnosticsOverlay"/> for the specified <see cref="XamlRoot"/>.
	/// </summary>
	/// <param name="root">The root onto which the overlay is being rendered.</param>
	/// <returns></returns>
	public static DiagnosticsOverlay Get(XamlRoot root)
		=> _overlays.GetValue(root, static r => new DiagnosticsOverlay(r));

	///// <summary>
	///// Register a global diagnostic provider that can be displayed on any window.
	///// </summary>
	///// <param name="name">Name of the diagnostic (this should be a friendly name to be visible as tooltip on the minimized view).</param>
	///// <param name="preview">Minimized view of the diagnostic display directly as overlay on the application.</param>
	///// <param name="details">Optional details view displayed when the minimized view is tapped.</param>
	//public static void Register(string name, Func<IDiagnosticViewContext, UIElement> preview, Func<IDiagnosticViewContext, CancellationToken, ValueTask<object?>>? details = null)
	//	=> Register(new GenericProvider(name, preview, details));

	///// <summary>
	///// Register a global diagnostic provider that can be displayed on any window.
	///// </summary>
	///// <param name="provider">A diagnostic provider to display.</param>
	//public static void Register(IDiagnosticViewProvider provider)
	//{
	//	ImmutableInterlocked.Update(
	//		ref _globalProviders,
	//		static (providers, provider) => providers.Add(provider),
	//		new GlobalProviderRegistration(GlobalProviderMode.One, provider));

	//	foreach (var overlay in _overlays)
	//	{
	//		overlay.Value.EnqueueUpdate();
	//	}
	//}

	private readonly XamlRoot _root;
	private readonly object _updateGate = new();
	private readonly List<IDiagnosticViewProvider> _localProviders = new();
	private readonly Dictionary<IDiagnosticViewProvider, DiagnosticElement> _elements = new();

	private DispatcherQueue? _dispatcher;
	private Context? _updateCoordinator;
	private Popup? _overlayHost;
	private StackPanel? _overlayPanel;
	private bool _isVisible;
	private int _updateEnqueued;

	static DiagnosticsOverlay()
	{
		DiagnosticViewRegistry.Added += static (snd, e) =>
		{
			foreach (var overlay in _overlays)
			{
				overlay.Value.EnqueueUpdate();
			}
		};
	}

	private DiagnosticsOverlay(XamlRoot root)
	{
		_root = root;
		_dispatcher = root.Content?.DispatcherQueue;
		_updateCoordinator = _dispatcher is null ? null : new Context(_dispatcher);

		root.Changed += static (snd, e) =>
		{
			var overlay = Get(snd);
			var dispatcher = snd.Content?.DispatcherQueue;
			if (dispatcher != overlay._dispatcher) // Is this even possible ???
			{
				lock (overlay._updateGate)
				{
					overlay._dispatcher = dispatcher;
					overlay._updateCoordinator = dispatcher is null ? null : new Context(dispatcher);

					// Clean all dispatcher bound state
					overlay._overlayHost = null;
					overlay._overlayPanel = null;
					foreach (var element in overlay._elements.Values)
					{
						element.Dispose();
					}
					overlay._elements.Clear();
				}
			}
			overlay.EnqueueUpdate();
		};
	}

	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			_isVisible = value;
			EnqueueUpdate(forceUpdate: !value); // For update when hiding.
		}
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="name">The friendly name of the </param>
	/// <param name="preview"></param>
	/// <param name="details"></param>
	public void Add(string name, UIElement preview, Func<UIElement>? details = null)
		=> Add(new GenericProvider(name, _ => preview, async (_, ct) => details?.Invoke()));

	/// <summary>
	/// Add a UI diagnostic element to this overlay.
	/// </summary>
	/// <param name="provider">The provider to add</param>
	public void Add(IDiagnosticViewProvider provider)
	{
		lock (_updateGate)
		{
			_localProviders.Add(provider);
		}

		EnqueueUpdate(); // Making IsVisible = true wil (try) to re-enqueue the update, but safer to keep it here anyway.
		IsVisible = true;
	}

	private void EnqueueUpdate(bool forceUpdate = false)
	{
		var dispatcher = _dispatcher;
		if ((!_isVisible && !forceUpdate) || dispatcher is null || Interlocked.CompareExchange(ref _updateEnqueued, 1, 0) is not 0)
		{
			return;
		}

		dispatcher.TryEnqueue(() =>
		{
			_updateEnqueued = 0;

			if (!_isVisible)
			{
				if (_overlayHost is { } host)
				{
					ShowHost(host, false);
				}

				return;
			}

			lock (_updateGate)
			{
				var providers = DiagnosticViewRegistry
					.Registrations
					.Where(ShouldMaterialize)
					.Select(reg => reg.Provider)
					.Concat(_localProviders)
					.Distinct()
					.ToList();

				var panel = _overlayPanel ??= CreatePanel();
				var host = _overlayHost ??= CreateHost(_root, panel);

				foreach (var provider in providers)
				{
					if (!_elements.ContainsKey(provider))
					{
						var element = new DiagnosticElement(this, provider, _updateCoordinator!);
						_elements[provider] = element;

						panel.Children.Add(element.Preview);
					}
				}

				ShowHost(host, true);
			}
		});
	}

	private static StackPanel CreatePanel()
	{
		var panel = new StackPanel
		{
			BorderThickness = new Thickness(1),
			BorderBrush = new SolidColorBrush(Colors.Black),
			Background = new SolidColorBrush(Colors.DarkGray),
			Orientation = Orientation.Horizontal,
			Padding = new Thickness(3),
			Spacing = 3,
			ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY
		};
		panel.ManipulationDelta += static (snd, e) =>
		{
			var panel = (Panel)snd;
			var transform = panel.RenderTransform as TranslateTransform;
			if (transform is null)
			{
				panel.RenderTransform = transform = new TranslateTransform();
			}

			transform.X += e.Delta.Translation.X;
			transform.Y += e.Delta.Translation.Y;
		};

		return panel;
	}

	private static Popup CreateHost(XamlRoot root, StackPanel panel)
		=> new()
		{
			XamlRoot = root,
			Child = panel,
			IsLightDismissEnabled = false,
			LightDismissOverlayMode = LightDismissOverlayMode.Off,
		};

	private static void ShowHost(Popup host, bool isVisible)
		=> host.IsOpen = isVisible;

	private bool ShouldMaterialize(DiagnosticViewRegistration registration) // Note: this as to be invoked in the _updateGate to avoid concurrent access on the _elements dictionary.
		=> registration.Mode switch
		{
			GlobalProviderMode.All => true,
			GlobalProviderMode.OnDemand => false,
			_ => _overlays.Count(overlay => overlay.Value._elements.ContainsKey(registration.Provider)) is 0
		};

	private record GenericProvider(string Name, Func<IDiagnosticViewContext, UIElement> Preview, Func<IDiagnosticViewContext, CancellationToken, ValueTask<object?>>? Details) : IDiagnosticViewProvider
	{
		/// <inheritdoc />
		public object GetPreview(IDiagnosticViewContext coordinator) => Preview(coordinator);

		/// <inheritdoc />
		public async ValueTask<object?> GetDetailsAsync(IDiagnosticViewContext context, CancellationToken ct) => Details is null ? null : await Details(context, ct);
	}

	//// A diagnostic provider that can be display on any window.
	//private record GlobalProviderRegistration(GlobalProviderMode Mode, IDiagnosticViewProvider Provider)
	//{
	//	public bool ShouldMaterialize() // Note: this as to be invoked in the _updateGate to avoid concurrent access on the _elements dictionary.
	//		=> Mode switch
	//		{
	//			GlobalProviderMode.All => true,
	//			GlobalProviderMode.OnDemand => false,
	//			_ => _overlays.Count(overlay => overlay.Value._elements.ContainsKey(Provider)) is 0
	//		};
	//}

	//private enum GlobalProviderMode
	//{
	//	/// <summary>
	//	/// Diagnostic is being rendered as overlay on each window.
	//	/// </summary>
	//	All,

	//	/// <summary>
	//	/// Diagnostic is being display on at least one window.
	//	/// I.e. only the main/first opened but move to the next one if the current window is closed.
	//	/// </summary>
	//	One,

	//	/// <summary>
	//	/// Only registers the diagnostic provider but does not display it.
	//	/// </summary>
	//	OnDemand
	//}
}

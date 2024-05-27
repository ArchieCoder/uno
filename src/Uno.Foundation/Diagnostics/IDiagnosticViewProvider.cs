#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.Diagnostics.UI;

/// <summary>
/// A diagnostic entry that can be displayed on the <see cref="DiagnosticsOverlay"/>.
/// </summary>
public interface IDiagnosticViewProvider
{
	/// <summary>
	/// Name of the diagnostic.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Get a preview of the diagnostic, usually a value or an icon.
	/// </summary>
	/// <param name="context">An update coordinator that can be used to push updates on the preview.</param>
	/// <returns>Either a UIElement to be displayed by the diagnostic overlay or a plain object (rendered as text).</returns>
	/// <remarks>This is expected to be invoked on the dispatcher used to render the preview.</remarks>
	object GetPreview(IDiagnosticViewContext context);

	/// <summary>
	/// Show details of the diagnostic.
	/// </summary>
	/// <param name="context">An update coordinator that can be used to push updates on the preview.</param>
	/// <param name="ct">Token to cancel the async operation.</param>
	/// <returns>Either a UIElement to be displayed by the diagnostic overlay, a ContentDialog to show, or a simple object to show in a content dialog.</returns>
	/// <remarks>This is expected to be invoked on the dispatcher used to render the preview.</remarks>
	ValueTask<object?> GetDetailsAsync(IDiagnosticViewContext context, CancellationToken ct);
}


internal static class DiagnosticViewRegistry
{
	internal static EventHandler<ImmutableList<DiagnosticViewRegistration>>? Added;

	private static ImmutableList<DiagnosticViewRegistration> _registrations = ImmutableList<DiagnosticViewRegistration>.Empty;

	internal static ImmutableList<DiagnosticViewRegistration> Registrations => _registrations;

	/// <summary>
	/// Register a global diagnostic provider that can be displayed on any window.
	/// </summary>
	/// <param name="provider">A diagnostic provider to display.</param>
	public static void Register(IDiagnosticViewProvider provider)
	{
		ImmutableInterlocked.Update(
			ref _registrations,
			static (providers, provider) => providers.Add(provider),
			new DiagnosticViewRegistration(GlobalProviderMode.One, provider));

		Added?.Invoke(null, _registrations);
	}
}

internal record DiagnosticViewRegistration(GlobalProviderMode Mode, IDiagnosticViewProvider Provider);

internal enum GlobalProviderMode
{
	/// <summary>
	/// Diagnostic is being rendered as overlay on each window.
	/// </summary>
	All,

	/// <summary>
	/// Diagnostic is being display on at least one window.
	/// I.e. only the main/first opened but move to the next one if the current window is closed.
	/// </summary>
	One,

	/// <summary>
	/// Only registers the diagnostic provider but does not display it.
	/// </summary>
	OnDemand
}

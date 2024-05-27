#nullable enable

using System;
using System.Linq;
using Microsoft.UI.Xaml;

namespace Uno.Diagnostics.UI;

internal class DiagnosticViewHelper<T>(Func<T> Factory, Action<T> Update)
	where T : FrameworkElement
{
	private event EventHandler? _changed;

	public void NotifyChanged()
	{
		_changed?.Invoke(this, EventArgs.Empty);
	}

	public UIElement GetView(IDiagnosticViewContext coordinator)
	{
		var view = Factory();
		EventHandler requestUpdate = (_, __) => coordinator.Schedule(() => Update(view));

		view.Loaded += (snd, e) =>
		{
			_changed += requestUpdate;
			requestUpdate(null, EventArgs.Empty);
		};
		view.Unloaded += (snd, e) => _changed -= requestUpdate;

		return view;
	}
}

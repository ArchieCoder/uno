using System;
using System.Linq;

namespace Uno.UI.RemoteControl.Messaging.IdeChannel;

/// <summary>
/// A message sent by the IDE to the dev-server regarding hot-reload operations.
/// </summary>
/// <param name="Event">The kind of hot-reload message.</param>
public record HotReloadEventIdeMessage(HotReloadEvent Event) : IdeMessage(WellKnownScopes.HotReload);

public enum HotReloadEvent
{
	/// <summary>
	/// Hot-reload completed (errors might come after!)
	/// </summary>
	Completed,

	/// <summary>
	/// Hot-reload completed with no changes
	/// </summary>
	NoChanges,

	/// <summary>
	/// Hot-reload failed (usually due to compilation errors)
	/// </summary>
	Failed,

	/// <summary>
	/// Hot-reload cannot be applied (rude edit), a dialog has been prompt to the user ... and he just gave a response!
	/// </summary>
	CannotApplyDialogButton
}

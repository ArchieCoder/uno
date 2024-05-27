#nullable enable

using System.Threading;

namespace Uno.UI.RemoteControl.Messaging.IdeChannel;

public record ForceHotReloadIdeMessage() : IdeMessage(WellKnownScopes.HotReload)
{
	private static long _count;
	public long Id { get; } = Interlocked.Increment(ref _count);
}

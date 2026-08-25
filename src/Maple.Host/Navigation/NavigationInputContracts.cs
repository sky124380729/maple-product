using Maple.Host.Stationary;

namespace Maple.Host.Navigation;

public enum NavigationInputAction { Attack, MoveLeft, MoveRight, MoveUp, MoveDown }

public interface INavigationActionSink
{
    Task<InputActionResult> KeyDownAsync(NavigationInputAction action, int leaseMs, CancellationToken token);
    Task<InputActionResult> KeyUpAsync(NavigationInputAction action, CancellationToken token);
    Task<InputActionResult> ReleaseAllAsync(CancellationToken token);
}

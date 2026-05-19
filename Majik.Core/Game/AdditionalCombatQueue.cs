namespace Majik.Core.Game;

/// <summary>
/// CR 506.4 — additional combat phases. Effects like Aggravated Assault
/// or Combat Celebrant push extra combats onto this queue during a turn.
/// CombatFlow / TurnDriver consult <see cref="HasAdditional"/> after the
/// current combat finishes and re-enter the combat sequence if true.
/// </summary>
public sealed class AdditionalCombatQueue
{
    private int _pending;

    public int Pending => _pending;
    public bool HasAdditional => _pending > 0;

    public void EnqueueAdditional() => _pending++;

    public bool TryConsume()
    {
        if (_pending == 0) return false;
        _pending--;
        return true;
    }

    public void Reset() => _pending = 0;
}

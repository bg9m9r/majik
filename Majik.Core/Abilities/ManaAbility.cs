using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Abilities;

/// <summary>
/// Represents a mana ability that generates mana.
/// Mana abilities don't use the stack (Rule 605).
/// </summary>
public class ManaAbility : IManaAbility
{
    private readonly Func<bool>? _canActivateCheck;
    private readonly Func<ManaCost> _manaGenerator;

    public object Source { get; }
    public Player Controller { get; }
    public ManaCost ManaGenerated { get; private set; }

    public ManaAbility(object source, Player controller, ManaCost manaGenerated, Func<bool>? canActivateCheck = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ManaGenerated = manaGenerated ?? throw new ArgumentNullException(nameof(manaGenerated));
        _canActivateCheck = canActivateCheck;
        _manaGenerator = () => manaGenerated;
    }

    public ManaAbility(object source, Player controller, Func<ManaCost> manaGenerator, Func<bool>? canActivateCheck = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _manaGenerator = manaGenerator ?? throw new ArgumentNullException(nameof(manaGenerator));
        _canActivateCheck = canActivateCheck;
        ManaGenerated = ManaCost.Zero; // Will be set when activated
    }

    public bool CanActivate()
    {
        if (_canActivateCheck != null)
        {
            return _canActivateCheck();
        }

        // Default: can activate if source is a permanent that can tap
        if (Source is Cards.Permanent permanent)
        {
            return !permanent.IsTapped;
        }

        return true;
    }

    public ManaCost Activate()
    {
        if (!CanActivate())
        {
            throw new InvalidOperationException("Cannot activate mana ability");
        }

        // Generate mana
        var mana = _manaGenerator();
        ManaGenerated = mana;

        // Tap the source if it's a permanent
        if (Source is Cards.Permanent permanent)
        {
            permanent.Tap();
        }

        return mana;
    }
}

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
    private readonly Action<Player>? _additionalCostPayer;

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

    /// <summary>
    /// Construct a mana ability whose activation also pays an additional
    /// non-mana cost beyond {T} — Horizon Canopy cycle "Pay 1 life",
    /// painlands' "deals N damage to you", etc. The
    /// <paramref name="additionalCostPayer"/> runs after tapping and before
    /// returning the generated mana; the <paramref name="canActivateCheck"/>
    /// gates legality (e.g. life total &gt; 1 for Pay 1 life — CR 119.4).
    ///
    /// CR 605.1 — the ability is still a mana ability (doesn't use the
    /// stack); the extra cost is part of the activation cost, not a
    /// resolution effect. The activator/bot treats it like any other mana
    /// ability — the side-effect happens transparently.
    /// </summary>
    public ManaAbility(
        object source,
        Player controller,
        ManaCost manaGenerated,
        Func<bool> canActivateCheck,
        Action<Player> additionalCostPayer)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ManaGenerated = manaGenerated ?? throw new ArgumentNullException(nameof(manaGenerated));
        _canActivateCheck = canActivateCheck ?? throw new ArgumentNullException(nameof(canActivateCheck));
        _additionalCostPayer = additionalCostPayer ?? throw new ArgumentNullException(nameof(additionalCostPayer));
        _manaGenerator = () => manaGenerated;
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

        // Pay any additional non-mana cost wired in via the
        // ctor (Horizon Canopy cycle "Pay 1 life", painlands' self-damage,
        // …). Runs after tapping so the failure mode (no-op for legal
        // activations) matches the rules-engine assumption that
        // CanActivate gated legality up front.
        _additionalCostPayer?.Invoke(Controller);

        return mana;
    }
}

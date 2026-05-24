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
    private readonly bool _tapsAsCost;

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
        _tapsAsCost = true;
    }

    public ManaAbility(object source, Player controller, Func<ManaCost> manaGenerator, Func<bool>? canActivateCheck = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _manaGenerator = manaGenerator ?? throw new ArgumentNullException(nameof(manaGenerator));
        _canActivateCheck = canActivateCheck;
        ManaGenerated = ManaCost.Zero; // Will be set when activated
        _tapsAsCost = true;
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
        _tapsAsCost = true;
    }

    /// <summary>
    /// Construct a mana ability whose activation cost does NOT include
    /// {T}. Wall of Roots' "Put a -0/-1 counter on this: Add {G}" is the
    /// canonical shape — the activation cost is the additional non-mana
    /// cost payer alone; the permanent stays untapped. Distinct from the
    /// standard "{T}, &lt;extra cost&gt;: Add …" overload which always taps
    /// the source.
    ///
    /// <para>Caller MUST supply both <paramref name="canActivateCheck"/>
    /// (the legality gate — typically a per-turn lock and/or a resource
    /// check) and <paramref name="additionalCostPayer"/> (the side-effect
    /// that actually pays the printed cost — e.g. place a -0/-1 counter on
    /// self).</para>
    ///
    /// CR 605.1 — the ability is still a mana ability (doesn't use the
    /// stack); the activation cost is paid up front and the generated
    /// mana is returned in the same atomic step.
    /// </summary>
    public ManaAbility(
        object source,
        Player controller,
        ManaCost manaGenerated,
        Func<bool> canActivateCheck,
        Action<Player> additionalCostPayer,
        bool tapsAsCost)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ManaGenerated = manaGenerated ?? throw new ArgumentNullException(nameof(manaGenerated));
        _canActivateCheck = canActivateCheck ?? throw new ArgumentNullException(nameof(canActivateCheck));
        _additionalCostPayer = additionalCostPayer ?? throw new ArgumentNullException(nameof(additionalCostPayer));
        _manaGenerator = () => manaGenerated;
        _tapsAsCost = tapsAsCost;
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

        // Tap the source if it's a permanent AND the printed cost
        // includes {T} (default). Wall of Roots' "Put a -0/-1 counter on
        // this: Add {G}" ability does NOT tap — the no-tap overload sets
        // _tapsAsCost = false so the permanent stays untapped through
        // multiple cost-counter activations across consecutive turns.
        if (_tapsAsCost && Source is Cards.Permanent permanent)
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

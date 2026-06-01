using Majik.Core.Mana;
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

    /// <summary>
    /// Optional spend-restriction stamped on every unit of mana this
    /// ability generates (Cavern of Souls' chosen-type rider, Eldrazi
    /// Temple's "spend only on Eldrazi spells/abilities", Mishra's
    /// Workshop's artifact-only rider, …). <c>null</c> ⇒ vanilla mana.
    /// CR 106.4 — the restriction is part of the mana's provenance, not
    /// the ability itself, and applies at spend time.
    ///
    /// <para>v1 of the spend-restriction primitive ships the data only:
    /// the rider lives here so factories can stamp it, and the payment
    /// resolver will consult it once
    /// <see cref="Majik.Core.ValueObjects.ManaPool"/> grows per-slot
    /// provenance (today's pool stores bucketed colour counts, no tags).
    /// Until then, restriction is observational metadata.</para>
    /// </summary>
    public SpendRestriction? SpendRestriction { get; }

    /// <summary>
    /// Optional slot-level provenance reaction (CR 106.4 — deferral #1). When
    /// non-null, the <see cref="Majik.Core.Services.ManaAbilityActivator"/>
    /// stamps every colored unit this ability produces with a
    /// <see cref="Majik.Core.Mana.ManaProvenanceSlot"/> whose source is THIS
    /// ability and whose <c>OnSpent</c> is this delegate — so the
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> can fire the
    /// reaction precisely when one of those units pays a cost, carrying the
    /// object the mana was spent on (the cast card, or null). Arena of Glory's
    /// exert ability sets this to "grant haste to a creature spell"
    /// (CR 702.10). <c>null</c> ⇒ vanilla mana, no slot tagging.
    /// </summary>
    public Action<Cards.ICard?>? ProvenanceReaction { get; set; }

    public ManaAbility(object source, Player controller, ManaCost manaGenerated, Func<bool>? canActivateCheck = null)
        : this(source, controller, manaGenerated, canActivateCheck, spendRestriction: null)
    {
    }

    /// <summary>
    /// Construct a mana ability whose generated mana carries a
    /// spend-restriction (Cavern of Souls, Eldrazi Temple, future
    /// Mishra's Workshop). <see cref="SpendRestriction"/> is stamped on
    /// every unit produced; the payment-gate enforcement is deferred
    /// until <see cref="Majik.Core.ValueObjects.ManaPool"/> grows
    /// per-slot tags (see property xmldoc).
    /// </summary>
    public ManaAbility(
        object source,
        Player controller,
        ManaCost manaGenerated,
        Func<bool>? canActivateCheck,
        SpendRestriction? spendRestriction)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ManaGenerated = manaGenerated ?? throw new ArgumentNullException(nameof(manaGenerated));
        _canActivateCheck = canActivateCheck;
        _manaGenerator = () => manaGenerated;
        _tapsAsCost = true;
        SpendRestriction = spendRestriction;
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
    /// Construct a DYNAMIC-mana ability ("{N},{T}: Add … for each …") whose
    /// activation also pays an additional cost via
    /// <paramref name="additionalCostPayer"/> — Cabal Coffers'
    /// "{2},{T}: Add {B} for each Swamp you control" (deferral #2). Composes
    /// the additional-cost payer with the <paramref name="manaGenerator"/>
    /// <c>Func&lt;ManaCost&gt;</c> so the {N} mana payment is declared cleanly
    /// instead of being inlined inside the generator lambda.
    ///
    /// <para>Order in <see cref="Activate"/>: evaluate the generator
    /// (counts the dynamic quantity, e.g. Swamps), tap the source ({T}), then
    /// run <paramref name="additionalCostPayer"/> (pay the {N}). The {N}
    /// payment and the mana production are part of the same atomic activation
    /// cost (CR 602.2a / 605.1) — the observable post-activation state is the
    /// same regardless of intra-step ordering. <paramref name="canActivateCheck"/>
    /// gates legality (untapped AND can afford {N}) so the generator is only
    /// ever reached when the full cost is payable (CR 119.4).</para>
    /// </summary>
    public ManaAbility(
        object source,
        Player controller,
        Func<ManaCost> manaGenerator,
        Func<bool> canActivateCheck,
        Action<Player> additionalCostPayer)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _manaGenerator = manaGenerator ?? throw new ArgumentNullException(nameof(manaGenerator));
        _canActivateCheck = canActivateCheck ?? throw new ArgumentNullException(nameof(canActivateCheck));
        _additionalCostPayer = additionalCostPayer ?? throw new ArgumentNullException(nameof(additionalCostPayer));
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
        // CR 302.6 / 605.3a — central summoning-sickness gate. When the
        // activation cost includes {T} (the standard mana-ability shape;
        // _tapsAsCost == true) and the source is a summoning-sick creature
        // without haste, the ability can't be activated. Mana abilities are
        // NOT exempt (CR 605.3a). Checked BEFORE the per-card
        // canActivateCheck (which only tests !IsTapped) so custom gates can't
        // bypass the rule. No-tap mana abilities (Wall of Roots,
        // _tapsAsCost == false) and lands are unaffected.
        if (_tapsAsCost && !SummoningSicknessTapGate.CanTapForAbility(Source))
        {
            return false;
        }

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

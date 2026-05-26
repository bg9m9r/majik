using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Burrenton Forge-Tender (Lorwyn, {W}).
///
/// Creature — Kithkin Wizard 1/1. Oracle text:
///   "Protection from red.
///    Sacrifice Burrenton Forge-Tender: Prevent all damage that would be
///    dealt to target creature this turn by red sources."
///
/// ## Implemented (v1)
///
/// - 1/1 Kithkin Wizard at {W}, owner / controller wired.
/// - <b>Protection from red</b> as a <see cref="ProtectionAbility"/>
///   ("red"). CR 702.16 — DEBT-A (damage / enchant + equip / block /
///   target restrictions). Same wiring shape as Goblin Piledriver's
///   protection-from-blue.
/// - <b>Activated sacrifice ability</b> (CR 602.1) with one cost +
///   one effect:
///     * Cost: <see cref="SacrificeCreatureCost"/> targeting Burrenton
///       itself — sacrifice-self goes through Battlefield → Graveyard
///       on payment. <see cref="SacrificeCreatureCost.CanPay"/> reads
///       Controller + Zone == Battlefield + HasType(Creature), which
///       Burrenton trivially satisfies on its own activation.
///     * Effect: register a
///       <see cref="PreventAllDamageFromColoredSourcesToCreatureShield"/>
///       (colour = <see cref="ManaColor.Red"/>) on the chosen creature
///       target's controller's <see cref="Player.Replacements"/> bus
///       so any incoming <see cref="DamageIntent"/> whose source is a
///       red card AND target is the chosen creature is cancelled (CR
///       615.1). Shield auto-drops at cleanup via
///       <see cref="IEndOfTurnExpirable"/>.
///
/// ## Target choice
/// The damage-prevention target is set on the ability via
/// <see cref="BurrentonForgeTenderAbility.PreventionTarget"/> before
/// activation — same pattern as <see cref="GoblinBombardmentFactory"/>'s
/// pre-set damage target. Mid-activation agent prompt for the target is
/// deferred (same posture as Goblin Bombardment / Walking Ballista).
///
/// ## Replacement-bus routing
/// The shield must be registered on a <see cref="ReplacementBus"/> the
/// engine consults during damage assignment. The factory accepts an
/// optional <c>replacementBus</c> at construction; production callers
/// thread the live game-shared bus, tests can supply a hand-rolled one
/// or read the resolution via <see cref="BurrentonForgeTenderResolution"/>.
/// When null the activated-ability effect still runs the
/// CanPay / Pay flow but skips bus registration (the prevention shield
/// has no live wiring to attach to — the resolution still records
/// what would have been registered, so shape tests can assert it).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven target prompt</b>: same gap as
///   <see cref="GoblinBombardmentFactory"/>. The activated-ability flow
///   does not yet route through a target-request prompt the way
///   <see cref="Game.SpellCastFlow"/> does.
/// - <b>Per-target controller's bus</b>: v1 registers the shield on
///   Burrenton's controller's <see cref="Player.Replacements"/> bus. A
///   future refinement could route to the target's controller's bus
///   instead — but the engine's
///   <see cref="ReplacementBus"/> isn't currently per-player-scoped in
///   the damage-resolution pipeline; the shared bus model already
///   matches every existing prevention shield (Endure, Safe Passage,
///   Deflecting Palm). When per-player scoping ships the wiring point
///   is the one line marked below.
/// </summary>
[CardName("Burrenton Forge-Tender")]
public static class BurrentonForgeTenderFactory
{
    public const string CardName = "Burrenton Forge-Tender";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Burrenton Forge-Tender with no live replacement bus.
    /// Protection from red is wired; the activated ability is attached
    /// but its effect body will skip the bus-register step (the
    /// sac-self cost still pays). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacementBus: null);

    /// <summary>
    /// Construct Burrenton Forge-Tender with an optional live
    /// <see cref="ReplacementBus"/>. When supplied, the activated
    /// ability registers the prevention shield on the bus on resolve.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacementBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Kithkin, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.16 — Protection from red. Quality stored as "red"; the
        // Rules.Protection / TargetLegality / CombatAbilities helpers
        // interpret it (DEBT-A). Same shape as Goblin Piledriver.
        card.AddAbility(new ProtectionAbility("red"));

        // CR 602.1 — activated ability: sac-self → prevent red damage.
        // Cost: "Sacrifice Burrenton Forge-Tender" — SacrificeSelfCost
        // routes Battlefield → Graveyard on the source itself (CR 701.16a).
        var sacrificeCost = new SacrificeSelfCost(card);
        var ability = new BurrentonForgeTenderAbility(
            source: card,
            controller: owner,
            sacrificeCost: sacrificeCost,
            replacementBus: replacementBus);

        card.AddAbility(ability);
        return card;
    }
}

/// <summary>
/// Burrenton Forge-Tender's only activated ability — sac-self prevent
/// red. Subclasses <see cref="ActivatedAbility"/> so the chosen
/// prevention target travels with the ability instance (test / bot
/// setter), and the sacrifice cost is reachable for tests.
/// </summary>
public sealed class BurrentonForgeTenderAbility : ActivatedAbility
{
    /// <summary>The sacrifice cost. Exposed so tests can inspect the
    /// sacrificed creature post-payment.</summary>
    public SacrificeSelfCost SacrificeChoice { get; }

    private readonly BurrentonForgeTenderState _state;

    /// <summary>
    /// The chosen creature for damage prevention. Set this between
    /// activations; <c>null</c> means the effect is a no-op (mirrors
    /// Goblin Bombardment's <c>DamageTarget</c>).
    /// </summary>
    public Creature? PreventionTarget
    {
        get => _state.Target;
        set => _state.Target = value;
    }

    /// <summary>
    /// Most recent resolution payload — null until the ability resolves
    /// at least once. Used by tests / bots to read which shield was
    /// registered (or that none was, when the target was null).
    /// </summary>
    public BurrentonForgeTenderResolution? LastResolution => _state.LastResolution;

    internal BurrentonForgeTenderAbility(
        Creature source,
        Player controller,
        SacrificeSelfCost sacrificeCost,
        ReplacementBus? replacementBus)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { sacrificeCost },
            effects: BuildEffects(replacementBus, out var stateRef))
    {
        SacrificeChoice = sacrificeCost;
        _state = stateRef;
    }

    private static IEffect[] BuildEffects(
        ReplacementBus? replacementBus,
        out BurrentonForgeTenderState stateOut)
    {
        var state = new BurrentonForgeTenderState();
        stateOut = state;
        return new IEffect[]
        {
            new Effect(
                "Burrenton Forge-Tender: prevent red damage to target creature EOT (CR 615)",
                () =>
                {
                    var target = state.Target;
                    if (target == null)
                    {
                        state.LastResolution = new BurrentonForgeTenderResolution(
                            Target: null, Shield: null, Registered: false);
                        return;
                    }

                    // CR 615 — build the prevention shield. Even when the
                    // replacement bus is null (shape tests), construct the
                    // shield so the resolution payload reflects what would
                    // have been registered.
                    var shield = new PreventAllDamageFromColoredSourcesToCreatureShield(
                        target, ManaColor.Red);

                    if (replacementBus != null)
                    {
                        replacementBus.Register(shield);
                        state.LastResolution = new BurrentonForgeTenderResolution(
                            Target: target, Shield: shield, Registered: true);
                    }
                    else
                    {
                        state.LastResolution = new BurrentonForgeTenderResolution(
                            Target: target, Shield: shield, Registered: false);
                    }
                }),
        };
    }
}

/// <summary>
/// Resolution payload exposed via
/// <see cref="BurrentonForgeTenderAbility.LastResolution"/>. <c>Target</c>
/// is null when the ability resolved without a chosen prevention target
/// (clean no-op). <c>Registered</c> is true iff the shield was actually
/// attached to a live <see cref="ReplacementBus"/>; false on the shape /
/// no-bus path.
/// </summary>
public sealed record BurrentonForgeTenderResolution(
    Creature? Target,
    PreventAllDamageFromColoredSourcesToCreatureShield? Shield,
    bool Registered);

/// <summary>
/// Mutable box shared between the activated ability and its effect
/// closure so the chosen target + last resolution travel with the
/// instance (mirrors Goblin Bombardment's <c>PingEffectState</c>).
/// </summary>
internal sealed class BurrentonForgeTenderState
{
    public Creature? Target { get; set; }
    public BurrentonForgeTenderResolution? LastResolution { get; set; }
}

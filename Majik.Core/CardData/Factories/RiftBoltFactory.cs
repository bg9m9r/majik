using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rift Bolt (Time Spiral, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Rift Bolt deals 3 damage to any target.
///    Suspend 1—{R}."
///
/// ## Implemented (v1)
/// - Sorcery card with mana cost {2}{R}.
/// - Normal cast path: damage 3 to any target via the standard
///   <see cref="OracleSpellBinder.DealDamage"/> shape (Creature /
///   Planeswalker / Player). Caller drives the cast through
///   <see cref="Majik.Core.Game.SpellCastFlow"/> with
///   <see cref="BuildSpellDefinition"/>.
/// - Suspend alt cost: pay {R}, exile Rift Bolt with 1 time counter via
///   <see cref="SuspendAlternativeCost"/>. On the controller's next
///   upkeep <see cref="SuspendedCardRegistry"/> removes the last counter
///   and the card is cast without paying its mana cost.
///
/// ## Deferred (v1 gaps)
/// - <b>Creature-suspend haste</b> (CR 702.62g): Rift Bolt is a sorcery,
///   so the "gains haste until removed from combat" rider doesn't apply.
///   When a Creature suspend lands, the registry's ready-callback should
///   tag the resolving permanent with haste until it leaves combat.
/// - <b>Oracle binder discovery</b>: a binder pass for "Suspend N—[cost]"
///   isn't wired into <see cref="OracleSpellBinder"/> yet — bots see
///   suspend via the dedicated <see cref="SuspendOracleParser"/>
///   helper or by direct factory construction. Auto-discovery from
///   Scryfall oracle text is a follow-up.
/// </summary>
[CardName("Rift Bolt")]
public static class RiftBoltFactory
{
    public const string CardName = "Rift Bolt";
    public const string PrintedManaCost = "{2}{R}";
    public const string SuspendCostText = "{R}";
    public const int SuspendTimeCounters = 1;

    /// <summary>
    /// Build a Rift Bolt sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the spell definition (target + damage effect) is built
    /// on-demand by <see cref="BuildSpellDefinition"/> since
    /// <see cref="SpellDefinition"/> needs a target resolver supplied by
    /// the caller's <see cref="GameContext"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Rift Bolt uses when cast —
    /// whether paid normally for {2}{R} or via the post-suspend free cast.
    /// Single 1..1 "any target" request, deals 3 damage on resolution.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Rift Bolt: deal 3 damage", () =>
                        Fx.DealDamage(target, 3)),
                };
            });

    /// <summary>The suspend alt cost printed on Rift Bolt — Suspend 1—{R}.
    /// CR 702.62.</summary>
    public static SuspendAlternativeCost BuildSuspendCost() =>
        new(SuspendTimeCounters, ManaCost.Parse(SuspendCostText));
}

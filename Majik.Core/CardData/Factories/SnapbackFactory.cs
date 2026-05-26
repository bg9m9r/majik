using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snapback (Time Spiral, {2}{U}).
///
/// Instant. Oracle text:
///   "You may exile a blue card from your hand rather than pay this spell's
///    mana cost.
///    Return target creature to its owner's hand."
///
/// ## Implemented (v1)
/// - Instant card shape ({2}{U}, Blue) — built via the fluent
///   <see cref="CardDef"/> DSL, matching the Force-of-Negation /
///   Force-of-Will / Force-of-Vigor / Force-of-Despair shape.
/// - Pitch alternative cost via
///   <see cref="Majik.Core.Costs.ExileColoredCardAlternativeCost"/>
///   (<c>RequiredColor = Blue</c>) — the no-timing-gate / no-life-rider
///   pitch primitive Soul Spike already uses. Snapback's printed pitch
///   carries NO "if it's not your turn" restriction (unlike the
///   Force-of-Will cycle); this is the correct primitive (vs.
///   <see cref="Majik.Core.Costs.PitchAlternativeCost"/> which enforces
///   the Force-cycle not-your-turn gate).
/// - Resolve effect (<see cref="BuildDefinition"/>): "return target creature
///   to its owner's hand" — single-target bounce via
///   <see cref="Fx.BounceToHand"/>, same shape every Unsummon-family
///   template binds to.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot probe</b>: <see cref="PitchAltCostProbe.DefaultLookup"/> is
///   keyed by <see cref="Majik.Core.Costs.PitchAlternativeCost"/>'s
///   not-your-turn shape, so Snapback isn't surfaced through the existing
///   PitchAltCostProbe — same posture as Soul Spike (which uses
///   <see cref="Majik.Core.Costs.ExileTwoColoredCardsAlternativeCost"/>
///   and likewise isn't in the Force-cycle probe). A
///   "TimingFreeExileColoredCardProbe" mirror is deferred until the bot
///   shows it cares about Snapback / Foil / Pyrokinesis at the EV level —
///   the printed mana cast still works.
/// </summary>
[CardName("Snapback")]
public static class SnapbackFactory
{
    public const string CardName = "Snapback";
    public const string PrintedManaCost = "{2}{U}";

    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "return target creature to its owner's hand" SpellDefinition.
    /// Mirrors <c>ControlSpellFactory.BounceTargetSpell</c> — inlined here
    /// so the named-card factory is fully self-contained (same posture as
    /// Force of Negation / Force of Will).
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        ZoneService? zoneService = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Snapback — return target creature to its owner's hand", () =>
                    {
                        // CR 701.10 — return: source zone → owner's hand.
                        // CR 608.2b — illegal target (target moved off the
                        // battlefield since cast) → no-op.
                        if (resolved is not ICard card) return;
                        if (card.Owner == null) return;
                        if (card.Zone != ZoneType.Battlefield) return;
                        Fx.BounceToHand(card, zoneService);
                    }),
                };
            });
}

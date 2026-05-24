using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Charge (Odyssey / Modern Horizons, {R}).
///
/// Sorcery. Oracle text:
///   "Target creature gets +3/+0 and gains haste until end of turn.
///    Flashback {2}{R}."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target creature"
///   request. On resolution the targeted creature gains +3/+0 and the Haste
///   keyword until end of turn, registered as a
///   <see cref="PumpUntilEndOfTurnEffect"/> + <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///   pair on the target's <see cref="Creature.ActiveEffects"/>
///   (CR 514.2 — both expire at the cleanup step).
/// - Flashback alt-cost {2}{R} is exposed via <see cref="BuildFlashbackCost"/>
///   (parsed by <see cref="FlashbackOracleParser"/> from the printed oracle
///   text so the data-driven binder path and this named-factory path agree
///   on cost shape). Callers wire the returned <see cref="FlashbackAlternativeCost"/>
///   into <see cref="SpellCastFlow"/> when casting from graveyard; the
///   post-resolve exile (CR 702.34b) runs through the cost's
///   <c>OnResolved</c> hook.
///
/// ## Deferred (v1 gaps)
/// - <b>Illegal-target fizzle</b>: handled by <see cref="SpellCastFlow"/>
///   at resolution-time target legality (CR 608.2b); the resolve closure
///   additionally guards against a non-Creature resolver result and a
///   missing <see cref="Creature.ActiveEffects"/> service (test/shape mode)
///   so the effect is a clean no-op rather than a NRE.
/// - <b>Anaphoric "gains haste"</b>: the keyword grant binds to the same
///   target as the pump — there is no separate target request for the haste
///   half (CR 700.2). This matches the printed text "Target creature gets
///   +3/+0 and gains haste".
/// </summary>
[CardName("Reckless Charge")]
public static class RecklessChargeFactory
{
    public const string CardName = "Reckless Charge";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Oracle text used by <see cref="BuildFlashbackCost"/> to derive the
    /// flashback cost via <see cref="FlashbackOracleParser"/>. Kept on the
    /// factory so the production load path (Scryfall row → oracle text →
    /// parser) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Target creature gets +3/+0 and gains haste until end of turn.\nFlashback {2}{R}";

    /// <summary>+P pump magnitude. Reckless Charge prints +3/+0.</summary>
    public const int PumpPower = 3;

    /// <summary>+T pump magnitude. Reckless Charge prints +3/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Build a Reckless Charge sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + pump/haste
    /// effect is built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Reckless Charge is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature gains +3/+0 and Haste until end of turn.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Mirrors
    /// the shape used by Path to Exile / Swords to Plowshares / Searing
    /// Blaze.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Reckless Charge: +3/+0 and gains haste until end of turn", () =>
                    {
                        // CR 608.2b — illegal-target check. The cast-flow's
                        // own legality pass already drops illegal targets,
                        // but the closure runs after that pass against the
                        // resolver's snapshot, so guard defensively: if the
                        // chosen target is no longer a Creature (zone-change,
                        // type-loss, etc.) or has no live continuous-effects
                        // service wired, the spell does nothing.
                        if (raw is not Creature target) return;
                        if (target.ActiveEffects == null) return;

                        // CR 613.1c — Layer 7c +P/+T modification.
                        target.ActiveEffects.Register(
                            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

                        // CR 613.1c (Layer 6) — keyword grant. Haste lifts
                        // summoning sickness for the rest of the turn
                        // (CR 702.10b — checked at attack-declaration).
                        target.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(target, GrantedKeyword));
                    }),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost ({2}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here)
    /// keeps the named-factory path and the data-driven oracle binder path
    /// agreeing on shape — any change to the parser's interpretation of
    /// "Flashback {2}{R}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Reckless Charge's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}

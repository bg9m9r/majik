using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Static Discharge (Mystery Booster 2 — Playtest /
/// Wizards Play Network, {1}{R}).
///
/// Sorcery. Scryfall oracle text (verbatim):
///   "Starting intensity 3
///    This sorcery deals damage equal to its intensity to any target. Then
///    cards you own named Static Discharge intensify by 1."
///
/// ## Intensity / Intensify (the unblocked mechanic)
/// Intensity is a CARD-scoped numeric value tracked on
/// <see cref="Card.Intensity"/> (NOT a permanent counter — sorceries never
/// become permanents). It persists across every zone, so a Static Discharge
/// in the graveyard keeps the higher value it accumulated and deals that much
/// the next time it's cast. The keyword wiring lives in
/// <see cref="IntensifyHelper"/>:
/// <list type="bullet">
///   <item><b>Starting intensity 3</b> — stamped at card-build time via
///   <see cref="IntensifyHelper.Build"/> (and an "Intensity 3" keyword
///   marker for inspectors).</item>
///   <item><b>"deals damage equal to its intensity"</b> — the resolve body
///   reads the live value with <see cref="IntensifyHelper.IntensityOf"/>
///   (every owned copy stays in lock-step, so reading any owned copy — incl.
///   the one resolving on the stack — yields the correct amount) and deals
///   it to the chosen target through <see cref="Fx.DealDamageAny"/> (CR 115.3
///   — any target = creature, player, planeswalker, or battle).</item>
///   <item><b>"Then cards you own named Static Discharge intensify by 1"</b>
///   — <see cref="IntensifyHelper.IntensifyOwnedCopies"/> raises every copy
///   the caster owns (any zone, including the resolving spell) by 1.</item>
/// </list>
///
/// ## Production wiring
/// The live cast path resolves spell behaviour through the binder chain
/// (<see cref="OracleSpellBinder"/>), NOT a named factory's
/// <see cref="BuildSpellDefinition"/> (which is test-only convenience — see
/// the named-factory-vs-binder-chain memory note). The production behaviour
/// for Static Discharge is therefore supplied by
/// <see cref="SpellTemplates.Templates.Bespoke.StaticDischargeTemplate"/>,
/// which the binder selects on the "deals damage equal to its intensity"
/// oracle pattern and builds the same read-intensity / intensify-owned body.
/// The starting-intensity stamp is applied to the live card via the same
/// helper from <see cref="ScryfallCardFactory"/>'s post-bind keyword pass
/// (Intensity keyword on the seed row). This factory's <see cref="Define"/>
/// also stamps it so the named-factory test path and the deck-build path
/// agree.
/// </summary>
[CardName("Static Discharge")]
public static class StaticDischargeFactory
{
    public const string CardName = "Static Discharge";
    public const string PrintedManaCost = "{1}{R}";
    public const int StartingIntensity = 3;

    public const string OracleText =
        "Starting intensity 3\n" +
        "This sorcery deals damage equal to its intensity to any target. " +
        "Then cards you own named Static Discharge intensify by 1.";

    /// <summary>CardDef DSL — Sorcery shape, {1}{R}. The damage/intensify
    /// body is supplied at cast time via <see cref="BuildSpellDefinition"/>
    /// (it needs the live caster + resolver from the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner)
    {
        var card = (Sorcery)CardDefRuntime.Build(Define(), owner);
        // CR — "Starting intensity 3". Stamp the printed value + keyword
        // marker so the card reports intensity 3 the moment it is built.
        IntensifyHelper.Build(card, StartingIntensity);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Static Discharge is
    /// cast. Single 1..1 "any target" request; on resolution deals damage
    /// equal to the caster's current Static Discharge intensity, then
    /// intensifies every Static Discharge the caster owns by 1.
    /// </summary>
    /// <param name="caster">The spell's caster/owner — its owned Static
    /// Discharge copies are read for the damage amount and intensified after.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        "Static Discharge: deal intensity damage, then intensify by 1",
                        () =>
                        {
                            var intensity = IntensifyHelper.IntensityOf(caster, CardName);
                            if (intensity > 0) Fx.DealDamageAny(target, intensity);
                            // CR 608.2c — "Then …" the intensify happens after
                            // the damage, in printed order.
                            IntensifyHelper.IntensifyOwnedCopies(caster, CardName, 1);
                        }),
                };
            });
    }
}

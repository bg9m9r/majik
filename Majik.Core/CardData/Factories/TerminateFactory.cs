using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terminate (Planeshift / various reprints, {B}{R}).
///
/// Instant. Oracle text:
///   "Destroy target creature. It can't be regenerated."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}{R}, owner / controller.
/// - <b>Destroy target creature</b> — <see cref="BuildSpellDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   targeted creature is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it
///   is still on the battlefield and is a creature (CR 608.2b).
///
/// ## Deferred (v1 gaps)
/// - <b>"It can't be regenerated"</b>: the engine has no regeneration
///   shield surface in v1 (same gap as Wrath of God's and
///   DayOfJudgment's can't-be-regenerated rider, and SlaughterPact's
///   indestructible/regeneration note). The printed rider is noted here
///   for completeness — it would be implemented as a one-shot
///   RegenerationShield suppression flag on the targeted permanent at
///   resolution time.
/// - <b>Indestructible</b>: the destroy call moves the creature to the
///   graveyard without checking for Indestructible — same gap as every
///   other single-target destroy template.
/// </summary>
public static class TerminateFactory
{
    public const string CardName = "Terminate";
    public const string PrintedManaCost = "{B}{R}";

    /// <summary>
    /// Construct the Terminate card shape (Instant, {B}{R}).
    /// Resolve behaviour is built on demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Terminate is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature is destroyed (CR 701.7) iff it is still on the
    /// battlefield and is a creature (CR 608.2b — illegal target → no-op).
    ///
    /// The "it can't be regenerated" rider is deferred — the engine has
    /// no regeneration shield surface in v1 (see class xmldoc).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a
    /// live engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature on the battlefield.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. "It can't be regenerated"
                            // rider is deferred — no regeneration shield surface
                            // in the engine yet (same gap as Wrath of God /
                            // Day of Judgment's can't-regenerate clause).
                            OracleSpellBinder.MoveToGraveyard(target);
                        }),
                };
            });
    }
}

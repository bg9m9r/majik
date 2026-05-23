using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murderous Cut (Khans of Tarkir, {4}{B}).
///
/// Instant. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Destroy target creature."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {4}{B}.
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> so downstream
///   code (UI, bot probes, action validator) can introspect the keyword.
///   The actual Delve mechanic (CR 702.66) lives in
///   <see cref="Majik.Core.Costs.DelveCost"/> + <see cref="SpellCastFlow"/>;
///   callers cast Murderous Cut via the cast-flow's <c>delveCost</c>
///   parameter when they want to substitute graveyard exiles for generic
///   mana — same wire-up as Treasure Cruise / Dig Through Time.
/// - On-resolve "Destroy target creature" effect (CR 701.7), exposed via
///   <see cref="BuildSpellDefinition"/>. Single 1..1 target-creature
///   request; "indestructible" + "can't be regenerated" riders deferred —
///   same lossy MVP as <c>DestroySpellFactory.DestroyCreatureSpell</c>.
///
/// ## Deferred (v1 gaps)
/// - Bot-side <c>IAlternativeCostProbe</c>-style discovery for Delve is
///   not yet wired; the heuristic bot won't proactively delve. Parallels
///   the Treasure Cruise / Dig Through Time v1 PRs which deferred bot
///   probe wiring.
/// </summary>
public static class MurderousCutFactory
{
    public const string CardName = "Murderous Cut";
    public const string PrintedManaCost = "{4}{B}";

    /// <summary>
    /// Build a Murderous Cut instant owned by <paramref name="owner"/>.
    /// Card shape + Delve keyword marker; the resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.66 — Delve marker. The behavior itself is in DelveCost +
        // SpellCastFlow; the marker is here so introspection (UI, bots)
        // can see the keyword on the card.
        card.AddAbility(new KeywordAbility("Delve", card, owner));

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request; on resolution the targeted creature is
    /// destroyed (CR 701.7) — moved from battlefield to its owner's
    /// graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
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
                    new Effect("Murderous Cut: destroy target creature", () =>
                    {
                        if (raw is not Creature target) return;
                        // CR 608.2b — illegal-target check at resolution.
                        if (target.Zone != ZoneType.Battlefield) return;
                        OracleSpellBinder.MoveToGraveyard(target);
                    }),
                };
            });
    }
}

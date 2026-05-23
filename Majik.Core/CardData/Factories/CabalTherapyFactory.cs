using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cabal Therapy (Judgment / Modern Horizons 2, {B}).
///
/// Sorcery. Oracle text:
///   "Name a nonland card. Target player reveals their hand and discards
///    all cards with that name.
///    Flashback—Sacrifice a creature."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request. On resolution the target player reveals their hand (one
///   <see cref="CardRevealedEvent"/> per card via
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.RevealHelper.RevealHand"/>)
///   and then every card matching the chosen name is moved from hand to
///   graveyard (CR 701.16 — discard; CR 701.16a — "discard all cards with
///   that name").
/// - "Name a nonland card" choice is sourced from a caller-supplied
///   <see cref="Func{Player, String}"/> <c>nameSelector</c> (mirrors the
///   <see cref="PithingNeedleFactory"/> pattern). The single-arg
///   dispatcher path leaves the selector empty — callers building the
///   spell definition directly pass it through. A null / empty name
///   matches nothing (defensive — no discards rather than discarding
///   nameless cards).
/// - Flashback alt-cost is exposed via <see cref="BuildFlashbackCost"/>
///   alongside <see cref="BuildFlashbackAdditionalCosts"/>. Cabal
///   Therapy's printed flashback cost is "Sacrifice a creature" — a
///   non-mana cost. The engine's <see cref="FlashbackAlternativeCost"/>
///   only carries the mana portion (CR 118.9), so v1 splits the cost:
///   the alt cost is <see cref="ManaCost.Zero"/> and the sacrifice rider
///   ships as a separate <see cref="SacrificeACreatureAdditionalCost"/>
///   that callers thread through <see cref="SpellCastFlow"/>'s
///   <c>additionalCosts</c> parameter when flashbacking. The post-resolve
///   exile (CR 702.34b) runs through the cost's <c>OnResolved</c> hook.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may name any card" prompt</b>: agent-side card-name picker
///   isn't surfaced yet — same queue as Pithing Needle, Cavern of Souls,
///   Plague Engineer. Callers pre-pick via <c>nameSelector</c>.
/// - <b>Nonland gate</b>: the printed text restricts the named card to
///   nonland. v1 doesn't enforce — the caller's <c>nameSelector</c> is
///   responsible for picking a legal name (matches the deferral pattern
///   on Pithing Needle).
/// - <b>Flashback-with-sacrifice as a single cost</b>: engine's
///   <see cref="IAlternativeCost"/> surface only carries the mana
///   portion, so the sacrifice rider rides as a paired additional cost.
///   Future work: extend <see cref="IAlternativeCost"/> to carry a
///   non-mana rider list so the cast flow charges them together.
/// </summary>
public static class CabalTherapyFactory
{
    public const string CardName = "Cabal Therapy";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Oracle text reference. Cabal Therapy's printed flashback cost is
    /// "Sacrifice a creature" — non-mana, so
    /// <see cref="FlashbackOracleParser"/> would parse the mana portion as
    /// <see cref="ManaCost.Zero"/>. Kept here for documentation; the
    /// flashback cost is built directly by <see cref="BuildFlashbackCost"/>
    /// rather than through the parser (the parser doesn't model the
    /// non-basic-land sacrifice rider yet).
    /// </summary>
    public const string OracleText =
        "Name a nonland card. Target player reveals their hand and discards all cards with that name.\nFlashback—Sacrifice a creature.";

    /// <summary>
    /// Build a Cabal Therapy sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + name-and-
    /// discard effect is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Cabal Therapy is
    /// cast. Single 1..1 "target player" request; on resolution the
    /// target player reveals their hand and discards every card matching
    /// the chosen name.
    /// </summary>
    /// <param name="caster">Cast-time controller — used to publish the
    /// reveal event with a stable reason string.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="nameSelector">Resolves the chosen card name at
    /// resolution time (CR 700.2 — choice made on cast for "name a card"
    /// effects; v1 lazily queries at resolve to keep the closure simple).
    /// Returning null / empty matches nothing.</param>
    /// <param name="eventBus">Optional event bus for publishing
    /// <see cref="CardRevealedEvent"/> per card in the revealed hand. No-op
    /// when null (test fixtures may bind without a bus).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        Func<Player, string?>? nameSelector,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Cabal Therapy: reveal + discard all with named name", () =>
                    {
                        // CR 608.2b — illegal-target check (target player
                        // left the game, etc.). The cast-flow's own pass
                        // catches most of these; guard defensively.
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target player reveals their hand."
                        // RevealHelper publishes one CardRevealedEvent per
                        // card so portal clients can flash the hand.
                        Majik.Core.CardData.SpellTemplates.Templates.Bespoke
                            .RevealHelper.RevealHand(eventBus, victim, "Cabal Therapy");

                        // CR 701.16a — discard all cards with the chosen
                        // name. Null / empty name matches nothing (defensive
                        // guard so a missing nameSelector doesn't sweep
                        // nameless tokens).
                        var chosenName = nameSelector?.Invoke(caster);
                        if (string.IsNullOrEmpty(chosenName)) return;

                        // Snapshot hand before mutation so the iteration is
                        // stable (mirrors RevealHelper's snapshot pattern).
                        var matches = victim.Zones.Hand.GetCards()
                            .Where(c => string.Equals(c.Name, chosenName, StringComparison.Ordinal))
                            .ToList();
                        foreach (var pick in matches)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost. Cabal Therapy's printed
    /// flashback cost is "Sacrifice a creature" — non-mana — so the
    /// returned cost carries <see cref="ManaCost.Zero"/>. The sacrifice
    /// rider ships separately via
    /// <see cref="BuildFlashbackAdditionalCosts"/>; callers compose both
    /// when wiring the flashback cast through <see cref="SpellCastFlow"/>.
    /// Post-resolve exile (CR 702.34b) is handled by the cost's
    /// <c>OnResolved</c> hook (same as Faithless Looting / Reckless
    /// Charge).
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost() =>
        new FlashbackAlternativeCost(ManaCost.Zero);

    /// <summary>
    /// Build the additional-cost rider that accompanies the flashback
    /// alt-cost — "Sacrifice a creature" as a non-mana cost (CR 601.2f /
    /// CR 702.34). Returned as a single-element list to match the shape
    /// <see cref="SpellCastFlow"/> threads through its
    /// <c>additionalCosts</c> parameter. v1 deterministically picks the
    /// first creature the caster controls (mirrors
    /// <see cref="SacrificeACreatureAdditionalCost"/>'s payment policy on
    /// Blood for Bones / Infernal Plunge / Fling).
    /// </summary>
    public static IReadOnlyList<IAdditionalCost> BuildFlashbackAdditionalCosts() =>
        new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() };
}

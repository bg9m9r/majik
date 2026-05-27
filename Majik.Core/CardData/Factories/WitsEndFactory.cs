using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wit's End (7th Edition, {5}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target player discards their hand."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {5}{B}{B}, black.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target player"
///   request. On resolution the target player discards every card in their
///   hand (CR 701.7 — each discard is simultaneous for a full-hand effect;
///   implemented as sequential removals since there is no choice involved).
///   If the hand is empty the spell resolves as a no-op (CR 701.7c — can't
///   discard what you don't have).
///
/// ## CR notes
/// - Rule 701.7a — "A player who discards a card puts that card from their
///   hand into their graveyard." Wit's End discards the entire hand, so
///   every card moves to the graveyard.
/// - Rule 701.7c — "If a player is asked to discard a card but has none,
///   nothing happens." Empty-hand resolves cleanly.
/// - Rule 608.2b — Illegal-target check: if the chosen object is not a
///   Player at resolution time the effect does nothing.
/// </summary>
[CardName("Wit's End")]
public static class WitsEndFactory
{
    public const string CardName = "Wit's End";
    public const string PrintedManaCost = "{5}{B}{B}";

    /// <summary>
    /// Build a Wit's End sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// target request + discard body is built on demand via
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
    /// Build the <see cref="SpellDefinition"/> used when Wit's End is cast.
    /// Single 1..1 "target player" request; on resolution that player
    /// discards their entire hand (all cards, no choice involved).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="Game.GameContext"/> (chosen target → live game object).
    /// </param>
    /// <param name="targetAgent">Unused for Wit's End (discarding an entire
    /// hand involves no player choice — all cards go), but accepted for
    /// signature parity with other discard factories. Reserved for future
    /// "discard in a specific order" extension if rules ever require it.
    /// </param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? targetAgent)
    {
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
                    new Effect("Wit's End: target player discards their hand", () =>
                    {
                        // CR 608.2b — illegal-target check.
                        if (raw is not Player victim) return;

                        // Discard every card in the target player's hand.
                        // CR 701.7a — each card moves from hand to graveyard.
                        // CR 701.7c — empty hand resolves as a no-op.
                        var hand = victim.Zones.Hand.GetCards().ToList();
                        foreach (var card in hand)
                        {
                            victim.Zones.Hand.RemoveCard(card);
                            victim.Zones.Graveyard.AddCard(card);
                            card.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}

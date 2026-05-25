using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hymn to Tourach (Fallen Empires, {B}{B}).
///
/// Sorcery. Oracle text:
///   "Target player discards two cards at random."
///
/// ## Why a dedicated factory
/// "At random" discard is a distinct shape from every other discard
/// effect already covered by the spell templates — there's no reveal
/// step, the chooser is the rules engine rather than the caster, and the
/// CR-compliant entropy source has to come from the per-game RNG so
/// replay determinism (Phase 29) holds. None of the existing templates
/// emit that shape, so Hymn gets its own named factory.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}{B}, owner / controller.
/// - <see cref="BuildSpellDefinition"/> declares one 1..1 "target player"
///   <see cref="TargetRequest"/> with a live candidate gatherer covering
///   every player (CR 115.6 — "target player" includes the caster).
/// - Resolution (CR 701.16):
///   1. Materialise the target's hand snapshot.
///   2. Draw two cards from it without replacement using
///      <see cref="GameRandomRegistry.Get"/> for the caster's per-game
///      <see cref="GameRandom"/> (CR 100.6 — single RNG per game,
///      deterministic when seeded).
///   3. Move both picks from hand to graveyard (CR 701.16 — discard).
/// - Hand of size 0 → no-op. Hand of size 1 → discard the one card
///   (CR 701.16 — "discards N cards" caps at the hand size when there
///   aren't enough cards to discard).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal of randomly chosen cards</b>: CR 701.16 has the discarded
///   cards becoming public on resolution. v1 moves them straight to the
///   graveyard (where they're public anyway via the graveyard zone). No
///   <see cref="Majik.Core.Events.CardRevealedEvent"/> is emitted — the
///   wire delta surfaces the discard via the zone-move event. Future
///   work: emit a paired reveal event so the portal can briefly flash
///   "Hymn took X + Y" before they land in the yard.
/// </summary>
[CardName("Hymn to Tourach")]
public static class HymnToTourachFactory
{
    public const string CardName = "Hymn to Tourach";
    public const string PrintedManaCost = "{B}{B}";
    public const int DiscardCount = 2;

    public const string OracleText =
        "Target player discards two cards at random.";

    /// <summary>
    /// Build a Hymn to Tourach sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time target request + random discard
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
    /// Build the <see cref="SpellDefinition"/> used when Hymn to Tourach
    /// is cast. Single 1..1 "target player" request; on resolution the
    /// target discards two cards at random from their hand.
    /// </summary>
    /// <param name="caster">Cast-time controller — used to look up the
    /// per-game <see cref="GameRandom"/> via
    /// <see cref="GameRandomRegistry"/>.</param>
    /// <param name="resolver">Target resolver (chosen target → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Discard,
                    CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: target discards {DiscardCount} at random", () =>
                    {
                        // CR 608.2b — illegal-target check.
                        if (raw is not Player victim) return;

                        // CR 701.16 — "discards N cards at random". Snapshot
                        // the hand so the picker can sample without
                        // replacement; mutate the live zone last.
                        var hand = victim.Zones.Hand.GetCards().ToList();
                        if (hand.Count == 0) return;

                        // CR 100.6 — single per-game RNG. Look up by caster
                        // so the same Hymn cast in a replay draws the same
                        // pair. Registry falls back to a process-wide
                        // GameRandom when nothing is registered (tests bind
                        // a deterministic seed via SetDefault).
                        var rng = GameRandomRegistry.Get(caster);

                        // Sample DiscardCount distinct cards (capped at the
                        // hand size — discarding more than the hand size
                        // is a no-op per CR 701.16 / 701.9 partial-effect).
                        var take = Math.Min(DiscardCount, hand.Count);
                        var picks = new List<ICard>(take);
                        for (var i = 0; i < take; i++)
                        {
                            var idx = rng.Next(hand.Count);
                            picks.Add(hand[idx]);
                            hand.RemoveAt(idx);
                        }

                        foreach (var pick in picks)
                        {
                            // Defensive — sanity check still in hand.
                            if (pick.Zone != ZoneType.Hand) continue;
                            if (!ReferenceEquals(pick.Owner, victim)) continue;
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }
                    }),
                };
            });
    }
}

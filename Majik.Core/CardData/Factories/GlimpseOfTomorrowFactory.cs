using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glimpse of Tomorrow (Zendikar, {3}{R}).
///
/// Sorcery. Oracle text:
///   "Shuffle all permanents you control into your library, then reveal
///    cards from the top of your library until you've revealed that many
///    nonland permanent cards. Put those nonland permanent cards onto
///    the battlefield, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost <c>{3}{R}</c>.
/// - No targets, no modes, no X.
/// - Resolve sequence (CR 121.4 — "then" is a single instruction
///   sequence, so the count is locked between the two halves):
///   1. <b>Snapshot</b>: count the permanents the controller controls
///      <em>before</em> any zone move (this is the count "that many"
///      refers to). Then move every controller-controlled permanent
///      battlefield → library via raw zone manipulation. CR 701.20a —
///      "shuffle ... into your library" implies a shuffle, run via
///      <see cref="LibraryShuffle.ShuffleLibrary"/>.
///   2. <b>Reveal-until-N</b>: peel cards off the top of the controller's
///      library until <c>N</c> nonland permanent cards have been
///      revealed (or the library runs dry — clean stop, same as
///      Goblin Charbelcher / Indomitable Creativity).
///   3. <b>Put onto the battlefield</b>: the revealed nonland permanent
///      cards enter the battlefield under the controller's control via
///      raw zone manipulation. The peeled-but-not-hit cards (lands,
///      instants, sorceries) return to the library; the upcoming
///      shuffle reorders them.
///   4. <b>Then shuffle</b>: CR 701.20a — every "then shuffles" gets a
///      real Fisher-Yates via <see cref="LibraryShuffle.ShuffleLibrary"/>.
///
/// ## v1 gaps
/// - <b>Reveal-event emission</b>: revealed cards are not published on a
///   reveal bus (same gap as every reveal-until factory — Goblin
///   Charbelcher, Ancient Stirrings, Indomitable Creativity).
/// - <b>ZoneService routing</b>: the bulk battlefield → library and the
///   subsequent library → battlefield moves use raw zone mutation, so
///   LTB triggers on the snapshotted permanents (Bridge from Below) and
///   ETB triggers on the reveal-cheated permanents won't fire on this
///   path. Same gap Indomitable Creativity's reveal path documents.
/// - <b>Attached objects</b>: when an Aura's enchanted permanent gets
///   bulk-bounced into the library, the Aura is also moved (it's a
///   permanent the controller controls). CR 704.5n / CR 702.5 — an
///   Aura with no legal enchant target falls off, but here both ends
///   are moved together so the SBA never sees the mismatch. v1
///   simply treats every controller-controlled permanent (Aura
///   included) as a uniform list — no special re-attach pass after the
///   reveal-cheat re-enter.
/// </summary>
[CardName("Glimpse of Tomorrow")]
public static class GlimpseOfTomorrowFactory
{
    public const string CardName = "Glimpse of Tomorrow";
    public const string PrintedManaCost = "{3}{R}";

    /// <summary>
    /// Construct a Glimpse of Tomorrow sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
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
    /// Build the <see cref="SpellDefinition"/> Glimpse of Tomorrow uses
    /// on resolution. No targets, no modes, no X.
    /// </summary>
    /// <param name="caster">The spell controller — the player whose
    /// permanents are shuffled in and whose library is then peeled.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"{CardName}: shuffle permanents into library + reveal-until-N + shuffle",
                    () => Resolve(caster)),
            });
    }

    /// <summary>
    /// Resolve Glimpse of Tomorrow for <paramref name="caster"/>. See
    /// the class xmldoc for the four-step sequence. Exposed for direct
    /// invocation by tests / bots without driving the full cast flow.
    /// </summary>
    public static GlimpseOfTomorrowResolution Resolve(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // Step 1 — snapshot the count and bulk-move permanents into the
        // library. Cast through Permanent so non-Permanent cards
        // (shouldn't exist on the battlefield, but defensively) are
        // ignored.
        var snapshot = caster.Zones.Battlefield.GetCards().OfType<Permanent>().ToList();
        var n = snapshot.Count;

        foreach (var perm in snapshot)
        {
            caster.Zones.Battlefield.RemoveCard(perm);
            caster.Zones.Library.AddCard(perm);
            perm.SetZone(ZoneType.Library);
        }

        // CR 701.20a — "shuffle ... into your library" includes the
        // shuffle. Run it before the reveal so the reveal sees a
        // randomised library.
        LibraryShuffle.ShuffleLibrary(caster, "glimpse-of-tomorrow-shuffle-in");

        // Step 2 + 3 — peel until N nonland permanent cards have been
        // revealed (or the library runs dry). The hits enter the
        // battlefield under the controller's control; the non-hit
        // peeled cards return to the library.
        var hits = new List<ICard>();
        var nonHits = new List<ICard>();
        while (hits.Count < n)
        {
            var top = caster.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) break; // library empty — clean stop.

            caster.Zones.Library.RemoveCard(top);

            if (IndomitableCreativityFactory.IsNonlandPermanentCard(top))
            {
                hits.Add(top);
            }
            else
            {
                nonHits.Add(top);
            }
        }

        foreach (var c in hits)
        {
            caster.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
            c.SetController(caster);
        }

        foreach (var c in nonHits)
        {
            caster.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // Step 4 — "then shuffle" (CR 701.20a).
        LibraryShuffle.ShuffleLibrary(caster, "glimpse-of-tomorrow-shuffle-out");

        return new GlimpseOfTomorrowResolution(
            ShuffledIn: snapshot,
            RevealedHits: hits,
            RevealedNonHits: nonHits);
    }

    /// <summary>
    /// Observation record describing one Glimpse of Tomorrow
    /// resolution. <c>ShuffledIn</c> is the set of permanents that were
    /// bulk-moved from the battlefield to the library (Step 1).
    /// <c>RevealedHits</c> are the nonland permanent cards that re-entered
    /// the battlefield (Step 3). <c>RevealedNonHits</c> are the
    /// peeled-but-not-hit cards (lands, instants, sorceries) — they go
    /// back into the library, the final shuffle reorders them.
    /// </summary>
    public sealed record GlimpseOfTomorrowResolution(
        IReadOnlyList<Permanent> ShuffledIn,
        IReadOnlyList<ICard> RevealedHits,
        IReadOnlyList<ICard> RevealedNonHits);
}

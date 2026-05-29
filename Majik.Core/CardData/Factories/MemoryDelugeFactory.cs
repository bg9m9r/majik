using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Memory Deluge (Innistrad: Midnight Hunt, {2}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Look at the top X cards of your library, where X is the amount of mana
///    spent to cast this spell. Put two of them into your hand and the rest on
///    the bottom of your library in a random order.
///    Flashback {5}{U}{U} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Analogue lineage
/// - The look-at-top-X / put-some-in-hand / bottom-the-rest body is the
///   <see cref="SleightOfHandFactory"/> shape (peek via
///   <see cref="ScryAction.Peek"/>, agent picks via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, raw zone moves),
///   generalised from "look at 2, keep 1" to "look at X, keep 2".
/// - X = "amount of mana spent to cast this spell" is supplied by a
///   caller-provided <c>Func&lt;int&gt; manaSpentProvider</c>, the same
///   mana-provenance posture <see cref="PainfulTruthsFactory"/> uses for
///   Converge (X = colors spent). When null, X defaults to
///   <see cref="DefaultManaSpent"/> (4 — the printed mana value of
///   {2}{U}{U}, the floor any legal cast reaches). Real cast-time
///   provenance plugs in once the mana resolver exposes a per-spell
///   mana-spent ledger to spell definitions.
/// - The Flashback {5}{U}{U} half is exposed via
///   <see cref="BuildFlashbackCost"/> exactly as
///   <see cref="FaithlessLootingFactory.BuildFlashbackCost"/> does — the
///   alt-cost is parsed from the printed oracle text through
///   <see cref="Majik.Core.CardData.FlashbackOracleParser"/>, then wired to
///   the cast flow by callers (graveyard-zone gating + exile-on-resolution
///   are handled by <see cref="FlashbackAlternativeCost"/>; CR 702.34).
///
/// ## Implemented (v1)
/// - Instant shape {2}{U}{U}, materialised from <c>memory-deluge.json</c>
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="EchoingTruthFactory"/>; the JSON schema does not express
///   the look-and-take body, so resolve behaviour is layered on here).
/// - <see cref="BuildResolveEffect"/>:
///     1. Read X via <paramref name="manaSpentProvider"/> (clamped ≥ 0),
///        defaulting to <see cref="DefaultManaSpent"/>.
///     2. CR 701.20-style peek of the top X (Peek tolerates short / empty
///        libraries, returning up to X).
///     3. Put two of the peeked cards into hand — the controller chooses
///        which two via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///        (called twice; pre-agent fallback = the first two peeked, the
///        deterministic posture shared with Sleight of Hand). When fewer
///        than two cards were peeked, all of them go to hand and there is
///        nothing to bottom.
///     4. Put the rest on the BOTTOM of the library in a RANDOM order
///        (CR — "in a random order"): the remaining peeked cards are
///        shuffled with the supplied <see cref="GameRandom"/> (seeded → a
///        deterministic replay sequence) and appended after the existing
///        library tail (index 0 is the top; AddCard appends, so the bottom
///        end receives them).
///
/// ## Deferred (v1 gaps)
/// - <b>Mana-spent ledger</b>: matches Painful Truths / Bring to Light —
///   real cast-time "amount of mana spent" provenance requires the cost
///   flow to expose a per-spell ledger. Until then callers supply
///   <c>manaSpentProvider</c> (tests do; dispatcher path uses
///   <see cref="DefaultManaSpent"/>).
/// - <b>Flashback wiring</b>: <see cref="BuildFlashbackCost"/> returns the
///   alt-cost; callers feed it to the cast flow (same posture as Faithless
///   Looting and every other flashback factory — no new cast plumbing).
/// </summary>
[CardName("Memory Deluge")]
public static class MemoryDelugeFactory
{
    public const string CardName = "Memory Deluge";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "memory-deluge";

    /// <summary>Number of cards put into hand (printed "two of them").</summary>
    public const int PutIntoHandCount = 2;

    /// <summary>
    /// Default X ("amount of mana spent") when no provider is supplied.
    /// The printed cost {2}{U}{U} has mana value 4 — the floor any legal
    /// cast reaches with no cost increases.
    /// </summary>
    public const int DefaultManaSpent = 4;

    /// <summary>
    /// Printed oracle text — the source <see cref="BuildFlashbackCost"/>
    /// parses the Flashback {5}{U}{U} cost from (via
    /// <see cref="Majik.Core.CardData.FlashbackOracleParser"/>). Kept on the
    /// factory so the flashback cost has a single canonical origin.
    /// </summary>
    public const string OracleText =
        "Look at the top X cards of your library, where X is the amount of mana "
        + "spent to cast this spell. Put two of them into your hand and the rest "
        + "on the bottom of your library in a random order.\n"
        + "Flashback {5}{U}{U}";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {2}{U}{U}) from
    /// the embedded JSON definition. Resolve behaviour is built on demand via
    /// <see cref="BuildResolveEffect"/>, mirroring
    /// <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build Memory Deluge's resolve effect — look at the top X cards
    /// (X = mana spent), put two into hand, and bottom the rest in a random
    /// order.
    /// </summary>
    /// <param name="caster">The player looking / drawing.</param>
    /// <param name="manaSpentProvider">Yields X = mana spent to cast. When
    /// null, <see cref="DefaultManaSpent"/> is used (printed MV 4). Clamped
    /// to ≥ 0.</param>
    /// <param name="random">RNG used to randomise the bottomed cards' order
    /// (CR — "in a random order"). Required so seeded replay stays
    /// deterministic; when null a fresh <see cref="GameRandom"/> is created
    /// (test / shape-only convenience).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<int>? manaSpentProvider = null,
        GameRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var rng = random ?? new GameRandom();

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: look at top X (mana spent), put two into hand, "
                + "bottom the rest in a random order.",
                () =>
                {
                    var x = manaSpentProvider?.Invoke() ?? DefaultManaSpent;
                    if (x < 0) x = 0;
                    if (x == 0) return;

                    // CR 701.20 — peek the top X. Peek tolerates short /
                    // empty libraries (returns up to X), so single-card and
                    // empty-library handling falls out for free. Memory
                    // Deluge never says "draw", so an empty library is a
                    // clean no-op (no draw-from-empty SBA).
                    var peeked = ScryAction.Peek(caster, x).ToList();
                    if (peeked.Count == 0) return;

                    // Put two of them into hand. Controller chooses via the
                    // agent (ChooseLibraryPickAsync, called once per slot);
                    // pre-agent fallback = the first peeked card not yet
                    // taken (deterministic, matching Sleight of Hand). When
                    // fewer than two cards were peeked, all of them go to
                    // hand and there is nothing left to bottom.
                    var agent = AgentRegistry.Get(caster);
                    var toHand = new List<ICard>(PutIntoHandCount);
                    for (var i = 0; i < PutIntoHandCount && peeked.Count > 0; i++)
                    {
                        ICard pick;
                        if (agent != null)
                        {
                            // TODO: drop sync-over-async once IEffect.Execute
                            // becomes async.
                            var chosen = agent.ChooseLibraryPickAsync(
                                ctx: null,
                                candidates: peeked,
                                kindLabel: "card to put into your hand")
                                .GetAwaiter().GetResult();

                            // "Put two of them into your hand" is mandatory
                            // (no "may"); a null / off-list agent return is a
                            // mis-wired agent, so fall back to the first
                            // remaining peeked card. Same posture as Sleight
                            // of Hand.
                            pick = chosen != null && peeked.Contains(chosen)
                                ? chosen
                                : peeked[0];
                        }
                        else
                        {
                            pick = peeked[0];
                        }

                        peeked.Remove(pick);
                        toHand.Add(pick);
                    }

                    foreach (var card in toHand)
                    {
                        caster.Zones.Library.RemoveCard(card);
                        caster.Zones.Hand.AddCard(card);
                        card.SetZone(ZoneType.Hand);
                    }

                    // The rest go on the BOTTOM of the library in a RANDOM
                    // order. Library index 0 is the top; AddCard appends, so
                    // shuffling the leftovers and appending them places them
                    // at the bottom in randomised order (the existing
                    // library tail is unchanged).
                    if (peeked.Count == 0) return;
                    rng.Shuffle(peeked);
                    foreach (var card in peeked)
                    {
                        caster.Zones.Library.RemoveCard(card);
                        caster.Zones.Library.AddCard(card);
                        card.SetZone(ZoneType.Library);
                    }
                }),
        };
    }

    /// <summary>
    /// Build Memory Deluge's Flashback {5}{U}{U} alternative cost (CR 702.34)
    /// by parsing <see cref="OracleText"/> through
    /// <see cref="Majik.Core.CardData.FlashbackOracleParser"/>. Callers wire
    /// the returned <see cref="FlashbackAlternativeCost"/> to the cast flow;
    /// graveyard-zone gating and the exile-on-resolution side effect
    /// (CR 702.34b) are handled by the alt-cost itself. Mirrors
    /// <see cref="FaithlessLootingFactory.BuildFlashbackCost"/>.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = Majik.Core.CardData.FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Memory Deluge's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}

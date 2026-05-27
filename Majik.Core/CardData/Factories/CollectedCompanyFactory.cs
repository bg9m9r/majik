using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Collected Company (Dragons of Tarkir, {3}{G}).
///
/// Instant. Oracle text:
///   "Look at the top six cards of your library. You may put up to two
///    creature cards with mana value 3 or less from among them onto the
///    battlefield. Put the rest on the bottom of your library in a
///    random order."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{G}.
/// - <see cref="BuildSpellDefinition"/> returns a <see cref="SpellDefinition"/>
///   with no targets, no variable X, and a single effect closure that:
///     1. Peeks the top six cards of the caster's library (fewer if the
///        library is short — CR 701.21 "top N" never throws).
///     2. Builds the eligible pool: creatures with
///        <see cref="ManaCostValue.TotalValue"/> ≤ 3 (CR 202.3 — mana
///        value reads off the printed cost; X in cost is 0 in any zone
///        other than the stack per CR 202.3b, so cards like Walking
///        Ballista naturally fall under the cap).
///     3. Sequentially prompts the agent (CR 701.19a — searches /
///        look-ats consult the agent) up to two times, refiltering each
///        pass so a previously-picked card never reappears. The agent
///        returns <see langword="null"/> to decline ("up to two" → 0 is
///        legal). Same single-pick loop shape as
///        <see cref="ScapeshiftFactory"/>'s land-tutor cascade.
///     4. Moves picked cards Library → Battlefield. Routes through
///        <see cref="ZoneService.MoveCard"/> when one is registered for
///        the caster (via <see cref="ZoneServiceRegistry"/>) so ETB
///        triggers fire and <c>CardMovedEvent</c> listeners observe the
///        move (CR 603.6a). Raw zone fallback otherwise.
///     5. Re-bottoms the remaining peeked cards in random order
///        (<see cref="GameRandom.Shuffle"/> sourced from
///        <see cref="GameRandomRegistry.Get"/> — deterministic when
///        tests seed it; same posture as
///        <see cref="TibaltsTrickeryFactory"/> / <see cref="GoblinCharbelcherFactory"/>).
/// - The peek does NOT trigger a shuffle (CR 701.20a is a search-effect
///   clause; "look at the top N" is a peek, not a search). The bottom-
///   in-random-order clause provides the randomisation the printed
///   text requires.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" opt-out</b>: an agent that returns picks for both
///   slots gets both put onto the battlefield. The "up to two" upper
///   bound is enforced; the "you may" lower bound is honoured by the
///   agent's right to return <see langword="null"/> at any slot.
/// - <b>Reveal event</b>: the peek does not publish a per-card reveal
///   event. Same gap as the rest of the look-at-top-N family
///   (<see cref="AncientStirringsFactory"/>, <see cref="SleightOfHandFactory"/>).
/// </summary>
[CardName("Collected Company")]
public static class CollectedCompanyFactory
{
    public const string CardName = "Collected Company";
    public const string PrintedManaCost = "{3}{G}";
    public const int PeekCount = 6;
    public const int MaxPicks = 2;
    public const int MaxManaValue = 3;

    /// <summary>
    /// Construct Collected Company owned and controlled by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> Collected Company uses on
    /// resolution.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library
    /// is peeked and onto whose battlefield the picked creatures land.</param>
    /// <param name="zoneService">Optional. When supplied, the picked
    /// creatures' Library → Battlefield moves route through this service
    /// so ETB triggers (CR 603.6a) and <c>CardMovedEvent</c> listeners
    /// fire. When null, <see cref="ZoneServiceRegistry.Get"/> is
    /// consulted, falling back to raw zone manipulation.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"Collected Company: peek top {PeekCount}, put up to {MaxPicks} creatures " +
                    $"with mv ≤ {MaxManaValue} onto the battlefield, rest to bottom in random order.",
                    () => Resolve(caster, zoneService)),
            });
    }

    /// <summary>
    /// Execute Collected Company's resolution against
    /// <paramref name="caster"/>'s library. Public so tests and bots can
    /// drive the resolution without going through SpellCastFlow.
    /// </summary>
    /// <param name="caster">Spell controller — library / battlefield owner.</param>
    /// <param name="zoneService">Optional zone service for routing the
    /// Library → Battlefield move so ETB triggers fire.</param>
    /// <param name="agent">Optional explicit agent that owns the
    /// "up to two" pick decisions. When null, falls back to
    /// <see cref="AgentRegistry.Get"/>; when no agent is registered
    /// either, picks the first eligible candidate each slot
    /// (deterministic pre-agent posture).</param>
    public static void Resolve(
        Player caster,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;

        // 1. Peek up to PeekCount cards (CR 701.21 — short library is fine).
        var peeked = library.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        // 2. Eligible pool: creatures with mv ≤ MaxManaValue (CR 202.3).
        bool IsEligible(ICard c) =>
            c.HasType(CardType.Creature) &&
            ManaCost.Parse(c.ManaCost ?? string.Empty).TotalValue <= MaxManaValue;

        // 3. Sequentially prompt for up to MaxPicks; agent may decline
        //    at any slot (null) per the printed "you may" / "up to" clause.
        agent ??= AgentRegistry.Get(caster);
        var picks = new List<ICard>(MaxPicks);
        var alreadyPicked = new HashSet<ICard>();

        for (int slot = 0; slot < MaxPicks; slot++)
        {
            var candidates = peeked
                .Where(c => !alreadyPicked.Contains(c) && IsEligible(c))
                .ToList();
            if (candidates.Count == 0) break;

            ICard? pick = agent != null
                ? agent.ChooseLibraryPickAsync(
                    ctx: null,
                    candidates: candidates,
                    kindLabel: $"creature card with mana value {MaxManaValue} or less")
                    .GetAwaiter().GetResult()
                : candidates[0];

            // CR 117.x — "you may" / "up to two" lets the agent decline.
            if (pick == null) break;
            // Defensive: agent must pick from the offered candidates.
            if (!candidates.Contains(pick)) break;

            picks.Add(pick);
            alreadyPicked.Add(pick);
        }

        // 4. Move picked cards Library → Battlefield.
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
        foreach (var pick in picks)
        {
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    pick, ZoneType.Library, ZoneType.Battlefield, caster);
            }
            else
            {
                library.RemoveCard(pick);
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(caster);
            }
        }

        // 5. Bottom the rest in random order. Per-game RNG; tests seed it.
        var remainder = peeked.Where(c => !alreadyPicked.Contains(c)).ToList();
        if (remainder.Count > 0)
        {
            var rng = GameRandomRegistry.Get(caster);
            rng.Shuffle(remainder);

            // Library.AddCard appends to the bottom; remove-then-add
            // each remainder card so the new bottom order is the
            // shuffled order. Cards keep their ZoneType.Library tag.
            foreach (var c in remainder)
            {
                library.RemoveCard(c);
            }
            foreach (var c in remainder)
            {
                library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }
    }
}

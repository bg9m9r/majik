using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Indomitable Creativity (Amonkhet, {X}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Destroy X target artifacts and/or creatures. For each permanent
///    destroyed this way, its controller reveals cards from the top of
///    their library until they reveal a nonland permanent card, puts
///    that card onto the battlefield, then shuffles."
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost <c>{X}{R}{R}</c>.
/// - <see cref="SpellDefinition.HasVariableX"/> = true so the cast flow
///   prompts for X at cast time.
/// - One <see cref="TargetRequest"/> with <c>MinTargets = 0,
///   MaxTargets = int.MaxValue</c> gathering every artifact + creature
///   on every battlefield. v1 simplification — the engine's
///   <see cref="TargetRequest"/> can't yet bind <c>MinTargets = X</c>
///   dynamically (no X-keyed target-count primitive). Callers are
///   expected to supply exactly X chosen targets via
///   <see cref="ChosenSpellParams.Targets"/>; the resolve closure
///   silently clamps to the supplied list.
/// - Resolve loop:
///   1. For each chosen target still on the battlefield (CR 608.2b)
///      AND still an Artifact or Creature: destroy via
///      <see cref="Fx.MoveToGraveyard"/> with
///      <see cref="ZoneMoveReason.Destroy"/> — indestructible (CR 702.12)
///      and regeneration (CR 701.15) gates apply normally.
///   2. For each permanent that was actually destroyed: its controller
///      reveals from the top of their library until a nonland permanent
///      card surfaces. That card is moved to the battlefield under the
///      revealing player's control; remaining revealed cards stay in the
///      library tail. The revealing player then shuffles
///      (CR 701.20a — every "then shuffles" gets a real Fisher-Yates).
///
/// ## v1 gaps
/// - <b>X-keyed target count</b>: there is no <c>MinTargets = X</c>
///   binding on <see cref="TargetRequest"/>; callers must pre-supply
///   exactly X targets. The resolve closure trusts the chosen-target
///   list cardinality.
/// - <b>Reveal-event emission</b>: revealed cards are not published on a
///   reveal bus (same gap as every reveal-until factory — Goblin
///   Charbelcher, Ancient Stirrings, Tibalt's Trickery).
/// - <b>"Permanent card" filter</b>: a card is a "permanent card" iff its
///   types intersect {Creature, Artifact, Enchantment, Land,
///   Planeswalker, Battle} (CR 110.4 — Battle joined the permanent type
///   roster). The reveal predicate enumerates the permanent types
///   explicitly (CR 110.4a) rather than gating on a single
///   <c>IsPermanentType</c> helper.
/// - <b>ZoneService routing</b> for the reveal-driven Library →
///   Battlefield move is deferred (same gap as Chord of Calling's
///   single-arg dispatcher path). The fallback uses raw zone mutation;
///   ETB triggers on the reveal-cheated permanent won't fire on this
///   path until the cast flow threads a <see cref="Majik.Core.Services.ZoneService"/>
///   into the resolve closure.
/// </summary>
[CardName("Indomitable Creativity")]
public static class IndomitableCreativityFactory
{
    public const string CardName = "Indomitable Creativity";
    public const string PrintedManaCost = "{X}{R}{R}";

    /// <summary>
    /// Construct an Indomitable Creativity sorcery owned and controlled
    /// by <paramref name="owner"/>. Card shape only — the resolve-time
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
    /// Build the <see cref="SpellDefinition"/> Indomitable Creativity
    /// uses on resolution. <see cref="SpellDefinition.HasVariableX"/> is
    /// true so the engine prompts for X at cast time; the
    /// <see cref="TargetRequest"/> is open-cardinality and callers
    /// pre-supply exactly X targets (see class xmldoc gap note).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields enumerate
    /// candidate targets. Typically <c>Game.Players</c>.</param>
    public static SpellDefinition BuildSpellDefinition(IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "X target artifacts and/or creatures",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    $"{CardName}: destroy X target artifacts/creatures + reveal-until-permanent per destroyed",
                    () => Resolve(p.Targets.Count == 0
                        ? Array.Empty<object>()
                        : p.Targets[0])),
            });
    }

    /// <summary>
    /// Resolve Indomitable Creativity against the supplied chosen
    /// targets. Exposed for direct invocation by tests / bots without
    /// driving the full cast flow.
    ///
    /// Two-pass shape so the reveal loop doesn't re-process targets that
    /// fizzled at the destroy step (CR 608.2b — illegal target → its
    /// share of the rider effect is skipped).
    /// </summary>
    public static IndomitableCreativityResolution Resolve(IReadOnlyList<object> chosenTargets)
    {
        ArgumentNullException.ThrowIfNull(chosenTargets);

        // Pass 1 — destroy each chosen target that's still a legal
        // (Artifact or Creature) permanent on the battlefield. Record
        // its controller for the reveal pass.
        var destroyed = new List<Permanent>();
        foreach (var raw in chosenTargets)
        {
            if (raw is not Permanent perm) continue;
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (!(perm.HasType(CardType.Artifact) || perm.HasType(CardType.Creature))) continue;

            var controllerSnapshot = perm.Controller;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) cancels;
            // regeneration shield (CR 701.15) is consumed normally
            // (no "can't be regenerated" rider on Creativity).
            var preZone = perm.Zone;
            Fx.MoveToGraveyard(perm, ZoneMoveReason.Destroy);

            // Only count the destroy when the permanent actually left
            // the battlefield (indestructible / regen → still on
            // battlefield → no reveal trigger per "destroyed this way").
            if (perm.Zone != ZoneType.Battlefield && controllerSnapshot != null)
            {
                destroyed.Add(perm);
                _lastReveals[perm] = controllerSnapshot;
            }
        }

        // Pass 2 — for each permanent that was actually destroyed, its
        // controller reveals until a nonland permanent card. CR 701.20a
        // — shuffle after the reveal.
        var reveals = new List<RevealEvent>();
        foreach (var perm in destroyed)
        {
            if (!_lastReveals.TryGetValue(perm, out var controller)) continue;
            _lastReveals.Remove(perm);

            var ev = RevealUntilPermanent(controller);
            reveals.Add(ev);
        }

        return new IndomitableCreativityResolution(destroyed, reveals);
    }

    // Per-Resolve scratchpad — keyed by destroyed permanent so a single
    // resolution's pass-1 → pass-2 handoff doesn't leak across calls.
    // Cleared at the end of Resolve; cleared again in pass-2 per entry.
    private static readonly Dictionary<Permanent, Player> _lastReveals = new();

    /// <summary>
    /// Reveal cards from the top of <paramref name="player"/>'s library
    /// until a nonland permanent card surfaces. That card is moved to
    /// the battlefield under <paramref name="player"/>'s control;
    /// preceding revealed cards (lands + nonland nonpermanents — instants
    /// and sorceries) go back on top of the library in the same order
    /// they were peeled, then the library is shuffled (CR 701.20a —
    /// every "then shuffles" picks up the post-move stack and reorders).
    /// Library empty before a hit → no permanent enters; the revealed
    /// pile is replaced and shuffled regardless.
    /// </summary>
    public static RevealEvent RevealUntilPermanent(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var library = player.Zones.Library;
        var peeled = new List<ICard>();
        ICard? hit = null;

        while (true)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break; // library empty — clean stop (CR 608.2b parity).

            library.RemoveCard(top);
            peeled.Add(top);

            if (IsNonlandPermanentCard(top))
            {
                hit = top;
                break;
            }
        }

        // If a hit was revealed, it enters the battlefield under the
        // revealing player's control. The other peeled cards return to
        // the library — the upcoming shuffle reorders them.
        if (hit != null)
        {
            peeled.Remove(hit);
            player.Zones.Battlefield.AddCard(hit);
            hit.SetZone(ZoneType.Battlefield);
            hit.SetController(player);
        }

        // Put the leftover peeled cards back into the library
        // (the shuffle below randomises the order).
        foreach (var c in peeled)
        {
            library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // CR 701.20a — shuffle after the reveal.
        LibraryShuffle.ShuffleLibrary(player, "indomitable-creativity");

        return new RevealEvent(peeled, hit);
    }

    /// <summary>
    /// CR 110.4 — a "permanent card" is one whose types intersect the
    /// permanent type roster (Creature, Artifact, Enchantment, Land,
    /// Planeswalker, Battle). For Indomitable Creativity's reveal-until
    /// clause we further require <em>nonland</em> (the printed text).
    /// </summary>
    public static bool IsNonlandPermanentCard(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.HasType(CardType.Land)) return false;
        return card.HasType(CardType.Creature)
            || card.HasType(CardType.Artifact)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Planeswalker);
    }

    /// <summary>
    /// Observation record for one reveal-until-permanent loop —
    /// the peeled-but-not-hit cards (lands / instants / sorceries seen
    /// before the terminator) and the nonland permanent card that
    /// terminated the loop (null when the library ran dry first).
    /// </summary>
    public sealed record RevealEvent(IReadOnlyList<ICard> Peeled, ICard? Hit);

    /// <summary>
    /// Observation record for one Indomitable Creativity resolution —
    /// the destroyed permanents (legal + actually moved to graveyard)
    /// and the matching reveal events. <c>Destroyed[i]</c> aligns with
    /// <c>Reveals[i]</c>.
    /// </summary>
    public sealed record IndomitableCreativityResolution(
        IReadOnlyList<Permanent> Destroyed,
        IReadOnlyList<RevealEvent> Reveals);
}

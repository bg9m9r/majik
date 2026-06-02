using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Engulf the Shore (Eldritch Moon, {3}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Return to their owners' hands all creatures with toughness less than
///    or equal to the number of Islands you control."
///
/// A mass return-to-hand bounce gated by a dynamic threshold — the caster's
/// Island count, computed once at resolution. It blends two existing shapes:
/// the return-to-owners'-hand bounce of <see cref="EchoingTruthFactory"/>
/// (CR 701.10 — each creature goes to ITS OWN owner's hand) with the
/// count-of-Islands board scan of <see cref="BoilFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {3}{U}, materialised from the
///   embedded JSON definition (<c>engulf-the-shore.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="EchoingTruthFactory"/>.
/// - <b>Resolve effect</b> built on demand via
///   <see cref="BuildResolveEffect"/>: callers supply the caster (whose
///   Islands set the threshold) and every player whose battlefield should be
///   swept (typically <c>Game.Players</c>). This mirrors the positional
///   <c>BuildResolveEffect(allPlayers)</c> of <see cref="BoilFactory"/> /
///   <see cref="PyroclasmFactory"/>, with the extra <c>caster</c> argument
///   for the "you control" count.
///
/// ## Rules notes
/// - <b>"Islands you control"</b> (CR 109.5 — "you" is the spell's
///   controller): the threshold counts ONLY the caster's battlefield
///   permanents with <see cref="CardSubtype.Island"/> (CR 205.3i — Island the
///   subtype, basic/nonbasic alike). It is fixed once at resolution; Islands
///   removed mid-resolution by an earlier replacement do not change a
///   threshold already read.
/// - <b>The bounce is not targeted</b> — "all creatures" is a global scan, so
///   it ignores shroud / hexproof / protection (CR 115.6 — only targeted
///   effects check those). It reaches every creature on every battlefield
///   regardless of controller, the caster's own included.
/// - <b>Inclusive boundary</b> — "less than OR equal to" means a creature
///   whose toughness equals the Island count is also returned.
/// - <b>Effective toughness</b> — <see cref="Creature.Toughness"/> already
///   folds in continuous effects (CR 613), so a pumped/shrunk creature is
///   judged by its current toughness at resolution.
/// - <b>Owners' hands, plural</b> (CR 701.10) — each returned creature goes
///   to ITS OWN owner's hand, not the caster's.
/// </summary>
[CardName("Engulf the Shore")]
public static class EngulfTheShoreFactory
{
    public const string CardName = "Engulf the Shore";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "engulf-the-shore";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {3}{U}) from the
    /// embedded JSON definition. Resolve behaviour is supplied separately via
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
    /// Build Engulf the Shore's resolve effect — return to their owners' hands
    /// every creature (on any supplied battlefield) whose effective toughness
    /// is &lt;= the number of Islands the <paramref name="caster"/> controls.
    /// </summary>
    /// <param name="caster">The spell's controller. Only this player's Islands
    /// count toward the toughness threshold ("Islands you control",
    /// CR 109.5).</param>
    /// <param name="allPlayers">All players whose battlefields the bounce
    /// should reach. Typically <c>Game.Players</c> — the sweep is
    /// controller-agnostic on the creatures it returns.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: return all creatures with toughness <= Islands "
                + "you control to their owners' hands.",
                () =>
                {
                    // CR 109.5 / CR 205.3i — count ONLY the caster's Islands;
                    // fixed once at resolution.
                    var threshold = caster.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Count(p => p.HasSubtype(CardSubtype.Island));

                    // CR 115.6 — not a targeted effect, so every battlefield is
                    // scanned regardless of shroud/hexproof. Snapshot first so
                    // a bounce (a zone move) doesn't disturb enumeration
                    // (mirrors the Echoing Truth / Boil snapshot pattern).
                    var toBounce = allPlayers
                        .SelectMany(pl => pl.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        // Inclusive boundary — "less than OR equal to".
                        .Where(c => c.Toughness <= threshold)
                        .ToList();

                    foreach (var creature in toBounce)
                    {
                        // CR 608.2b guard — a same-step move may have already
                        // pulled this creature off the battlefield.
                        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield)
                            continue;

                        ReturnToOwnersHand(creature);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 701.10 — return a single creature to its owner's hand via raw zone
    /// manipulation (same posture as <see cref="EchoingTruthFactory"/>'s
    /// no-ZoneService path).
    /// </summary>
    private static void ReturnToOwnersHand(Creature creature)
    {
        var owner = creature.Owner;
        if (owner == null) return;

        var controller = creature.Controller ?? owner;

        controller.Zones.Battlefield.RemoveCard(creature);
        owner.Zones.Hand.AddCard(creature);
        creature.SetZone(Majik.Core.Zones.ZoneType.Hand);
        creature.SetController(owner);
    }
}

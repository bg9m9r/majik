using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the split card Boom // Bust (Planeshift,
/// {1}{R} // {5}{R}). Both faces are Sorceries.
///
/// ## Card text (Scryfall verified)
///   Boom {1}{R} — Sorcery: "Destroy target land you control and target land
///     you don't control."
///   Bust {5}{R} — Sorcery: "Destroy all lands."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one
/// face to cast and only that face's mana cost / effect applies (CR 712.4a).
/// On the battlefield neither face is a permanent — both halves are
/// Sorceries here, so resolution is a one-shot effect that then heads to the
/// graveyard.
///
/// The combined card name "Boom // Bust" is the <c>[CardName]</c> dispatch
/// key (matching the embedded seed row), exactly the two-face posture of
/// <see cref="SinkIntoStuporFactory"/>. The card SHAPE is materialised from
/// the embedded JSON definition (<c>boom-bust.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// <see cref="SpellDefinition"/> is built on demand by the methods below.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Sorcery, red, combined card name. The combined card
///   carries the front (Boom) face's {1}{R} cost — the engine's split-cast
///   plumbing selects the per-face cost when each face is cast; the printed
///   front cost is the natural default for the single combined object.
/// - <b>Boom</b> — two 1..1 <see cref="TargetRequest"/>s:
///     <list type="bullet">
///       <item>"target land you control" — candidate gatherer offers only
///         lands the caster controls.</item>
///       <item>"target land you don't control" — candidate gatherer offers
///         only lands the caster does NOT control.</item>
///     </list>
///   Resolution destroys each chosen land (CR 701.7 → owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>, which honours
///   Indestructible CR 702.12 and regeneration CR 701.15). Each target is
///   re-checked for legality at resolution (CR 608.2b): the "you control"
///   land must still be controlled by the caster and the "you don't
///   control" land must not be — an illegal pick for either slot simply does
///   nothing for that slot. Same control-relative-to-caster filtering shape
///   as <see cref="SinkIntoStuporFactory"/>; the destroy body mirrors the
///   land-destruction <see cref="StoneRainFactory"/> / <see cref="BoilFactory"/>
///   posture.
/// - <b>Bust</b> — "Destroy all lands." Symmetric Armageddon-style sweep
///   (CR 700.3 — no controller restriction). <see cref="BuildBustResolveEffect"/>
///   snapshots every supplied player's lands and routes each to its owner's
///   graveyard. Same shape as <see cref="BoilFactory.BuildResolveEffect"/>
///   (the only delta: filter on <see cref="CardType.Land"/> rather than the
///   Island subtype).
///
/// ## Deferred (v1 gaps)
/// - <b>Per-face cast cost selection.</b> The combined object exposes the
///   Boom cost; selecting {5}{R} for Bust is the split-card cast-plumbing's
///   job. The per-face resolve definitions here are independent of how the
///   cast cost is chosen.
/// </summary>
[CardName("Boom // Bust")]
public static class BoomBustFactory
{
    public const string CardName = "Boom // Bust";
    public const string Slug = "boom-bust";

    /// <summary>CR 712 — Boom (front face) printed cost.</summary>
    public const string BoomManaCost = "{1}{R}";

    /// <summary>CR 712 — Bust (back face) printed cost.</summary>
    public const string BustManaCost = "{5}{R}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Sorcery, red, combined name "Boom // Bust"). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; per-face resolve
    /// behaviour is built on demand via <see cref="BuildBoomDefinition"/> /
    /// <see cref="BuildBustResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time definition for the Boom face:
    /// "Destroy target land you control and target land you don't control."
    ///
    /// Two 1..1 target requests — slot 0 = "land you control", slot 1 =
    /// "land you don't control" — each with a candidate gatherer that filters
    /// lands by control relative to <paramref name="caster"/>. Resolution
    /// destroys each chosen land (CR 701.7), re-checking legality at
    /// resolution (CR 608.2b).
    /// </summary>
    /// <param name="caster">The player casting Boom; used to split the two
    /// candidate pools and to re-check control at resolution.</param>
    /// <param name="resolver">Resolves a chosen target token to the live
    /// game object.</param>
    public static SpellDefinition BuildBoomDefinition(
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
                    "target land you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherLands(ctx, caster, youControl: true)),
                new TargetRequest(
                    "target land you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherLands(ctx, caster, youControl: false)),
            },
            EffectFactory: chosen =>
            {
                // Slot 0 = "land you control"; slot 1 = "land you don't control".
                var youControlRaw = resolver(chosen.Targets[0][0]);
                var youDontRaw = resolver(chosen.Targets[1][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} (Boom): destroy target land you control and target land you don't control",
                        () =>
                        {
                            DestroyLandIfControlMatches(youControlRaw, caster, mustControl: true);
                            DestroyLandIfControlMatches(youDontRaw, caster, mustControl: false);
                        }),
                };
            });
    }

    /// <summary>
    /// Build the resolve effect for the Bust face: "Destroy all lands."
    /// Symmetric Armageddon-style sweep — every land on every supplied
    /// player's battlefield is destroyed regardless of controller
    /// (CR 700.3). Each land is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7 — plain
    /// Destroy; Indestructible CR 702.12 cancels and regeneration CR 701.15
    /// is honoured, matching the printed text which has no "can't be
    /// regenerated" rider). Mirrors <see cref="BoilFactory.BuildResolveEffect"/>.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields are swept —
    /// typically <c>Game.Players</c>.</param>
    public static IReadOnlyList<IEffect> BuildBustResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName} (Bust): destroy all lands.", () =>
            {
                // Snapshot every battlefield up front — MoveToGraveyard
                // mutates the source zone in place.
                foreach (var pl in allPlayers)
                {
                    if (pl == null) continue;
                    var lands = pl.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(p => p.HasType(CardType.Land))
                        .ToList();
                    foreach (var land in lands)
                    {
                        OracleSpellBinder.MoveToGraveyard(
                            land, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }

    /// <summary>
    /// Gather land candidates for one Boom slot. When
    /// <paramref name="youControl"/> is true, only lands the
    /// <paramref name="caster"/> controls are offered; otherwise only lands
    /// the caster does NOT control. Lands in any player's battlefield are
    /// considered (so opponent lands are visible for the "you don't control"
    /// slot — CR 115.1).
    /// </summary>
    private static IReadOnlyList<object> GatherLands(
        GameContext ctx, Player caster, bool youControl)
    {
        var pool = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            foreach (var perm in p.Zones.Battlefield.GetCards().OfType<Permanent>())
            {
                if (!perm.HasType(CardType.Land)) continue;
                var controller = perm.Controller ?? perm.Owner;
                var casterControls = ReferenceEquals(controller, caster);
                if (casterControls == youControl) pool.Add(perm);
            }
        }
        return pool;
    }

    /// <summary>
    /// Destroy <paramref name="raw"/> if it is a land whose control state
    /// matches the slot (CR 608.2b resolution-time legality):
    /// <list type="bullet">
    ///   <item><paramref name="mustControl"/> true → the land must still be
    ///     controlled by <paramref name="caster"/> ("land you control").</item>
    ///   <item><paramref name="mustControl"/> false → the land must NOT be
    ///     controlled by the caster ("land you don't control").</item>
    /// </list>
    /// An illegal pick does nothing for that slot.
    /// </summary>
    private static void DestroyLandIfControlMatches(
        object raw, Player caster, bool mustControl)
    {
        if (raw is not Permanent land) return;
        if (land.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;
        if (!land.HasType(CardType.Land)) return;

        var controller = land.Controller ?? land.Owner;
        var casterControls = ReferenceEquals(controller, caster);
        if (casterControls != mustControl) return;

        // CR 701.7 — Destroy. Indestructible (CR 702.12) cancels; regeneration
        // shield (CR 701.15) is honoured (no "can't be regenerated" rider).
        OracleSpellBinder.MoveToGraveyard(
            land, Majik.Core.Zones.ZoneMoveReason.Destroy);
    }
}

using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fumigate (Aether Revolt, {3}{W}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Destroy all creatures. You gain 1 life for each creature destroyed
///    this way."
///
/// ## Why a named factory
/// Fumigate is a symmetric board wipe (CR 109.5 — "Destroy all creatures",
/// no controller restriction) with a count-of-kills life-gain rider. The
/// engine already ships both primitives:
/// <list type="bullet">
///   <item><b>Symmetric mass destroy</b> — every creature is routed to its
///   owner's graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7), the same board-wipe posture as
///   <see cref="WrathOfGodFactory"/> / <see cref="DayOfJudgmentFactory"/> /
///   <see cref="CruxOfFateFactory"/>.</item>
///   <item><b>Count-destroyed rider</b> — the caster gains 1 life per
///   creature that ACTUALLY moved to the graveyard, the same "destroyed
///   this way" gate <see cref="DecreeOfPainFactory"/> uses for its
///   per-controller discard.</item>
/// </list>
/// No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{W}{W}, white. Card shape comes from the
///   embedded JSON (<c>fumigate.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Resolve (<see cref="BuildResolveEffect"/>): sweep every supplied
///   player's battlefield (CR 109.5 — symmetric), then gain 1 life per
///   creature destroyed this way.
///
/// ## "Destroyed this way" counting (CR 701.7)
/// The life-gain counts only creatures that actually left the battlefield
/// for the graveyard. A creature that survives the destroy — indestructible
/// (CR 702.12b cancels the move) or a regeneration shield (CR 701.15c
/// consumes it, since plain "Destroy all creatures" carries NO "can't be
/// regenerated" rider) — was NOT "destroyed this way" and does not count
/// toward the life gained. We snapshot each creature up front and, after
/// the sweep, count only those whose post-move zone is the graveyard.
///
/// ## Rules citations
/// - CR 109.5 — symmetric sweep; no controller restriction.
/// - CR 701.7 — destroy → owner's graveyard; plain Destroy (indestructible
///   CR 702.12 cancels, regeneration shields CR 701.15 are consumed normally
///   — no "can't be regenerated" rider on Fumigate).
/// - CR 119.x — life gain; the count of creatures "destroyed this way"
///   drives the amount.
/// </summary>
[CardName("Fumigate")]
public static class FumigateFactory
{
    public const string CardName = "Fumigate";
    public const string Slug = "fumigate";
    public const string PrintedManaCost = "{3}{W}{W}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Fumigate. No targets, no
    /// X — the symmetric sweep + life rider is entirely a resolution body
    /// (CR 701.7). The caster is needed both to scope the sweep across all
    /// players' battlefields and as the player who gains life.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        IReadOnlyList<Player> allPlayers, Player caster)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(allPlayers, caster));
    }

    /// <summary>
    /// Build Fumigate's resolve effect — destroy every <see cref="Creature"/>
    /// on every supplied player's battlefield (CR 109.5 / CR 701.7), then
    /// have <paramref name="caster"/> gain 1 life per creature actually
    /// destroyed this way.
    ///
    /// Exposed for direct invocation by tests / bots without driving the
    /// full resolution pipeline (same posture as
    /// <see cref="CruxOfFateFactory.BuildResolveEffect"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (off-oracle).</param>
    /// <param name="caster">The player who gains life — Fumigate's "You" (CR
    /// 109.5 — only the caster gains, even though the sweep is symmetric).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers, Player caster)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all creatures; gain 1 life per creature destroyed this way.",
                () =>
                {
                    // Snapshot every battlefield up front — MoveToGraveyard
                    // mutates the source zone in place (collection-modified
                    // guard).
                    var creatures = new List<Creature>();
                    foreach (var pl in allPlayers)
                    {
                        if (pl == null) continue;
                        creatures.AddRange(
                            pl.Zones.Battlefield.GetCards().OfType<Creature>());
                    }

                    // CR 701.7 — plain Destroy (no "can't be regenerated"
                    // rider): indestructible (CR 702.12) cancels and any
                    // active regeneration shield (CR 701.15) is consumed at
                    // the binder. We deliberately use ZoneMoveReason.Destroy
                    // (regen-honouring) — matches Day of Judgment / Crux of
                    // Fate, NOT the Wrath/Damnation no-regen reason.
                    foreach (var c in creatures)
                    {
                        OracleSpellBinder.MoveToGraveyard(
                            c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }

                    // "You gain 1 life for each creature destroyed this way."
                    // Count only creatures that ACTUALLY landed in the
                    // graveyard — indestructible / regenerated survivors were
                    // not "destroyed this way" (CR 701.7) and do not count.
                    var destroyed = creatures.Count(
                        c => c.Zone == Majik.Core.Zones.ZoneType.Graveyard);

                    if (destroyed > 0)
                    {
                        caster.GainLife(destroyed);
                    }
                }),
        };
    }
}

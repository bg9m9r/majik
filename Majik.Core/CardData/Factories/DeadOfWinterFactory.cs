using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dead of Winter (Modern Horizons, {2}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "All nonsnow creatures get -X/-X until end of turn, where X is the
///    number of snow permanents you control."
///
/// ## Implementation (v1)
/// Card shape (name / Sorcery / {2}{B}) is materialised from the embedded
/// JSON definition (<c>dead-of-winter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="BitingRainFactory"/> (the JSON <c>SpellDefinition</c> schema
/// does not express a magnitude-driven nonsnow-creature sweep, so the resolve
/// behaviour is layered on here via <see cref="BuildResolveEffect"/>).
///
/// This is the snow-keyed, magnitude-driven sibling of
/// <see cref="BitingRainFactory"/> (fixed -2/-2) and
/// <see cref="ToxicDelugeFactory"/> (-X/-X by life paid). Two differences from
/// the plain symmetric sweep:
///
/// 1. <b>X is derived, not chosen</b>: X = the number of snow permanents the
///    caster controls at resolution (CR 109.5 — "you" is the spell's
///    controller). Counted via <see cref="Permanent.HasEffectiveSupertype"/>
///    so a granted Snow supertype is honoured (CR 205.4 / CR 613.1d).
///    "Permanents" — every permanent type, not just creatures.
/// 2. <b>Affects only nonsnow creatures</b> (CR 205.4 — the printed "nonsnow"
///    restriction): snow creatures are excluded from the -X/-X sweep, again via
///    <see cref="Permanent.HasEffectiveSupertype"/>. Still symmetric across all
///    players' battlefields (CR 109.5).
///
/// On resolve, register a <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per
/// nonsnow <see cref="Creature"/> on every supplied player's battlefield against
/// the engine's per-creature continuous-effects service
/// (<see cref="Card.ActiveEffects"/>). Layer 7c modify with EOT expiry
/// (CR 613.4 / CR 514.2 — cleanup step ends the effect). Sign-agnostic — the
/// layer system handles toughness ≤ 0 SBA death via the standard
/// creature-death check (CR 704.5f). When X = 0 (no snow permanents) the sweep
/// is a -0/-0 no-op.
///
/// CR rule references: 109.5 (symmetric sweep, "you" = controller), 117.5 (mana
/// cost), 205.4 (Snow supertype / "nonsnow"), 514.2 (EOT cleanup), 613.1d /
/// 613.4 (continuous-effects layers), 704.5f (toughness 0 creature-death SBA).
/// </summary>
[CardName("Dead of Winter")]
public static class DeadOfWinterFactory
{
    public const string CardName = "Dead of Winter";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "dead-of-winter";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {2}{B}) from the
    /// embedded JSON definition. Resolve behaviour (-X/-X to all nonsnow
    /// creatures) is built on demand via <see cref="BuildResolveEffect"/>,
    /// mirroring <see cref="BitingRainFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 205.4 — X = the number of snow permanents
    /// <paramref name="caster"/> controls. Reads through
    /// <see cref="Permanent.HasEffectiveSupertype"/> so a granted Snow supertype
    /// counts (CR 613.1d).
    /// </summary>
    public static int CountSnowPermanents(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Count(p => p.HasEffectiveSupertype(CardSupertype.Snow));
    }

    /// <summary>
    /// Build Dead of Winter's resolve effect — compute X (snow permanents the
    /// caster controls) then register a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per <b>nonsnow</b>
    /// creature on every supplied player's battlefield (CR 109.5 — symmetric
    /// sweep; CR 205.4 — "nonsnow" restriction). EOT cleanup is handled by the
    /// shared layer-system expiry (CR 514.2). Same shape every -N/-N sweep uses
    /// (mirrors <see cref="BitingRainFactory.BuildResolveEffect"/> and
    /// <see cref="ToxicDelugeFactory.BuildResolveEffect"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be swept.
    /// Typically <c>Game.Players</c>.</param>
    /// <param name="caster">The casting player; X = the number of snow
    /// permanents this player controls (CR 109.5 — "you").</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: all nonsnow creatures -X/-X EOT (X = snow permanents you control)",
                () =>
                {
                    // X is computed at resolution (CR 608.2 — resolve-time
                    // values), not at cast — snow permanents can change in
                    // response to the spell.
                    var x = CountSnowPermanents(caster);
                    if (x <= 0)
                    {
                        // -0/-0 is a no-op; skip registering inert effects.
                        return;
                    }

                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards()
                                     .OfType<Creature>()
                                     // CR 205.4 — "nonsnow" excludes snow creatures.
                                     .Where(c => !c.HasEffectiveSupertype(CardSupertype.Snow))
                                     .ToList())
                        {
                            if (c.ActiveEffects != null)
                            {
                                c.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(c, -x, -x));
                            }
                        }
                    }
                }),
        };
    }
}

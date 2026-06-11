using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Biting Rain (Torment, {2}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-10):
///   "All creatures get -2/-2 until end of turn."
///   Madness {2}{B}
///
/// ## Madness is intrinsic — NOT wired here
/// Madness {2}{B} is handled engine-wide by <c>MadnessCatalog</c> (name→cost)
/// consulted by the central discard funnel <c>Fx.DiscardCard</c> (CR 702.35):
/// a discarded madness card is routed to exile and offered for its madness cost
/// automatically. The printed "Madness {2}{B}" line therefore needs NO factory
/// code and NO JSON entry. This factory implements ONLY the spell body — the
/// symmetric -2/-2 sweep.
///
/// ## Implementation (v1)
/// Card shape (name / Sorcery / {2}{B}{B}) is materialised from the embedded
/// JSON definition (<c>biting-rain.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="BileBlightFactory"/> (the JSON <c>SpellDefinition</c> schema does
/// not express an untargeted all-creatures sweep, so the resolve behaviour is
/// layered on here via <see cref="BuildResolveEffect"/>).
///
/// The sweep is the fixed-magnitude sibling of <see cref="LanguishFactory"/>
/// (-4/-4) and <see cref="ToxicDelugeFactory"/> (-X/-X): on resolve, register a
/// <see cref="PumpUntilEndOfTurnEffect"/>(c, -2, -2) per <see cref="Creature"/>
/// on every supplied player's battlefield (CR 109.5 — symmetric sweep) against
/// the engine's per-creature continuous-effects service
/// (<see cref="Card.ActiveEffects"/>). Layer 7c modify with EOT expiry
/// (CR 613.4 / CR 514.2 — cleanup step ends the effect). Sign-agnostic — the
/// layer system handles toughness ≤ 0 SBA death via the standard
/// creature-death check (CR 704.5f).
///
/// CR rule references: 109.5 (symmetric sweep), 117.5 (mana cost), 514.2 (EOT
/// cleanup), 613.4 (continuous-effects layer 7c), 702.35 (Madness — intrinsic),
/// 704.5f (toughness 0 creature-death SBA).
/// </summary>
[CardName("Biting Rain")]
public static class BitingRainFactory
{
    public const string CardName = "Biting Rain";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "biting-rain";

    /// <summary>The fixed sweep magnitude: -2/-2 (CR 613.4 Layer 7c).</summary>
    public const int PumpAmount = -2;

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {2}{B}{B}) from the
    /// embedded JSON definition. Resolve behaviour (-2/-2 to all creatures) is
    /// built on demand via <see cref="BuildResolveEffect"/>, mirroring
    /// <see cref="BileBlightFactory"/>.
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
    /// Build Biting Rain's resolve effect — register a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(c, -2, -2) per creature on every
    /// supplied player's battlefield (CR 109.5 — symmetric sweep). EOT cleanup
    /// is handled by the shared layer-system expiry (CR 514.2). Same shape every
    /// -N/-N sweep uses (mirrors <see cref="LanguishFactory.BuildResolveEffect"/>
    /// and <see cref="ToxicDelugeFactory.BuildResolveEffect"/>).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be swept.
    /// Typically <c>Game.Players</c>; pass <c>new[] { caster }</c> for a
    /// controller-only sweep (off-oracle).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: all creatures {PumpAmount:+#;-#;0}/{PumpAmount:+#;-#;0} EOT",
                () =>
                {
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                        {
                            if (c.ActiveEffects != null)
                            {
                                c.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(c, PumpAmount, PumpAmount));
                            }
                        }
                    }
                }),
        };
    }
}

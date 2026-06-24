using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terror Tide (Modern Horizons 3, {2}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Fathomless descent — All creatures get -X/-X until end of turn,
///    where X is the number of permanent cards in your graveyard."
///
/// ## Implementation
///
/// "Fathomless descent" is purely an ability-word flavor label (CR 207.2c)
/// — it carries no rules text of its own; the count of permanent cards in
/// the caster's graveyard is the entire mechanic. So this is a variable-X
/// symmetric -X/-X sweep cribbed directly from
/// <see cref="LanguishFactory"/>, with the constant -4 replaced by a
/// per-resolve count.
///
/// X is computed at resolution (CR 608.2 — a one-shot effect's variables
/// are locked in as it resolves) as the number of *permanent cards* in the
/// caster's graveyard (CR 110.4a / 300.1 — land, creature, artifact,
/// enchantment, or planeswalker; instants and sorceries are NOT permanent
/// cards). The classification is the shared
/// <see cref="WrennAndSevenFactory.IsPermanentCard(ICard)"/> predicate.
///
/// Resolve registers a <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X)
/// per <see cref="Creature"/> on every supplied player's battlefield
/// (CR 109.5 — symmetric sweep) against the engine's per-creature
/// continuous-effects service (<see cref="Card.ActiveEffects"/>). Layer 7c
/// modify with EOT expiry (CR 613.4 / CR 514.2 — cleanup ends the effect).
/// Sign-agnostic: toughness ≤ 0 death is handled by the standard creature
/// SBA (CR 704.5f). X = 0 (empty / spell-only graveyard) is a legal no-op
/// (-0/-0).
///
/// Card shape comes from the embedded JSON (<c>terror-tide.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. No new engine mechanic required.
///
/// CR rule references: 109.5 (symmetric sweep), 110.4a / 300.1 (permanent
/// card definition), 117.5 (mana cost), 207.2c (ability word — no rules
/// meaning), 514.2 (EOT cleanup), 608.2 (lock X at resolution), 613.4
/// (continuous-effects layer 7c), 704.5f (toughness 0 creature-death SBA).
/// </summary>
[CardName("Terror Tide")]
public static class TerrorTideFactory
{
    public const string CardName = "Terror Tide";
    public const string Slug = "terror-tide";
    public const string PrintedManaCost = "{2}{B}{B}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Count Terror Tide's X — the number of permanent cards in the
    /// caster's graveyard (CR 110.4a / 300.1). Pure helper, exposed for
    /// tests; the live resolve calls it at resolution time (CR 608.2).
    /// </summary>
    public static int ComputeX(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return caster.Zones.Graveyard.GetCards()
            .Count(WrennAndSevenFactory.IsPermanentCard);
    }

    /// <summary>
    /// Build Terror Tide's resolve effect — compute X from the caster's
    /// graveyard (CR 608.2 — locked at resolution), then register a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per creature on
    /// every supplied player's battlefield (CR 109.5 — symmetric sweep).
    /// EOT cleanup is handled by the shared layer-system expiry (CR 514.2).
    /// </summary>
    /// <param name="caster">The player who cast Terror Tide; "your
    /// graveyard" is theirs.</param>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (off-oracle).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: all creatures -X/-X EOT (X = permanent cards in your graveyard)",
                () =>
                {
                    // CR 608.2 — lock X in as the effect resolves.
                    var x = ComputeX(caster);
                    if (x == 0)
                    {
                        // -0/-0 is a no-op; skip the registration churn.
                        return;
                    }

                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
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

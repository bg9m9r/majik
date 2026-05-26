using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Languish (Magic Origins, {2}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall):
///   "All creatures get -4/-4 until end of turn."
///
/// ## Implementation
///
/// Card shape at the dispatcher; the symmetric -4/-4 sweep is built on
/// demand via <see cref="BuildResolveEffect"/>.
///
/// Resolve registers a <see cref="PumpUntilEndOfTurnEffect"/>(c, -4, -4)
/// per <see cref="Creature"/> on every supplied player's battlefield
/// against the engine's per-creature continuous-effects service
/// (<see cref="Card.ActiveEffects"/>). Layer 7c modify with EOT expiry
/// (CR 613.4 / CR 514.2 — cleanup step ends the effect). Same shape
/// every -X/-X sweep uses (mirrors
/// <see cref="DecreeOfPainFactory.BuildCycleEffect"/> and the shared
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate"/>).
/// Sign-agnostic — the layer system handles toughness ≤ 0 SBA death via
/// the standard creature-death check (CR 704.5f).
///
/// ## Why a named factory (over the existing template)
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate"/>
/// already binds the printed oracle line by shape but scans only the
/// caster's view of the battlefield (same gap as the Pyroclasm /
/// Anger-of-the-Gods family — see those factories' notes). The named
/// factory takes <c>allPlayers</c> explicitly so the sweep is symmetric
/// across both battlefields (CR 109.5).
///
/// CR rule references: 109.5 (symmetric sweep), 117.5 (mana cost),
/// 514.2 (EOT cleanup), 613.4 (continuous-effects layer 7c), 704.5f
/// (toughness 0 creature-death SBA).
/// </summary>
[CardName("Languish")]
public static class LanguishFactory
{
    public const string CardName = "Languish";
    public const string PrintedManaCost = "{2}{B}{B}";
    public const int PumpAmount = -4;

    /// <summary>
    /// Build a Languish sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect (-4/-4 sweep) is built on
    /// demand via <see cref="BuildResolveEffect"/>.
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
    /// Build Languish's resolve effect — register a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(c, -4, -4) per creature on
    /// every supplied player's battlefield (CR 109.5 — symmetric sweep).
    /// EOT cleanup is handled by the shared layer-system expiry (CR
    /// 514.2).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (off-oracle).</param>
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
                                    new PumpUntilEndOfTurnEffect(
                                        c, PumpAmount, PumpAmount));
                            }
                        }
                    }
                }),
        };
    }
}

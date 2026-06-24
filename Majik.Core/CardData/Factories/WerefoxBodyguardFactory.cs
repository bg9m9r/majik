using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Werefox Bodyguard (Bloomburrow,
/// Creature — Elf Fox Knight {1}{W}{W} 2/2).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Flash
///    When this creature enters, exile up to one other target non-Fox creature
///    until this creature leaves the battlefield.
///    {1}{W}, Sacrifice this creature: You gain 2 life."
///
/// ## Shape source
/// The card is fully declarative JSON
/// (<c>Majik.Core/CardData/Cards/werefox-bodyguard.json</c>) — this factory only
/// loads + materialises it (same thin posture as
/// <see cref="HarbingerOfTheTidesFactory"/>). The JSON expresses:
///
/// - <b>Flash</b> (CR 702.8) — a printed keyword in the <c>keywords</c> array,
///   wired to a <see cref="Majik.Core.Abilities.KeywordAbility"/> marker by
///   <see cref="CardDefRuntime"/> at build time; it lets the spell be cast at
///   instant speed (CR 601.2b).
/// - <b>ETB exile-until-leaves</b> (CR 603.6a / 701.21) — the Banisher-Priest
///   shape shared with <see cref="BorrowedTimeFactory"/> / Banishing Light: an
///   <c>etb_self</c> trigger carrying the <c>exile_until_leaves</c> verb. The
///   printed riders map onto the verb's knobs:
///   <list type="bullet">
///     <item><c>"excludeSelf": true</c> — "other" (CR 608.2b): the resolution
///     re-check refuses Werefox Bodyguard itself.</item>
///     <item><c>"optional": true</c> — "up to one … target" (CR 115.1b): the
///     0..1 optional target slot, declinable.</item>
///     <item><c>"targetFilter": "non_fox_creature"</c> — the printed "non-Fox
///     creature" restriction (CR 205.3m): a battlefield creature without the
///     Fox subtype. The same predicate gates the CR 608.2b resolution re-check.
///     </item>
///   </list>
///   The verb attaches BOTH linked triggered abilities (ETB exile + LTB return)
///   to the card shape; the LTB return fires whenever Werefox Bodyguard leaves
///   the battlefield, returning the exiled creature under its owner's control
///   (CR 110.2).
/// - <b>{1}{W}, Sacrifice this creature: You gain 2 life</b> (CR 602.1 /
///   119.3) — an <c>activated</c> ability with a <c>mana</c> + <c>sacrifice_self</c>
///   cost and the <c>gain_life_self</c> effect (same posture as
///   <see cref="HeapedHarvestFactory"/>'s Food sacrifice ability).
/// </summary>
[CardName("Werefox Bodyguard")]
public static class WerefoxBodyguardFactory
{
    public const string CardName = "Werefox Bodyguard";
    public const string Slug = "werefox-bodyguard";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Werefox Bodyguard owned and controlled by
    /// <paramref name="owner"/>. Both triggered abilities (ETB exile + LTB
    /// return), the Flash keyword marker, and the sacrifice-for-life activated
    /// ability are attached to the card shape at build time.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }
}

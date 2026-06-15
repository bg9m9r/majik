using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Champion (Onslaught / many reprints —
/// Creature — Elf {1}{G}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Other Elf creatures get +1/+1 and have forestwalk. (They can't be
///    blocked as long as defending player controls a Forest.)"
///
/// A symmetric tribal anthem-plus-landwalk lord, structurally identical to
/// <see cref="LordOfAtlantisFactory"/> (Merfolk → Elf, Islandwalk →
/// Forestwalk). The base shape (name, Creature — Elf, {1}{G}{G}, 2/2) is
/// materialised from the embedded JSON definition (<c>elvish-champion.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the tribal anthem is layered on
/// top here because the JSON <c>AbilityDefinition</c> schema doesn't express a
/// tribal anthem (same posture as <see cref="ImperiousPerfectFactory"/>).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf, mana cost {1}{G}{G}, owner/controller wired (from JSON).
/// - <b>Static "Other Elf creatures get +1/+1 and have forestwalk"</b> wired via
///   <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Elf</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Forestwalk"]</c>, <c>includeSelf: false</c>,
///   <c>allPlayers: true</c>.
///   The printed text says "Other Elf creatures" with NO "you control"
///   qualifier, so the effect is symmetric (CR 109.5 doesn't scope it):
///   <c>allPlayers: true</c> pumps EVERY Elf on the battlefield, including an
///   opponent's. <c>includeSelf: false</c> honours the printed "Other".
///   Layer 7c for P/T (CR 613.7c), Layer 6 for the granted keyword (CR 613.1f).
///
/// ## Forestwalk (CR 702.14)
/// The "Forestwalk" string is added to each matching creature's keyword set.
/// Combat-validator enforcement of forestwalk ("can't be blocked as long as
/// the defending player controls a Forest") is deferred — same posture as
/// Islandwalk in <see cref="LordOfAtlantisFactory"/>. The keyword marker is
/// sufficient for the factory to ship.
///
/// ## Deferred (v1 gaps)
/// - Forestwalk combat-enforcement (CR 702.14b) — blocking restriction gate
///   not yet wired in the combat validator.
/// - LTB unregister — the registered <see cref="LordStaticEffect"/> stays on
///   the <see cref="ContinuousEffectsService"/> across zone changes; its
///   <see cref="ContinuousEffect.IsActive"/> check short-circuits when Elvish
///   Champion isn't on the battlefield so the bonus lifts correctly (same shape
///   as <see cref="LordOfAtlantisFactory"/> / <see cref="ImperiousPerfectFactory"/>).
/// </summary>
[CardName("Elvish Champion")]
public static class ElvishChampionFactory
{
    public const string CardName = "Elvish Champion";
    public const string Slug = "elvish-champion";

    /// <summary>
    /// Construct Elvish Champion without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effect is not
    /// registered (no layers service), so other Elves don't yet receive
    /// +1/+1 + Forestwalk. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Elvish Champion. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Forestwalk to all
    /// OTHER Elves on the battlefield (including opponents') is registered
    /// against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Forestwalk static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Elf,
        // {1}{G}{G}, 2/2).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords).
            // "Other Elf creatures get +1/+1 and have forestwalk." — note no
            // "you control" qualifier, so allPlayers: true applies the bonus
            // symmetrically to ALL Elves on the battlefield, including
            // opponents'. includeSelf: false honours "Other".
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Forestwalk" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: true));
        }

        return card;
    }
}

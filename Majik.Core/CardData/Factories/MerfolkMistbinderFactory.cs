using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Merfolk Mistbinder (Rivals of Ixalan, {G}{U}).
/// Creature — Merfolk Shaman 2/2. Oracle text (verified against Scryfall):
///   "Other Merfolk you control get +1/+1."
///
/// The card's base shape (name, Creature, Merfolk + Shaman subtypes, {G}{U},
/// 2/2) is materialised from the embedded JSON definition
/// (<c>merfolk-mistbinder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The lord anthem is layered on
/// top here — the JSON <c>AbilityDefinition</c> schema doesn't express lord
/// statics, so it lives in the factory (same posture as
/// <see cref="StormscaleScionFactory"/>'s Dragon anthem).
///
/// ## Implemented (v1)
/// - <b>Lord static (CR 613.7c / 613.1g)</b>: "Other Merfolk you control get
///   +1/+1." Wired via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Merfolk, power: 1, toughness: 1, includeSelf:
///   false, allPlayers: false</c> — controller-scoped (opponents' Merfolk
///   are unaffected); <c>includeSelf: false</c> honours the printed "Other".
///   Identical shape to <see cref="MasterOfThePearlTridentFactory"/>'s
///   Merfolk anthem, minus the Islandwalk grant. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="LordStaticEffect.IsActive"/> short-circuits when
///   the Mistbinder isn't on the battlefield so the bonus lifts correctly
///   (same posture as <see cref="StormscaleScionFactory"/>).
/// </summary>
[CardName("Merfolk Mistbinder")]
public static class MerfolkMistbinderFactory
{
    public const string CardName = "Merfolk Mistbinder";
    public const string Slug = "merfolk-mistbinder";

    /// <summary>
    /// Construct Merfolk Mistbinder without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord anthem is NOT
    /// registered (no layers service to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Merfolk Mistbinder. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to other Merfolk the
    /// controller controls is registered against the layers service.
    /// Opponents' Merfolk are NOT affected (allPlayers: false).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 anthem against. May be null — no live bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Merfolk + Shaman subtypes, {G}{U}, 2/2). The JSON carries no
        // abilities — the anthem is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1g (controller scope).
            // "Other Merfolk you control get +1/+1."
            // allPlayers: false → controller-scoped (only the controller's
            // own Merfolk benefit). includeSelf: false honours "Other".
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Merfolk,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}

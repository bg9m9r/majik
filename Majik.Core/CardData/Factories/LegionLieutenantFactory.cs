using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Legion Lieutenant (Rivals of Ixalan, {W}{B}).
/// Creature — Vampire Knight 2/2. Oracle text (verified against Scryfall):
///   "Other Vampires you control get +1/+1."
///
/// The card's base shape (name, type, Vampire + Knight subtypes, {W}{B},
/// 2/2) is materialised from the embedded JSON definition
/// (<c>legion-lieutenant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The lone printed behaviour
/// (the Vampire-lord anthem) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express lord statics, so it
/// lives in the factory (same posture as <see cref="StormscaleScionFactory"/>'s
/// Dragon anthem and <see cref="GoblinChieftainFactory"/>'s Goblin anthem).
///
/// ## Implemented (v1)
/// - <b>Lord static (CR 613.7c P/T + CR 613.1g controller scope)</b>:
///   "Other Vampires you control get +1/+1." Wired via
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: Vampire,
///   power: 1, toughness: 1, grantedKeywords: null, includeSelf: false,
///   opponentsOnly: false, allPlayers: false</c> — controller-scoped
///   (opponents' Vampires are unaffected, CR 109.5); <c>includeSelf:
///   false</c> honours the printed "Other". Legion Lieutenant is itself a
///   Vampire, so the "Other" rider (not the subtype gate) is what excludes
///   it from its own buff. Identical shape to
///   <see cref="StormscaleScionFactory"/>'s Dragon anthem. Registered only
///   when a <see cref="ContinuousEffectsService"/> is supplied.
///
/// Multiple copies stack: two Legion Lieutenants give Other Vampires +2/+2.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="LordStaticEffect.IsActive"/> short-circuits when
///   the Lieutenant isn't on the battlefield so the bonus lifts correctly
///   (same posture as <see cref="StormscaleScionFactory"/> /
///   <see cref="ElvishArchdruidFactory"/>).
/// </summary>
[CardName("Legion Lieutenant")]
public static class LegionLieutenantFactory
{
    public const string CardName = "Legion Lieutenant";
    public const string Slug = "legion-lieutenant";

    /// <summary>
    /// Construct Legion Lieutenant with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effect is not
    /// registered, so other Vampires you control don't yet receive +1/+1
    /// (there's no layers service to register the effect against). This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Legion Lieutenant. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to other Vampire
    /// creatures the controller controls is registered against the layers
    /// service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 anthem against. May be null — no live bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Vampire + Knight subtypes, {W}{B}, 2/2). The JSON carries no
        // abilities — the anthem is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Lord static — CR 613.7c (P/T) + CR 613.1g (controller scope).
        //   "Other Vampires you control get +1/+1."
        // allPlayers: false → controller-scoped (opponents' Vampires
        // unaffected, CR 109.5). includeSelf: false honours the printed
        // "Other" (the Lieutenant is itself a Vampire).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Vampire,
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

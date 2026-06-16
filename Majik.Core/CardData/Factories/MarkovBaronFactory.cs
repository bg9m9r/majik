using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Markov Baron (Duskmourn: House of Horror, {2}{B}).
/// Creature — Vampire Noble 2/2. Oracle text (verified against Scryfall):
///   "Convoke (Your creatures can help cast this spell. Each creature you tap
///    while casting this spell pays for {1} or one mana of that creature's
///    color.)
///    Lifelink
///    Other Vampires you control get +1/+1.
///    Madness {2}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// The base shape (name, Vampire + Noble subtypes, {2}{B}, 2/2, Lifelink) is
/// materialised from the embedded JSON definition (<c>markov-baron.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed riders are layered
/// on top here.
///
/// ## Implemented (v1)
/// - <b>Lifelink</b> (intrinsic keyword) — carried by the JSON
///   <c>keywords</c> list (CR 702.15), same as
///   <see cref="BloodthirstyConquerorFactory"/>'s Flying / Deathtouch.
/// - <b>Convoke keyword marker</b> (CR 702.51) — the same inline
///   <see cref="KeywordAbility"/>("Convoke") marker
///   <see cref="ConclaveTribunalFactory"/> / <see cref="ChordOfCallingFactory"/>
///   attach. The cast-time creature-tap prompt + per-tap pip reduction are
///   driven engine-side by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
///   marker is present (CR 702.51b) — no per-card cost wiring needed.
/// - <b>Lord static (CR 613.7c P/T + CR 613.1g controller scope)</b>:
///   "Other Vampires you control get +1/+1." Wired via
///   <see cref="LordStaticEffect"/> — identical shape to
///   <see cref="LegionLieutenantFactory"/>'s Vampire anthem
///   (<c>matchingSubtype: Vampire, +1/+1, includeSelf: false,
///   allPlayers: false</c> — controller-scoped, "Other" excludes the Baron
///   itself, CR 109.5). Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
/// - <b>Madness {2}{B}</b> (CR 702.35) — supported INTRINSICALLY via the
///   engine's <see cref="Majik.Core.Keywords.MadnessCatalog"/> (Markov Baron's
///   {2}{B} madness cost is already catalogued); no per-card wiring required.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/> stays
///   on the <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="LordStaticEffect.IsActive"/> short-circuits off the battlefield
///   so the bonus lifts correctly (same posture as
///   <see cref="LegionLieutenantFactory"/>).
/// </summary>
[CardName("Markov Baron")]
public static class MarkovBaronFactory
{
    public const string CardName = "Markov Baron";
    public const string Slug = "markov-baron";

    /// <summary>
    /// Construct Markov Baron with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord anthem is not
    /// registered (no layers service), but the Convoke marker and Lifelink
    /// keyword are present. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Markov Baron. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to other Vampire creatures
    /// the controller controls is registered against the layers service.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Vampire + Noble subtypes, {2}{B}, 2/2, Lifelink).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke keyword marker. Descriptive; the engine-side
        // cast-time creature-tap prompt + pip reduction key off this marker
        // in SpellCastFlow. Same inline attach pattern as Conclave Tribunal.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        // CR 613.7c (P/T) + CR 613.1g (controller scope) —
        //   "Other Vampires you control get +1/+1."
        // includeSelf: false honours the printed "Other" (the Baron is itself
        // a Vampire); allPlayers: false → controller-scoped (CR 109.5).
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

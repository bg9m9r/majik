using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anthem of Champions (Modern Horizons 3, {G}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creatures you control get +1/+1."
///
/// The base shape (name, single Enchantment card type, {G}{W}) is
/// materialised from the embedded JSON definition
/// (<c>anthem-of-champions.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HonorOfThePureFactory"/>. The anthem is layered on here because
/// the JSON schema doesn't express continuous static effects.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {G}{W}, owner / controller wiring.
/// - <b>Anthem (+1/+1)</b>: "Creatures you control get +1/+1." Registered as
///   a <see cref="ControllerCreatureAnthemEffect"/> static at Layer 7c
///   (CR 613.7c) with <c>requiredColor: null</c> — the colour-agnostic
///   "all creatures you control" (Glorious Anthem) shape, i.e. Honor of the
///   Pure without the colour gate. Scoped to the source's controller ("you
///   control"); opponents' creatures are unaffected (CR 109.5 — "you" =
///   controller). <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Anthem of Champions isn't on the battlefield so the bonus lifts on LTB
///   (CR 614). Anthem of Champions is an Enchantment (not a Creature), so the
///   includeSelf question is moot.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield, but a future Prune pass could drop the entry.
///   Same shape as Honor of the Pure / the Lord factories.
/// - <b>Control-change re-evaluation</b>: controller is read live from
///   <see cref="Permanent.Controller"/> on the source at AppliesTo time, so a
///   control change of Anthem of Champions is reflected lazily; same caveat
///   posture as the other anthem factories.
/// </summary>
[CardName("Anthem of Champions")]
public static class AnthemOfChampionsFactory
{
    public const string CardName = "Anthem of Champions";
    public const string Slug = "anthem-of-champions";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Anthem of Champions without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the anthem is not registered,
    /// so no creatures receive +1/+1.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Anthem of Champions. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="ControllerCreatureAnthemEffect"/> granting +1/+1 to every
    /// creature the controller controls is registered against the layers
    /// service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the anthem
    /// against. May be null — no live bonus.</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {G}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c — "Creatures you control get +1/+1." Layer 7c P/T
            // modification scoped to the controller's battlefield. No colour
            // gate (requiredColor: null) — the all-creatures Glorious Anthem
            // shape.
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: card,
                power: 1,
                toughness: 1,
                includeSelf: false,
                requiredColor: null));
        }

        return card;
    }
}

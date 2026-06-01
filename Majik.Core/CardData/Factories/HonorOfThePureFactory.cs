using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Honor of the Pure (Magic 2010 / Eldritch Moon,
/// {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "White creatures you control get +1/+1."
///
/// The base shape (name, single Enchantment card type, {1}{W}) is
/// materialised from the embedded JSON definition
/// (<c>honor-of-the-pure.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="SealOfFireFactory"/> / <see cref="RenegadeMapFactory"/>. The
/// colour-filtered anthem is layered on here because the JSON schema doesn't
/// express continuous static effects.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {1}{W}, owner / controller wiring.
/// - <b>Colour-filtered anthem (+1/+1)</b>: "White creatures you control get
///   +1/+1." Registered as a <see cref="ControllerCreatureAnthemEffect"/>
///   static at Layer 7c (CR 613.7c) with <c>requiredColor: White</c> — the
///   same effect Heartless Summoning / Glorious Anthem use, gated on the
///   creature's printed colour set (CR 105 / CR 202.2a) via
///   <see cref="Majik.Core.Cards.CardColors.GetColors"/>. Scoped to the
///   source's controller ("you control"); opponents' white creatures are
///   unaffected.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Honor of
///   the Pure isn't on the battlefield so the bonus lifts on LTB (CR 614).
///   Honor of the Pure is an Enchantment (not a Creature), so the
///   includeSelf question is moot.
///
/// ## Colour gate (CR 105 / CR 613.7c)
/// The anthem keys on the creature's printed colour (mana-cost pips +
/// printed colour indicator, CR 202.2) via
/// <see cref="Majik.Core.Cards.CardColors.GetColors"/>. A white token
/// (colour stamped by its creating effect) and a multicolour creature that
/// includes white both qualify; a mono-non-white creature does not.
/// <b>Deferred (v1 gap)</b>: a Layer-5 colour changer (a creature turned
/// white by another effect) is NOT reflected — the gate cannot consult
/// <see cref="Permanent.GetEffectiveColors"/> without re-entering the layer
/// service mid-evaluation (infinite recursion), so it reads printed colour.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield, but a future Prune pass could drop the
///   entry. Same shape as Heartless Summoning / the LordStaticEffect
///   factories.
/// - <b>Control-change re-evaluation</b>: controller is captured at
///   register time (via <see cref="Permanent.Controller"/> on the source).
///   Mind Control on Honor of the Pure won't currently flip the affected
///   side. Same caveat as Heartless Summoning.
/// </summary>
[CardName("Honor of the Pure")]
public static class HonorOfThePureFactory
{
    public const string CardName = "Honor of the Pure";
    public const string Slug = "honor-of-the-pure";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Honor of the Pure without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the anthem is not registered,
    /// so no creatures receive +1/+1.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Honor of the Pure. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="ControllerCreatureAnthemEffect"/> granting +1/+1 to WHITE
    /// creatures the controller controls is registered against the layers
    /// service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// colour-filtered anthem against. May be null — no live bonus.</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {1}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c — "White creatures you control get +1/+1." Layer 7c
            // P/T modification scoped to the controller's battlefield, gated
            // on effective white colour (CR 105). requiredColor: White is the
            // only difference from a plain Glorious Anthem.
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: card,
                power: 1,
                toughness: 1,
                includeSelf: false,
                requiredColor: ManaColor.White));
        }

        return card;
    }
}

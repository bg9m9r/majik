using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Intangible Virtue (Innistrad, {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creature tokens you control get +1/+1 and have vigilance."
///
/// The base shape (name, single Enchantment card type, {1}{W}) is
/// materialised from the embedded JSON definition
/// (<c>intangible-virtue.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HonorOfThePureFactory"/>. The token-filtered anthem +
/// keyword grant is layered on here because the JSON schema doesn't
/// express continuous static effects.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {1}{W}, owner / controller
///   wiring.
/// - <b>Token anthem (+1/+1) + vigilance</b>: "Creature tokens you control
///   get +1/+1 and have vigilance." Registered as a
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: null</c> (no
///   creature-type gate), <c>tokensOnly: true</c> (CR 111 — only token
///   creatures), and <c>grantedKeywords: ["Vigilance"]</c>. The +1/+1 is a
///   Layer 7c P/T modification (CR 613.7c); the granted keyword surfaces
///   through <see cref="CreatureCharacteristics.Keywords"/> at Compute time
///   and is read by <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>.
///   Scoped to the source's controller ("you control"); opponents' tokens
///   are unaffected. <see cref="ContinuousEffect.IsActive"/> short-circuits
///   when Intangible Virtue isn't on the battlefield so the bonus + keyword
///   lift on LTB (CR 614). Intangible Virtue is an Enchantment (not a
///   Creature), so the includeSelf question is moot.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield, but a future Prune pass could drop the
///   entry. Same shape as Honor of the Pure / the Lord factories.
/// - <b>Control-change re-evaluation</b>: controller is captured via
///   <see cref="Permanent.Controller"/> on the source at AppliesTo time, so
///   a control change of Intangible Virtue is reflected lazily; same caveat
///   posture as the other anthem factories.
/// </summary>
[CardName("Intangible Virtue")]
public static class IntangibleVirtueFactory
{
    public const string CardName = "Intangible Virtue";
    public const string Slug = "intangible-virtue";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Intangible Virtue without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the anthem is not registered,
    /// so no tokens receive +1/+1 or vigilance.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Intangible Virtue. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and vigilance to the
    /// controller's creature tokens is registered against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// token-filtered anthem against. May be null — no live bonus.</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {1}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keyword) — "Creature tokens
            // you control get +1/+1 and have vigilance." No subtype gate
            // (matchingSubtype: null), token-only (CR 111), scoped to the
            // source's controller. Vigilance is read back from
            // CreatureCharacteristics.Keywords by CombatAbilities.HasVigilance.
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: null,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Vigilance" },
                tokensOnly: true));
        }

        return card;
    }
}

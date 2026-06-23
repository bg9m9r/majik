using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worn Powerstone (Antiquities and reprints, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    {T}: Add {C}{C}."
///
/// The whole card — name, single Artifact card type, {3}, and the
/// <c>{T}: Add {C}{C}</c> <see cref="Majik.Core.Abilities.ManaAbility"/> —
/// is materialised from the embedded JSON definition
/// (<c>worn-powerstone.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Same posture as
/// <see cref="RenegadeMapFactory"/> / <see cref="AbradedBluffsFactory"/>:
/// the JSON schema already expresses a colourless ramp rock, so no
/// behaviour is layered on in code.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {3}, owner / controller wiring).
/// - <b>{T}: Add {C}{C}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1a — mana abilities don't use the stack). {C}{C} folds into
///   the generic bucket via <see cref="Majik.Core.Primitives.ManaCost.Parse"/>
///   (CR 107.4c) → two colourless. Hedron Archive's tap-for-{C}{C} body
///   (the {C}{C}{C} Hedron / Dreamstone cycle, one pip fewer).
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional
///   "This artifact enters tapped." Applied on the production load path by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle
///   text (this factory builds the artifact without it — matching the
///   Renegade Map / Refuge-cycle posture — so the replacement isn't
///   double-registered; the binder owns it).
/// </summary>
[CardName("Worn Powerstone")]
public static class WornPowerstoneFactory
{
    public const string CardName = "Worn Powerstone";
    public const string Slug = "worn-powerstone";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Worn Powerstone owned and controlled by
    /// <paramref name="owner"/>. The enters-tapped replacement (CR 614.1c)
    /// is owned by <see cref="Majik.Core.CardData.EntersTappedBinder"/> on
    /// the production load path, not here (shape-only build enters untapped,
    /// matching the Renegade Map / Refuge-cycle posture).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity (Artifact, {3}) + {T}: Add {C}{C} come from the JSON def.
        var stone = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        stone.SetOwner(owner);
        stone.SetController(owner);

        return stone;
    }
}

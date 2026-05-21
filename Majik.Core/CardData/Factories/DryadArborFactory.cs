using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dryad Arbor (Future Sight).
///
/// Dryad Arbor is a Land Creature — Forest Dryad 1/1.
/// It has no mana cost (CR 305.8 — "Summon Dryad Arbor as a creature with no mana cost").
/// The {T}: Add {G} ability comes from the Forest subtype (not oracle text), consistent
/// with how basic Forest lands generate mana.
///
/// ## Implementation strategy
/// Constructed as a <see cref="Creature"/> (to carry BasePower/BaseToughness),
/// then <see cref="CardType.Land"/> is added via <see cref="Card.AddCardType"/>
/// (the same multi-type seam used by Walking Ballista for Artifact Creature).
/// The green mana ability is wired directly because Dryad Arbor is not a Basic
/// land (HasSupertype(Basic) == false), so OracleManaBinder.BindBasicLandMana
/// would short-circuit. Instead we attach a ManaAbility for {G} explicitly —
/// which is equivalent to what the binder would do for a Basic Forest (CR 305.6).
///
/// ## Deferred
/// - Land-drop-per-turn enforcement (no land-play restriction yet).
/// - Summoning sickness (creatures that enter as lands vs. creatures — CR 302.6).
/// - Green Sun's Zenith interaction (can be fetched as a Forest creature — deferred
///   to the targeting / land-subtype search slice).
/// </summary>
public static class DryadArborFactory
{
    /// <summary>
    /// Construct a Dryad Arbor for the given owner.
    /// The returned <see cref="Creature"/> also carries <see cref="CardType.Land"/>
    /// (multi-type — CR 305.8 / 302.1) and a {T}: Add {G} mana ability.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Build as Creature to carry Power/Toughness; Land type added below.
        // No mana cost — CR 305.8 specifies Dryad Arbor has no mana cost.
        var arbor = new Creature(
            name: "Dryad Arbor",
            manaCost: "",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Forest, CardSubtype.Dryad });

        // Dryad Arbor is also a Land (CR 305.8).
        arbor.AddCardType(CardType.Land);

        arbor.SetOwner(owner);
        arbor.SetController(owner);

        // {T}: Add {G} — sourced from Forest subtype (CR 305.6 / basic land rule).
        // Wired directly rather than via OracleManaBinder.BindBasicLandMana because
        // Dryad Arbor has no Basic supertype and the binder gates on that check.
        arbor.AddAbility(new ManaAbility(arbor, owner, ManaCost.Parse("G")));

        return arbor;
    }
}

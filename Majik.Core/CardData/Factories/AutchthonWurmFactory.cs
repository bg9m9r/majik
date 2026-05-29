using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Autochthon Wurm (Ravnica: City of Guilds,
/// {10}{G}{G}{G}{W}{W}).
///
/// Creature — Wurm 9/14. Oracle text:
///   "Convoke (Your creatures can help cast this spell. Each creature you
///    tap while casting this spell pays for {1} or one mana of that
///    creature's color.)
///    Trample"
///
/// Autochthon Wurm has the highest printed mana value of any creature in
/// the Ravnica block (MV 15) and is a key target for Neobrand combo lines
/// that use Convoke to cast it for little or no mana by tapping a wide
/// board of token creatures.
///
/// ## Implemented (v1)
///
/// - 9/14 <see cref="Creature"/> — Wurm at {10}{G}{G}{G}{W}{W}
///   (MV 15). Green + White colour identity (CR 105.2 — a card is the
///   colour(s) of its mana-cost colour symbols).
/// - <b>Convoke</b> (CR 702.51): keyword marker wired as a
///   <see cref="KeywordAbility"/>. Cost reduction per CR 702.51b is
///   exercised through the shared
///   <see cref="Majik.Core.Costs.ConvokeAlternativeCost.ReduceCost"/>
///   pure-function reducer (same surface as
///   <see cref="ChordOfCallingFactory"/> / <see cref="ConclaveTribunalFactory"/>).
///   Per-cast creature-tap prompts in <c>SpellCastFlow</c> are deferred
///   — callers wire a <see cref="Majik.Core.Costs.ConvokeAdditionalCost"/>
///   for actual game play.
/// - <b>Trample</b> (CR 702.19): keyword marker wired as a
///   <see cref="KeywordAbility"/>; read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> in the
///   combat-damage assignment path (identical to
///   <see cref="YavimayaWurmFactory"/> / <see cref="FangrenHunterFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - Same Convoke-flow gaps documented on
///   <see cref="ChordOfCallingFactory"/>: the SpellCastFlow Convoke hook
///   is deferred; bot and player casting flows pre-build a
///   <see cref="Majik.Core.Costs.ConvokeAdditionalCost"/> with the chosen
///   creatures and thread it through
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> parameter.
/// </summary>
[CardName("Autochthon Wurm")]
public static class AutchthonWurmFactory
{
    public const string CardName = "Autochthon Wurm";
    public const string PrintedManaCost = "{10}{G}{G}{G}{W}{W}";
    public const int Power = 9;
    public const int Toughness = 14;

    /// <summary>
    /// Construct Autochthon Wurm owned and controlled by
    /// <paramref name="owner"/>. No runtime services are required — the
    /// card is a Convoke + Trample creature with no triggered or activated
    /// abilities.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Wurm });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke. Keyword marker consumed by bot-discovery
        // (ConvokeAltCostProbe) and the cast-flow additional-cost rail.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        // CR 702.19 — Trample. CombatAbilities.HasTrample reads the marker
        // during combat-damage assignment; excess damage is assigned to the
        // defending player / planeswalker once blockers are lethal.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}

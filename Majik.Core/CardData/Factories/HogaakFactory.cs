using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hogaak, Arisen Necropolis (Modern Horizons,
/// {B}{B}{G}{G}). Banned in Modern.
///
/// Legendary Creature — Avatar 8/8. Oracle text:
///   "This spell can't be cast unless you exile two creature cards from
///    your graveyard in addition to its other costs.
///    Convoke. (Your creatures can help cast this spell. Each creature
///    you tap while casting this spell pays for {1} or one mana of that
///    creature's color.)
///    Trample
///    Hogaak, Arisen Necropolis can't be cast from your hand.
///    Hogaak's mana value is 8."
///
/// ## Implemented (v1)
///
/// - 8/8 Legendary Creature — Avatar with printed mana cost
///   <c>{B}{B}{G}{G}</c> (mana value 4 from <see cref="ManaCost.Parse"/>;
///   the printed "Hogaak's mana value is 8" override is documented but
///   the engine has no name-keyed mana-value override yet — see
///   "Deferred" below).
/// - <see cref="CardSupertype.Legendary"/> stamped via the
///   <see cref="Creature"/> ctor; SBA 704.5j legend-rule handling is
///   inherited from the engine.
/// - Trample wired as a <see cref="KeywordAbility"/> marker (CR 702.19),
///   consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>.
/// - Convoke wired as a <see cref="KeywordAbility"/> marker (CR 702.51).
///   Cost surfaced via <see cref="BuildAlternativeCost"/> →
///   <see cref="ConvokeAlternativeCost"/>, same v1-lossy reduction
///   plumbing as Chord of Calling (printed cost returned unchanged
///   until <see cref="Services.SpellCastFlow"/> grows a Convoke-aware
///   reduction hook).
/// - <b>Additional cost — exile two creature cards from controller's
///   graveyard (CR 601.2f)</b>: surfaced via
///   <see cref="BuildExileTwoCreaturesAdditionalCost"/> →
///   <see cref="ExileCreaturesFromGraveyardAdditionalCost"/>. Picks
///   deterministically (first two creature cards in the controller's
///   graveyard) when no agent prompt for graveyard pick exists.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"This spell can't be cast unless …" gate</b>: layered as the
///   <see cref="ExileCreaturesFromGraveyardAdditionalCost.CanPay"/>
///   precondition rather than a cast-time speed restriction. The
///   engine consults <see cref="IAdditionalCost.CanPay"/> from
///   <see cref="Services.SpellCastFlow"/> (mirrors Cabal Therapy
///   flashback's sacrifice precondition).
/// - <b>"Can't be cast from your hand"</b>: no cast-zone restriction
///   primitive yet. Documented but unenforced — Hogaak still casts
///   from hand if a caller routes it there. Real wiring needs a
///   "legal cast zones" predicate on <see cref="SpellDefinition"/>;
///   when implemented, Hogaak's allowed cast zones become
///   {Graveyard, Library, Exile, …} via Sneak Attack / Show and Tell
///   entry (CR 117.6 — alt-zone casts).
/// - <b>"Hogaak's mana value is 8"</b>: requires a name-keyed
///   characteristic override applied at every layer-7 query for mana
///   value. Engine has no such override surface yet. v1 reports
///   <c>ManaCost.TotalValue == 4</c> (B+B+G+G); same scope decision
///   as Death's Shadow's printed 13/13 baseline.
/// - <b>Convoke reduction integration</b>: same gap as Chord of
///   Calling — <see cref="ConvokeAlternativeCost.ReduceCost"/> is
///   exercised in isolation, not yet consumed by
///   <see cref="Services.SpellCastFlow"/>.
///
/// CR rule references: 205.4a (Legendary supertype), 205.3m (Avatar
/// subtype), 601.2f (additional costs), 702.19 (Trample), 702.51
/// (Convoke), 117.6 (casting cards from non-hand zones).
/// </summary>
[CardName("Hogaak, Arisen Necropolis")]
public static class HogaakFactory
{
    public const string CardName = "Hogaak, Arisen Necropolis";
    public const string PrintedManaCost = "{B}{B}{G}{G}";

    /// <summary>
    /// Construct Hogaak owned and controlled by <paramref name="owner"/>.
    /// Wires Trample + Convoke keyword markers. The additional cost
    /// (exile two creature cards from graveyard) is exposed separately
    /// via <see cref="BuildExileTwoCreaturesAdditionalCost"/> so callers
    /// can compose it into the cast flow.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 8,
            toughness: 8,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Avatar });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample keyword marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 702.51 — Convoke keyword marker. Cost machinery lives on
        // BuildAlternativeCost (returns ConvokeAlternativeCost).
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="ConvokeAlternativeCost"/> the cast flow
    /// consults to surface Convoke on this card. v1 returns the printed
    /// cost unchanged — see <see cref="ConvokeAlternativeCost"/> for the
    /// open reduction-hook work. Identical wiring to Chord of Calling.
    /// </summary>
    public static ConvokeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(PrintedManaCost));

    /// <summary>
    /// Build the printed additional cost (CR 601.2f) — exile two
    /// creature cards from the caster's graveyard. Returned as an
    /// <see cref="IAdditionalCost"/> so <see cref="Services.SpellCastFlow"/>
    /// can compose it into the cast pipeline alongside Convoke.
    /// </summary>
    public static ExileCreaturesFromGraveyardAdditionalCost
        BuildExileTwoCreaturesAdditionalCost() => new(count: 2);
}

/// <summary>
/// "As an additional cost to cast this spell, exile N creature cards
/// from your graveyard." (CR 601.2f.) Generic shape so other
/// graveyard-exile additional costs (Bridgevine, Vengevine cycle,
/// future Cube experiments) can reuse it.
///
/// <see cref="Exiled"/> captures the cards exiled by <see cref="Pay"/>
/// so downstream effects that reference "the exiled creatures" can read
/// them (Hogaak doesn't, but the shape parallels
/// <see cref="SacrificeCreatureCost.Sacrificed"/>).
/// </summary>
public sealed class ExileCreaturesFromGraveyardAdditionalCost : IAdditionalCost
{
    private readonly int _count;
    private readonly List<ICard> _exiled = new();

    public IReadOnlyList<ICard> Exiled => _exiled.AsReadOnly();

    public ExileCreaturesFromGraveyardAdditionalCost(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    public string Description => $"exile {_count} creature cards from your graveyard";

    /// <summary>
    /// CR 601.2f — legality is checked at cast-announcement time. True
    /// when the caster's graveyard contains at least <c>_count</c>
    /// creature cards.
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Graveyard.GetCards()
            .Count(c => c.HasType(CardType.Creature)) >= _count;
    }

    /// <summary>
    /// CR 601.2f payment — picks the first N creature cards from the
    /// caster's graveyard (deterministic v1, no agent prompt yet) and
    /// moves them Graveyard → Exile via raw zone mutation (parallels
    /// <see cref="ScavengingOozeFactory"/>'s exile path).
    /// </summary>
    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;

        var picks = caster.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Creature))
            .Take(_count)
            .ToList();

        foreach (var pick in picks)
        {
            caster.Zones.Graveyard.RemoveCard(pick);
            caster.Zones.Exile.AddCard(pick);
            pick.SetZone(ZoneType.Exile);
            _exiled.Add(pick);
        }

        return true;
    }
}

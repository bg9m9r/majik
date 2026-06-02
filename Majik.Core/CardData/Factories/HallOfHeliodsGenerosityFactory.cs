using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hall of Heliod's Generosity (Theros Beyond Death;
/// reprinted in Duskmourn: House of Horror Commander).
///
/// Legendary Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}{W}, {T}: Put target enchantment card from your graveyard on top of
///    your library."
///
/// ## Why it gets its own factory
/// Same skeleton as <see cref="MysticSanctuaryFactory"/> /
/// <see cref="WitchsCottageFactory"/> (utility land that recurs a typed card
/// from the graveyard to the top of the library), but the recursion here is an
/// ACTIVATED ability rather than an ETB / enters-untapped trigger. The
/// cost+tap shape mirrors <see cref="RoadsideReliquaryFactory"/>'s
/// "{cost}, {T}:" activated ability. No new engine mechanic is required —
/// activated abilities already carry <see cref="TargetRequest"/>s
/// (CR 602.2b) and the graveyard → top-of-library move is the same
/// <see cref="IZone.InsertCardAt"/>(0) primitive used by Mystic Sanctuary /
/// Mystical Tutor.
///
/// ## Implemented (v1)
/// - <b>Legendary Land</b> identity from the embedded JSON definition
///   (<c>hall-of-heliods-generosity.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>, plus owner / controller
///   wiring. The Legendary supertype drives the legend rule (CR 704.5j) via
///   the engine's SBAs — no special-casing here.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, doesn't use the stack), declared in the JSON. {C} folds to one
///   generic/colourless via the mana-ability binder.
/// - <b>{1}{W}, {T}: Put target enchantment card from your graveyard on top of
///   your library.</b> — an <see cref="ActivatedAbility"/> (CR 602) with two
///   costs: <see cref="ManaCostCost"/>("{1}{W}") for the mana pips and
///   <see cref="AdditionalCost.Tap"/> on the land. A 1..1
///   <see cref="TargetRequest"/> declares the "enchantment card in your
///   graveyard" target slot. On resolution the chosen card is moved
///   Graveyard → top of Library via <see cref="IZone.InsertCardAt"/>(0).
///   CR 608.2b illegal-on-resolution rechecks gate out cards no longer in the
///   graveyard, not owned by the controller, or no longer enchantment cards.
///
/// ## Rules notes
/// - "your graveyard" / "your library" = the ability's controller's zones
///   (CR 109.5). The target is restricted to enchantment cards (CR 109.3 —
///   the card's types apply in the graveyard).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c> (mirrors Mystic Sanctuary /
///   Witch's Cottage). The resolution guard enforces the enchantment +
///   graveyard + owner checks per CR 608.2b.
/// </summary>
[CardName("Hall of Heliod's Generosity")]
public static class HallOfHeliodsGenerosityFactory
{
    public const string CardName = "Hall of Heliod's Generosity";
    public const string Slug = "hall-of-heliods-generosity";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Hall of Heliod's Generosity owned and controlled by
    /// <paramref name="owner"/>. The {T}: Add {C} mana ability comes from the
    /// embedded JSON; the "{1}{W}, {T}: Put target enchantment card from your
    /// graveyard on top of your library" activated ability is layered on
    /// structurally.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {1}{W}, {T}: Put target enchantment card from your graveyard on top
        // of your library. CR 602 — activated ability with two costs (mana
        // pips + tap). CR 608.2b illegal-on-resolution rechecks below.
        // ----------------------------------------------------------------
        ActivatedAbility? recur = null;
        var recurEffect = new Effect(
            $"{CardName}: put target enchantment card from graveyard on top of library",
            () =>
            {
                if (recur is null) return;
                if (recur.ChosenTargets.Count == 0) return;
                if (recur.ChosenTargets[0].Count == 0) return;
                if (recur.ChosenTargets[0][0] is not Card target) return;

                // "your" graveyard / library = the ability's controller's
                // zones (CR 109.5).
                var controller = land.Controller ?? owner;

                // CR 608.2b — illegal-on-resolution rechecks.
                if (target.Zone != ZoneType.Graveyard) return;
                if (target.Owner is null || !ReferenceEquals(target.Owner, controller)) return;
                if (!target.HasType(CardType.Enchantment)) return;

                controller.Zones.Graveyard.RemoveCard(target);
                controller.Zones.Library.InsertCardAt(0, target);
                target.SetZone(ZoneType.Library);
            });

        recur = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{W}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { recurEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target enchantment card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(recur);

        return land;
    }
}

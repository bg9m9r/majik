using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShiftingWoodlandFactory"/>.
///
/// Oracle (Scryfall-confirmed, Modern Horizons 3):
///   "This land enters tapped unless you control a Forest.
///    {T}: Add {G}.
///    Delirium — {2}{G}{G}: This land becomes a copy of target permanent card
///    in your graveyard until end of turn. Activate only if there are four or
///    more card types among cards in your graveyard."
///
/// Covers:
/// - Identity: Land, name, non-basic, non-legendary, not a Forest.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {G} vanilla mana ability (from JSON).
/// - ETB tapped-unless-Forest predicate (via <see cref="ReplacementBus"/>).
/// - Delirium copy ability: cost {2}{G}{G}, 1..1 graveyard-permanent target,
///   delirium gate, in-place "becomes a copy until EOT" continuous effect.
/// </summary>
[Trait("Color", "C")]
public class ShiftingWoodlandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // Place Shifting Woodland (with services) on Alice's battlefield.
    private (Land land, ContinuousEffectsService effects) PlaceWithEffects()
    {
        var effects = new ContinuousEffectsService();
        var land = ShiftingWoodlandFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return (land, effects);
    }

    private static Land AddForest(Player controller)
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            { Owner = controller, Controller = controller };
        forest.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(forest);
        return forest;
    }

    // Add a permanent CARD to a player's graveyard.
    private static T AddToGraveyard<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    // Fill the controller's graveyard with 4+ distinct card types so delirium
    // is satisfied (CR 702.105): creature, artifact, enchantment, land.
    private void SatisfyDelirium(Player owner)
    {
        AddToGraveyard(owner, new Creature("Bear", "{1}{G}", 2, 2));
        AddToGraveyard(owner, new Artifact("Trinket", "{1}"));
        AddToGraveyard(owner, new Enchantment("Aura", "{1}{W}"));
        AddToGraveyard(owner, new Land("Wastes"));
    }

    private static ActivatedAbility CopyAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single();

    private static ManaAbility GreenManaAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedShiftingWoodland()
    {
        var land = ShiftingWoodlandFactory.Create(_alice);
        land.Name.Should().Be("Shifting Woodland");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary_NotForest()
    {
        var land = ShiftingWoodlandFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Shifting Woodland is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("not legendary");
        land.HasSubtype(CardSubtype.Forest).Should().BeFalse(
            "Shifting Woodland has no Forest subtype and cannot satisfy its own ETB predicate");
    }
    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasOneManaAbility_AndOneCopyActivatedAbility()
    {
        var land = ShiftingWoodlandFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {G}");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}{G}{G} copy ability is a stack-using activated ability, not a mana ability");
    }

    [Fact]
    public void GreenManaAbility_Activate_ProducesOneGreen_AndTaps()
    {
        var (land, _) = PlaceWithEffects();
        var mana = (IManaAbility)GreenManaAbility(land);

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Green.Should().Be(1, "{T}: Add {G} produces one green");
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void CopyAbility_HasManaCostAndGraveyardTargetRequest()
    {
        var land = ShiftingWoodlandFactory.Create(_alice);
        var ability = CopyAbility(land);

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Contain("G");
        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("graveyard");
    }

    // -----------------------------------------------------------------------
    // ETB tapped unless you control a Forest (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = ShiftingWoodlandFactory.Create(alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield, Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("no Forest controlled");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasAForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        AddForest(alice);
        var land = ShiftingWoodlandFactory.Create(alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield, Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("controller has a Forest");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddForest(bob);
        var land = ShiftingWoodlandFactory.Create(alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield, Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("the 'you control' predicate checks the controller only");
    }

    // -----------------------------------------------------------------------
    // Delirium gate (CR 702.105 / 602.5b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Delirium_NotActive_WithFewerThanFourCardTypes()
    {
        AddToGraveyard(_alice, new Creature("Bear", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Artifact("Trinket", "{1}"));
        UnholyHeatFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "only two card types in the graveyard");
    }

    [Fact]
    public void Delirium_Active_WithFourCardTypes()
    {
        SatisfyDelirium(_alice);
        UnholyHeatFactory.IsDeliriumActive(_alice).Should().BeTrue(
            "creature + artifact + enchantment + land = four card types");
    }

    [Fact]
    public void CopyAbility_CanActivateNow_GatedByDelirium()
    {
        var land = ShiftingWoodlandFactory.Create(_alice);
        var ability = CopyAbility(land);

        // CR 602.5c — the engine "Activate only if" gate is wired to the
        // delirium count, re-evaluated live.
        ability.CanActivateNow().Should().BeFalse("empty graveyard — no delirium");

        SatisfyDelirium(_alice);
        ability.CanActivateNow().Should().BeTrue("four card types now in the graveyard");
    }

    // -----------------------------------------------------------------------
    // Graveyard target candidates (CR 110.4a — permanent cards only)
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveyardPermanentCards_ExcludesInstantsAndSorceries()
    {
        var bear = AddToGraveyard(_alice, new Creature("Bear", "{1}{G}", 2, 2));
        var artifact = AddToGraveyard(_alice, new Artifact("Trinket", "{1}"));
        AddToGraveyard(_alice, new Instant("Bolt", "{R}"));
        AddToGraveyard(_alice, new Sorcery("Divination", "{2}{U}"));

        var candidates = ShiftingWoodlandFactory.GraveyardPermanentCards(_alice);

        candidates.Should().HaveCount(2);
        candidates.Should().Contain(bear).And.Contain(artifact);
    }

    // -----------------------------------------------------------------------
    // Copy resolution — becomes a copy of the chosen graveyard permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void CopyAbility_Resolve_BecomesCopyOfTargetArtifact_WhenDelirium()
    {
        var (land, effects) = PlaceWithEffects();
        SatisfyDelirium(_alice);
        var artifact = AddToGraveyard(_alice,
            new Artifact("Bonesplitter", "{1}", subtypes: new[] { CardSubtype.Equipment }));

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Subtypes.Should().Contain(CardSubtype.Equipment);
        // CR 707.2 — copiable values overwrite: the land is no longer a Land.
        chars.Types.Should().NotContain(CardType.Land);
    }

    [Fact]
    public void CopyAbility_Resolve_CopyExpiresAtEndOfTurn()
    {
        var (land, effects) = PlaceWithEffects();
        SatisfyDelirium(_alice);
        var bear = AddToGraveyard(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2));

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { bear } });
        ability.Resolve();

        effects.Compute(land).Types.Should().Contain(CardType.Creature, "now a copy of the Bears");

        // CR 514.2 — cleanup step lifts the "until end of turn" copy.
        effects.ExpireEndOfTurn();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "the copy expired; it is a Land again");
        chars.Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void CopyAbility_Resolve_NoOp_WhenDeliriumNotActive()
    {
        var (land, effects) = PlaceWithEffects();
        // Only ONE card type in the graveyard — delirium not active.
        var bear = AddToGraveyard(_alice, new Creature("Bear", "{1}{G}", 2, 2));

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { bear } });
        ability.Resolve();

        // Delirium gate fails closed — no copy effect registered.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature,
            "delirium not satisfied — the copy half is skipped (cost already paid)");
    }

    [Fact]
    public void CopyAbility_Resolve_NoOp_WhenTargetNoLongerInGraveyard()
    {
        var (land, effects) = PlaceWithEffects();
        SatisfyDelirium(_alice);
        var bear = AddToGraveyard(_alice, new Creature("Bear2", "{1}{G}", 2, 2));

        // Target leaves the graveyard before resolution (CR 608.2b illegal target).
        _alice.Zones.Graveyard.RemoveCard(bear);
        bear.SetZone(ZoneType.Exile);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { bear } });
        ability.Resolve();

        effects.Compute(land).Types.Should().Contain(CardType.Land,
            "the target left the graveyard — copy does nothing");
        effects.Compute(land).Types.Should().NotContain(CardType.Creature);
    }
}

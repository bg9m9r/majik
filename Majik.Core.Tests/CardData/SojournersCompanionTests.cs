using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SojournersCompanionFactory"/>.
///
/// Card: Sojourner's Companion — Artifact Creature — Thopter Knight {6} 4/4
/// (Modern Horizons 2).
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    {2}, {T}, Sacrifice Sojourner's Companion: Search your library for a
///    basic land card, put it onto the battlefield tapped, then shuffle."
///
/// Covers:
///   - Identity (name, dual types Artifact + Creature, subtypes Thopter +
///     Knight, mana cost {6}, 4/4, owner/controller).
///   - NamedCardFactory dispatch returns a Creature with the Affinity
///     cost reducer + Affinity keyword marker.
///   - Affinity for artifacts (CR 702.40) — generic reduction; floor-at-
///     zero (CR 117.7c).
///   - Activated ability shape: ManaCostCost {2} + AdditionalCost.Tap
///     (self) + AdditionalCost.Sacrifice (self).
///   - Resolve: sacrifices self, fetches a basic land onto battlefield
///     TAPPED, shuffles.
///   - Resolve refuses nonbasic lands (Urza's Mine stays in library).
///   - Resolve no-ops past sac+shuffle when no basic land is in the library.
/// </summary>
public class SojournersCompanionTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        owner.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        owner.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    private static ActivatedAbility GetTutorAbility(Creature card)
        => card.Abilities.OfType<ActivatedAbility>().Single();

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void SojournersCompanion_Identity()
    {
        var c = SojournersCompanionFactory.Create(_alice);

        c.Name.Should().Be("Sojourner's Companion");
        c.ManaCost.Should().Be("{6}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("Sojourner's Companion is an Artifact Creature (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SojournersCompanion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sojourner's Companion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sojourner's Companion");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the Affinity-for-artifacts cost reducer is attached");
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Affinity",
                "the Affinity keyword marker is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}, {T}, Sac tutor activated ability is attached");
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifacts_FullSix()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(companion);
        companion.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(companion, _alice);
        effective.Generic.Should().Be(6);
        effective.TotalValue.Should().Be(6);
    }

    [Fact]
    public void Affinity_ThreeArtifacts_GenericThree()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(companion);
        companion.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(companion, _alice);
        effective.Generic.Should().Be(3, "{6} reduced by 3 → {3}");
    }

    [Fact]
    public void Affinity_SixArtifacts_FreeCast()
    {
        // Headline dream: six artifacts → cast Sojourner's Companion free.
        var companion = SojournersCompanionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(companion);
        companion.SetZone(ZoneType.Hand);

        for (var i = 0; i < 6; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(companion, _alice);
        effective.Generic.Should().Be(0, "{6} reduced by 6 → {0} (free)");
        effective.TotalValue.Should().Be(0);
    }

    [Fact]
    public void Affinity_EightArtifacts_FloorAtZero()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(companion);
        companion.SetZone(ZoneType.Hand);

        for (var i = 0; i < 8; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{0}"));
        }

        var effective = CostReduction.GetEffectiveCost(companion, _alice);
        effective.Generic.Should().Be(0, "floor-at-zero (CR 117.7c) — never negative");
    }

    // -------------------------------------------------------------------------
    // {2}, {T}, Sacrifice ~: Tutor a basic land -> battlefield tapped
    // -------------------------------------------------------------------------

    [Fact]
    public void TutorAbility_HasMana2_Tap_AndSacrificeCosts()
    {
        var companion = SojournersCompanionFactory.Create(_alice);

        var tutor = GetTutorAbility(companion);

        var mana = tutor.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Generic.Should().Be(2, "printed {2}");
        mana.Cost.TotalValue.Should().Be(2, "no coloured pips in the activation cost");

        // Tap (self) + Sacrifice (self) additional costs are present (one each).
        tutor.Costs.OfType<AdditionalCost>().Should().HaveCount(2,
            "the activation pays Tap (self) + Sacrifice (self) as additional costs");
    }

    [Fact]
    public void Resolve_BasicLand_EntersBattlefieldTapped_AndCompanionGoesToGraveyard()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        PutOnBattlefield(_alice, companion);

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        var tutor = GetTutorAbility(companion);
        foreach (var fx in tutor.Effects) fx.Execute();

        // Companion sacrificed → graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(companion);
        companion.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(companion);

        // Basic land moved Library → Battlefield, tapped.
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        forest.IsTapped.Should().BeTrue(
            "Sojourner's Companion puts the tutored land onto the battlefield tapped");
    }

    [Fact]
    public void Resolve_DoesNotPickNonbasicLand()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        PutOnBattlefield(_alice, companion);

        // Nonbasic added first so a buggy "any land" predicate would pick it.
        var mine = MakeNonbasicLand("Urza's Mine", _alice);
        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        var tutor = GetTutorAbility(companion);
        foreach (var fx in tutor.Effects) fx.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mine);
        _alice.Zones.Library.GetCards().Should().Contain(mine);
        forest.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NoBasicLandInLibrary_SacsCompanionAndIsOtherwiseNoOp()
    {
        var companion = SojournersCompanionFactory.Create(_alice);
        PutOnBattlefield(_alice, companion);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _alice.Zones.Library.AddCard(bears);
        bears.SetZone(ZoneType.Library);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        var tutor = GetTutorAbility(companion);
        foreach (var fx in tutor.Effects) fx.Execute();

        // Sac still happens; library unchanged.
        _alice.Zones.Graveyard.GetCards().Should().Contain(companion);
        _alice.Zones.Library.GetCards().Should().HaveCount(1)
            .And.Contain(bears);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bears);
    }
}

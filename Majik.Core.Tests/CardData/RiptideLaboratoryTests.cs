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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RiptideLaboratoryFactory"/> — a Land with
/// {T}: Add {C} and {1}{U}, {T}: Return target Wizard you control to its
/// owner's hand (Onslaught).
///
/// Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {1}{U}, {T}: Return target Wizard you control to its owner's hand."
///
/// Covers:
/// - Card identity (Land, name) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} colorless mana ability.
/// - {1}{U}, {T} activated ability cost composition (ManaCostCost + tap-self).
/// - Bounce a Wizard you control → owner's hand.
/// - The candidate gatherer offers only Wizards you control (CR 205.3m / 109.5):
///   a non-Wizard you control, and an opponent's Wizard, are not legal targets.
/// - No target chosen → CR 608.2b fizzle (clean no-op, no throw).
/// </summary>
public class RiptideLaboratoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Wizard(string name, Player controller)
    {
        var c = new Creature(
            name: name,
            manaCost: "{1}{U}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Wizard });
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RiptideLaboratory_IsLand()
    {
        var land = RiptideLaboratoryFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("Riptide Laboratory is not legendary");
        land.Name.Should().Be("Riptide Laboratory");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RiptideLaboratory()
    {
        var card = NamedCardFactory.Create("Riptide Laboratory", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Riptide Laboratory");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void RiptideLaboratory_HasColorlessManaAbility_ProducingC()
    {
        var land = RiptideLaboratoryFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} is modelled on ManaCost as Generic (the engine has no separate
        // Colorless field) — same posture as Bonders' Enclave's {T}: Add {C}.
        produced.Generic.Should().Be(1, "Riptide Laboratory taps for exactly one {C}");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0, "no blue component");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}{U}, {T}: Return target Wizard you control to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void RiptideLaboratory_BounceAbility_HasManaCost1U_AndTapSelf()
    {
        var land = RiptideLaboratoryFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        var manaCost = activated.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Blue.Should().Be(1, "the {U} component");
        manaCost.Generic.Should().Be(1, "the {1} component");

        activated.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle("the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void RiptideLaboratory_BounceAbility_DeclaresSingleWizardTargetRequest()
    {
        var land = RiptideLaboratoryFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("Wizard you control");
    }

    [Fact]
    public void RiptideLaboratory_Bounce_OwnWizard_ReturnsToOwnersHand()
    {
        var wizard = Wizard("Snapcaster Mage", _alice);

        var land = RiptideLaboratoryFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { wizard },
        });

        activated.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(wizard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wizard);
        wizard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void RiptideLaboratory_CandidateGatherer_OffersOnlyWizardsYouControl()
    {
        // CR 205.3m / 109.5 — only a Wizard the resolving player controls is a
        // legal target. A non-Wizard you control, and an opponent's Wizard, are
        // both excluded by the candidate gatherer.
        var myWizard = Wizard("Snapcaster Mage", _alice);

        var myBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        myBear.SetOwner(_alice);
        myBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(myBear);
        myBear.SetZone(ZoneType.Battlefield);

        var oppWizard = Wizard("Merfolk Looter", _bob);

        var land = RiptideLaboratoryFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        var request = activated.TargetRequests.Should().ContainSingle().Subject;

        var ctx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(myWizard, "a Wizard you control is a legal target");
        candidates.Should().NotContain(myBear, "a non-Wizard you control is not a legal target (CR 205.3m)");
        candidates.Should().NotContain(oppWizard, "an opponent's Wizard is not a Wizard you control (CR 109.5)");
    }

    [Fact]
    public void RiptideLaboratory_Bounce_NoTargetChosen_ResolvesWithoutThrowing()
    {
        var land = RiptideLaboratoryFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        // No ChosenTargets set → CR 608.2b fizzle (clean no-op, no throw).
        var act = () => activated.Resolve();

        act.Should().NotThrow("an unfilled target fizzles cleanly");
    }
}

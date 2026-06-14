using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unique-behaviour tests for Flare of Cultivation (MH3, {1}{G}{G}, Sorcery).
///
/// Oracle text:
///   "You may sacrifice a nontoken green creature rather than pay this spell's
///    mana cost.
///    Search your library for up to two basic land cards, reveal those cards,
///    put one onto the battlefield tapped and the other into your hand, then
///    shuffle."
///
/// Exercises only what is unique to this card:
///   * Identity: Sorcery + green + MV 3 (single _Identity assert).
///   * Alternative cost (<see cref="SacrificeNontokenGreenCreatureAlternativeCost"/>):
///       - nontoken green creature controlled by caster IS legal,
///       - token / non-green / opponent-controlled are NOT legal,
///       - on resolve the chosen creature moves battlefield → graveyard.
///   * Search resolve (shared Cultivate body): two basics → one to
///     battlefield tapped, one to hand.
///   * Bot probe surfaces eligible nontoken green creatures only.
///
/// Dispatch + well-formedness is covered automatically by
/// <see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>.
/// </summary>
[Trait("Color", "G")]
public class FlareOfCultivationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_Identity_Sorcery_Green_ManaValue3()
    {
        var card = FlareOfCultivationFactory.Create(_alice);

        card.Name.Should().Be("Flare of Cultivation");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}{G}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(3);
    }

    // ── Alternative cost — CanCastFor ────────────────────────────────────────

    [Fact]
    public void AltCost_CanCastFor_NontokenGreenCreature_ControlledByCaster_IsLegal()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        var bear = MakeGreenCreature("Grizzly Bears", _alice, isToken: false);

        var altCost = new SacrificeNontokenGreenCreatureAlternativeCost(bear);

        altCost.CanCastFor(card, _alice).Should().BeTrue();
    }

    [Fact]
    public void AltCost_CanCastFor_TokenGreenCreature_IsIllegal()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        var saproling = MakeGreenCreature("Saproling", _alice, isToken: true);

        var altCost = new SacrificeNontokenGreenCreatureAlternativeCost(saproling);

        altCost.CanCastFor(card, _alice).Should().BeFalse(
            because: "tokens are excluded per oracle text");
    }

    [Fact]
    public void AltCost_CanCastFor_NontokenRedCreature_IsIllegal()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        var goblin = MakeRedCreature("Goblin", _alice);

        var altCost = new SacrificeNontokenGreenCreatureAlternativeCost(goblin);

        altCost.CanCastFor(card, _alice).Should().BeFalse(
            because: "the creature must be green");
    }

    [Fact]
    public void AltCost_CanCastFor_GreenCreatureControlledByOpponent_IsIllegal()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        // Bob controls this creature — Alice is the caster.
        var bobBear = MakeGreenCreature("Wild Bear", _bob, isToken: false);

        var altCost = new SacrificeNontokenGreenCreatureAlternativeCost(bobBear);

        altCost.CanCastFor(card, _alice).Should().BeFalse(
            because: "the sacrificed creature must be controlled by the caster");
    }

    // ── Alternative cost — resolve (sacrifice path) ──────────────────────────

    [Fact]
    public void AltCost_OnResolved_SacrificesCreature_BattlefieldToGraveyard()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        var bear = MakeGreenCreature("Grizzly Bears", _alice, isToken: false);

        var altCost = new SacrificeNontokenGreenCreatureAlternativeCost(bear);
        altCost.OnResolved(card, _alice);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // ── Search resolve (shared Cultivate body) ───────────────────────────────

    [Fact]
    public void Resolve_TwoBasicsAvailable_OneToBattlefieldTappedOneToHand()
    {
        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        var mountain = MakeBasicLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(mountain);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(FlareOfCultivationFactory.BuildSpellDefinition(_alice));

        // First pick → battlefield tapped.
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        var placed = _alice.Zones.Battlefield.GetCards().First() as Permanent;
        placed.Should().NotBeNull();
        placed!.IsTapped.Should().BeTrue(
            "Flare of Cultivation puts the first basic onto the battlefield tapped");

        // Second pick → hand.
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Mountain");

        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Bot probe ────────────────────────────────────────────────────────────

    [Fact]
    public void BotProbe_YieldsNontokenGreenCandidates_SkipsTokensAndNonGreen()
    {
        var card = FlareOfCultivationFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bear = MakeGreenCreature("Bear", _alice, isToken: false);   // eligible
        MakeGreenCreature("Saproling", _alice, isToken: true);          // token — skip
        MakeRedCreature("Goblin", _alice);                              // red — skip

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, stack);

        var probe = new FlareOfCultivationAltCostProbe();
        var candidates = probe.CandidatesFor(card, _alice, ctx).ToList();

        candidates.Should().HaveCount(1);
        var picked = candidates[0].Should()
            .BeOfType<SacrificeNontokenGreenCreatureAlternativeCost>().Subject;
        picked.SacrificedCreature.Should().BeSameAs(bear);
    }

    [Fact]
    public void BotProbe_WrongCard_YieldsNothing()
    {
        var cultivate = CultivateFactory.Create(_alice);
        cultivate.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cultivate);

        MakeGreenCreature("Bear", _alice, isToken: false);

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, stack);

        var probe = new FlareOfCultivationAltCostProbe();
        var candidates = probe.CandidatesFor(cultivate, _alice, ctx).ToList();

        candidates.Should().BeEmpty(because: "probe only matches Flare of Cultivation");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    /// <summary>Create a green creature (mana cost {G}) on a player's battlefield.</summary>
    private Creature MakeGreenCreature(string name, Player controller, bool isToken)
    {
        // CardColors.GetColors derives green from the {G} in the mana cost.
        var creature = new Creature(name, "{G}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        if (isToken) creature.MarkAsToken();
        return creature;
    }

    /// <summary>Create a red creature (mana cost {R}) on a player's battlefield.</summary>
    private Creature MakeRedCreature(string name, Player controller)
    {
        var creature = new Creature(name, "{R}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }
}

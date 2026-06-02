using FluentAssertions;
using Moq;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FaunaShamanFactory"/> — Creature — Elf Shaman {1}{G}
/// 2/2 (Magic 2011). Oracle (Scryfall, verified):
///   "{G}, {T}, Discard a creature card: Search your library for a creature
///    card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
///   - Card identity (Creature + Elf/Shaman, {1}{G}, 2/2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one activated ability, no triggered/mana abilities.
///   - Activation cost composition: {G} mana + tap + discard-a-creature-card.
///   - Discard cost gating: payable only with a creature card in hand.
///   - Pay-then-resolve: discards the creature card, tutors ONE creature card
///     from library into hand, leaves a second creature in library.
///   - Resolve with only non-creature cards in library → no card moved.
/// </summary>
[Trait("Color", "G")]
public class FaunaShamanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FaunaShaman_IsElfShaman_AtOneG_TwoTwo()
    {
        var c = FaunaShamanFactory.Create(_alice);

        c.Name.Should().Be("Fauna Shaman");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void FaunaShaman_HasExactlyOneActivatedAbility_NoTriggersOrManaAbilities()
    {
        var c = FaunaShamanFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_Cost_IsGManaPlusTapPlusDiscardCreature()
    {
        var c = FaunaShamanFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {G} in the activation cost");
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "the {T} tap symbol");
        ability.Costs.OfType<DiscardACreatureCardCost>().Should().ContainSingle(
            "the 'Discard a creature card' cost");
    }

    [Fact]
    public void DiscardCost_CanPay_OnlyWithCreatureCardInHand()
    {
        var c = FaunaShamanFactory.Create(_alice);

        var cost = c.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs).OfType<DiscardACreatureCardCost>().Single();

        // No creature card in hand → cannot pay.
        cost.CanPay(_alice).Should().BeFalse("no creature card in hand");

        // A non-creature card does not satisfy it.
        var land = new Land("Forest");
        land.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(land);
        cost.CanPay(_alice).Should().BeFalse("a land cannot pay 'discard a creature card'");

        // A creature card does.
        _alice.Zones.Hand.AddCard(MakeCreatureInHand(_alice, "Bear"));
        cost.CanPay(_alice).Should().BeTrue("a creature card in hand pays the cost");
    }

    [Fact]
    public void PayThenResolve_DiscardsCreature_TutorsOneCreatureToHand()
    {
        var c = FaunaShamanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // Fodder creature in hand to pay the discard cost.
        var fodder = MakeCreatureInHand(_alice, "Fodder");
        _alice.Zones.Hand.AddCard(fodder);

        // Two creature cards in the library — only ONE should be tutored.
        var tutorable1 = MakeCreatureInLibrary(_alice, "Tarmogoyf");
        var tutorable2 = MakeCreatureInLibrary(_alice, "Wild Mongrel");
        // A non-creature card the tutor must never pick.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var discard = ability.Costs.OfType<DiscardACreatureCardCost>().Single();

        discard.CanPay(_alice).Should().BeTrue();
        discard.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        // The fodder creature was discarded (CR 701.16a).
        _alice.Zones.Hand.ContainsCard(fodder).Should().BeFalse(
            "the discarded creature card leaves the hand");
        _alice.Zones.Graveyard.ContainsCard(fodder).Should().BeTrue(
            "the discarded creature card goes to the graveyard");

        // Exactly one creature card was tutored from the library into the hand.
        var creaturesInHand = _alice.Zones.Hand.GetCards()
            .Where(x => x.HasType(CardType.Creature)).ToList();
        creaturesInHand.Should().HaveCount(1,
            "Fauna Shaman tutors A (one) creature card into hand");
        creaturesInHand[0].Zone.Should().Be(ZoneType.Hand);

        // One creature card remains in the library; the land was never taken.
        _alice.Zones.Library.GetCards()
            .Count(x => x.HasType(CardType.Creature)).Should().Be(1,
            "only one of the two library creatures is tutored");
        _alice.Zones.Library.GetCards().Should().Contain(bog,
            "the non-creature card is never tutored");
    }

    [Fact]
    public void Resolve_NoCreatureCardInLibrary_MovesNoCard()
    {
        var c = FaunaShamanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no creature card in library → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteAsync_HonoursScriptedLibraryPick_NotFirstCandidate()
    {
        // PLAN 01 Slice D — the tutor helper now prompts the agent off the
        // ResolutionContext. A scripted agent that picks the SECOND eligible
        // creature must be honoured through ExecuteAsync (not silently
        // first-picked).
        var c = FaunaShamanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var first = MakeCreatureInLibrary(_alice, "Tarmogoyf");
        var second = MakeCreatureInLibrary(_alice, "Wild Mongrel");

        // Agent returns the SECOND candidate (proves real consultation).
        var agent = new Moq.Mock<Majik.Core.Players.Agents.IPlayerAgent>();
        agent.Setup(a => a.ChooseLibraryPickAsync(
                It.IsAny<Majik.Core.Game.GameContext?>(),
                It.IsAny<IReadOnlyList<ICard>>(),
                It.IsAny<string>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .Returns<Majik.Core.Game.GameContext?, IReadOnlyList<ICard>, string, System.Threading.CancellationToken>(
                (_, cands, _, _) => System.Threading.Tasks.Task.FromResult<ICard?>(cands[1]));

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var rc = ResolutionContext.For(_alice, agent.Object, game: null, chosenTargets: null);
        foreach (var effect in ability.Effects)
        {
            await effect.ExecuteAsync(rc);
        }

        _alice.Zones.Hand.ContainsCard(second).Should().BeTrue(
            "the agent's chosen (second) creature is the one tutored");
        _alice.Zones.Hand.ContainsCard(first).Should().BeFalse(
            "the first candidate was not auto-picked");
        agent.Verify(a => a.ChooseLibraryPickAsync(
            It.IsAny<Majik.Core.Game.GameContext?>(),
            It.IsAny<IReadOnlyList<ICard>>(),
            It.IsAny<string>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeCreatureInHand(Player owner, string name)
    {
        var card = new Creature(name, "{G}", power: 1, toughness: 1);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    private static Creature MakeCreatureInLibrary(Player owner, string name)
    {
        var card = new Creature(name, "{G}", power: 1, toughness: 1);
        card.SetOwner(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}

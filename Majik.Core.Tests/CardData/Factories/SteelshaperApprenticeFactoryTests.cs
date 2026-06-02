using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SteelshaperApprenticeFactory"/>.
///
/// Steelshaper Apprentice (Fifth Dawn, {2}{W}{W}). Creature — Human Soldier
/// 1/3. Oracle text (verified against Scryfall):
///   "{W}, {T}, Return this creature to its owner's hand: Search your library
///    for an Equipment card, reveal that card, put it into your hand, then
///    shuffle."
///
/// Covers:
/// - Identity (name, mana cost, Human Soldier subtypes, 1/3, owner/controller).
/// - Activated ability present with the { {W}, {T}, return-self } cost trio
///   and no targets.
/// - The return-self cost is illegal when the creature is not on the
///   battlefield, legal when it is; paying it bounces the creature to its
///   owner's hand (CR 118 / CR 701.10).
/// - Resolving the effect picks the only Equipment card and places it in hand;
///   a non-Equipment artifact (Equipment is a SUBTYPE, CR 205.3g) stays in the
///   library.
/// - Empty / declined search is a no-op (CR 701.19a).
/// - CR 701.20a — the library is shuffled after the search; a
///   LibraryShuffledEvent is published.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "W")]
public class SteelshaperApprenticeFactoryTests
{
    private static ActivatedAbility GetAbility(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>().Single();

    private static Artifact MakeEquipment(string name, Player owner, string cost = "{1}")
    {
        var c = new Artifact(name, cost, subtypes: new[] { CardSubtype.Equipment });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void ResolveEffect(ActivatedAbility ability)
    {
        foreach (var fx in ability.Effects)
        {
            fx.Execute();
        }
    }

    [Fact]
    public void Identity_NameTypeSubtypesPtAndCost()
    {
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);

        card.Name.Should().Be("Steelshaper Apprentice");
        card.ManaCost.Should().Be("{2}{W}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(owner);
        card.Controller.Should().BeSameAs(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SteelshaperApprentice()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Steelshaper Apprentice", owner);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Steelshaper Apprentice");
        card.ManaCost.Should().Be("{2}{W}{W}");
    }

    [Fact]
    public void HasActivatedAbility_WithWhiteTapReturnSelfCosts_AndNoTargets()
    {
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);

        var ability = GetAbility(card);
        ability.TargetRequests.Should().BeEmpty(
            "the ability searches the library, it does not target.");
        ability.Costs.Should().HaveCount(3,
            "cost is {W}, {T}, and Return this creature to its owner's hand.");
        ability.Costs.OfType<ManaCostCost>().Single().Description.Should().Contain("W");
        ability.Costs.OfType<AdditionalCost>().Single().CostType
            .Should().Be(AdditionalCostType.Tap);
        ability.Costs.OfType<ReturnSelfToHandCost>().Single().Self
            .Should().BeSameAs(card);
    }

    [Fact]
    public void ReturnSelfCost_IllegalOffBattlefield_LegalOn_AndBouncesToHand()
    {
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);
        var cost = GetAbility(card).Costs.OfType<ReturnSelfToHandCost>().Single();

        // Not on the battlefield yet (CR 118 — cannot pay).
        cost.CanPay(owner).Should().BeFalse();

        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        cost.CanPay(owner).Should().BeTrue();

        cost.Pay(owner);

        // CR 701.10 — returned to its owner's hand.
        owner.Zones.Battlefield.GetCards().Should().NotContain(card);
        owner.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(card);
        card.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_PicksEquipment_PlacesInHand()
    {
        // Library: a Forest (filtered), a non-Equipment Artifact (filtered —
        // Equipment is a SUBTYPE), and a Bonesplitter (eligible Equipment).
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);

        var forest = new Land("Forest",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(owner); forest.SetController(owner);
        var manalith = new Artifact("Manalith", "{3}");
        manalith.SetOwner(owner); manalith.SetController(owner);
        var splitter = MakeEquipment("Bonesplitter", owner);
        owner.Zones.Library.AddCard(forest);
        owner.Zones.Library.AddCard(manalith);
        owner.Zones.Library.AddCard(splitter);

        AgentRegistry.Set(owner, new DeterministicBotAgent());
        GameRandomRegistry.Set(owner, new GameRandom(seed: 1));
        try
        {
            ResolveEffect(GetAbility(card));

            owner.Zones.Hand.GetCards().Select(c => c.Name)
                .Should().ContainSingle().Which.Should().Be("Bonesplitter");
            owner.Zones.Library.GetCards().Select(c => c.Name)
                .Should().BeEquivalentTo(new[] { "Forest", "Manalith" });
        }
        finally
        {
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Resolve_NoEquipmentInLibrary_IsNoOp()
    {
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);
        var manalith = new Artifact("Manalith", "{3}");
        manalith.SetOwner(owner); manalith.SetController(owner);
        owner.Zones.Library.AddCard(manalith);

        AgentRegistry.Set(owner, new DeterministicBotAgent());
        GameRandomRegistry.Set(owner, new GameRandom(seed: 1));
        try
        {
            ResolveEffect(GetAbility(card));

            owner.Zones.Hand.GetCards().Should().BeEmpty();
            owner.Zones.Library.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Manalith");
        }
        finally
        {
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Resolve_PublishesLibraryShuffledEvent()
    {
        // CR 701.20a — shuffle after the search resolves; the helper publishes
        // LibraryShuffledEvent so replay / UI can observe.
        var owner = new Player("A", 20);
        var card = SteelshaperApprenticeFactory.Create(owner);
        var splitter = MakeEquipment("Bonesplitter", owner);
        owner.Zones.Library.AddCard(splitter);

        AgentRegistry.Set(owner, new DeterministicBotAgent());
        GameRandomRegistry.Set(owner, new GameRandom(seed: 1));
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(owner, bus);
        try
        {
            ResolveEffect(GetAbility(card));

            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(owner);
            captured.Reason.Should().Be("steelshaper-apprentice");
        }
        finally
        {
            EventBusRegistry.Clear();
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }
}

using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Per-chapter effects for The Huntsman's Redemption (Tarkir: Dragonstorm,
/// {2}{G}) — a non-transforming, self-sacrificing Enchantment — Saga wired in
/// <see cref="SagaBinder"/>. Mirrors <see cref="RestorationOfEiganjoSagaTests"/>.
///   I  — Create a 3/3 green Beast creature token.
///   II — You may sacrifice a creature. If you do, search your library for a
///        creature or basic land card, reveal it, put it into your hand, then
///        shuffle.
///   III— Up to two target creatures each get +2/+2 and gain trample until end
///        of turn.
/// </summary>
[Trait("Color", "G")]
public class HuntsmansRedemptionSagaTests
{
    private const string CardName = "The Huntsman's Redemption";

    private static (Player owner, Enchantment saga) MakeSaga()
    {
        var owner = new Player("Alice", 20);
        var saga = new Enchantment(CardName, "2G",
            subtypes: new[] { CardSubtype.Saga })
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(saga);
        return (owner, saga);
    }

    private static CardEntity MakeEntity() =>
        new()
        {
            ScryfallId = Guid.NewGuid().ToString(),
            Name = CardName,
            TypeLine = "Enchantment — Saga",
            OracleText = TheHuntsmansRedemptionFactory.OracleText,
            Colors = "G",
            ColorIdentity = "G",
            Keywords = "",
            Legalities = "",
        };

    [Fact]
    public void Identity_IsGreenEnchantmentSaga_At2G()
    {
        var card = TheHuntsmansRedemptionFactory.Create(new Player("Alice", 20));

        card.Name.Should().Be(CardName);
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        // CR 712 — this Saga does NOT transform; no DFC face tracker is attached.
        card.MdfcState.Should().BeNull();
    }

    [Fact]
    public void ChapterI_CreatesA3x3GreenBeastToken()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter(); // chapter I

        var beast = owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Beast");
        beast.Should().NotBeNull("chapter I creates a 3/3 green Beast token");
        beast!.Power.Should().Be(3);
        beast.Toughness.Should().Be(3);
        beast.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        beast.IsToken.Should().BeTrue();
        CardColors.GetColors(beast).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void ChapterII_SacrificeCreature_ThenTutorsCreatureOrBasicLandToHand()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        // Agent sacrifices the offered creature for "you may sacrifice a creature".
        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromBattlefield(cs => cs[0]);
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
            var fodder = new Creature("Fodder", "G", 1, 1)
            { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
            owner.Zones.Battlefield.AddCard(fodder);

            // Library: a non-matching instant, then a creature card (the pick).
            owner.Zones.Library.AddCard(new Instant("Shock", "R"));
            var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = owner, Controller = owner, Zone = ZoneType.Library };
            owner.Zones.Library.AddCard(grizzly);

            saga.SagaState!.AdvanceAndChapter(); // I (Beast token)
            saga.SagaState.AdvanceAndChapter();  // II — sacrifice + tutor

            owner.Zones.Battlefield.GetCards().Should().NotContain(fodder,
                "the creature was sacrificed");
            owner.Zones.Graveyard.GetCards().Should().Contain(fodder,
                "the sacrificed creature lands in its owner's graveyard");
            owner.Zones.Hand.GetCards().Should().Contain(grizzly,
                "the tutored creature card is put into hand");
            owner.Zones.Library.GetCards().Should().NotContain(grizzly);
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    [Fact]
    public void ChapterII_AgentDeclines_NoSacrificeNoTutor()
    {
        var (owner, saga) = MakeSaga();
        SagaBinder.Bind(saga, MakeEntity()).Should().BeTrue();

        var agent = new Majik.Core.Players.Agents.ScriptedAgent();
        agent.QueueFromBattlefield((ICard?)null); // decline the optional sacrifice
        Majik.Core.Players.Agents.AgentRegistry.Set(owner, agent);

        try
        {
            var fodder = new Creature("Fodder", "G", 1, 1)
            { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
            owner.Zones.Battlefield.AddCard(fodder);
            var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = owner, Controller = owner, Zone = ZoneType.Library };
            owner.Zones.Library.AddCard(grizzly);

            saga.SagaState!.AdvanceAndChapter(); // I
            saga.SagaState.AdvanceAndChapter();  // II — declined

            owner.Zones.Battlefield.GetCards().Should().Contain(fodder,
                "decline → no sacrifice");
            owner.Zones.Hand.GetCards().Should().NotContain(grizzly,
                "no sacrifice → no tutor (the 'if you do' is unmet)");
        }
        finally
        {
            Majik.Core.Players.Agents.AgentRegistry.Remove(owner);
        }
    }

    [Fact]
    public void ChapterIII_PumpsUpToTwoCreatures_Plus2Plus2AndTrample_UntilEndOfTurn()
    {
        var (owner, saga) = MakeSaga();
        var effects = new ContinuousEffectsService();
        SagaBinder.Bind(saga, MakeEntity(), effects: effects).Should().BeTrue();

        // Three creatures present — only the top two by power are pumped (v1
        // deterministic "up to two target creatures").
        var big = new Creature("Big", "G", 4, 4)
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        var mid = new Creature("Mid", "G", 3, 3)
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        var small = new Creature("Small", "G", 1, 1)
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(big);
        owner.Zones.Battlefield.AddCard(mid);
        owner.Zones.Battlefield.AddCard(small);

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II (no creatures sacrificed — no agent)
        saga.SagaState.AdvanceAndChapter();  // III — pump

        var bigChars = effects.Compute(big);
        bigChars.Power.Should().Be(6, "4 base +2 → 6");
        bigChars.Toughness.Should().Be(6, "4 base +2 → 6");
        bigChars.Keywords.Should().Contain("Trample");

        var midChars = effects.Compute(mid);
        midChars.Power.Should().Be(5, "3 base +2 → 5");
        midChars.Keywords.Should().Contain("Trample");

        // The third creature is not among the (up to) two pumped.
        var smallChars = effects.Compute(small);
        smallChars.Power.Should().Be(1, "only two creatures are pumped");
        smallChars.Keywords.Should().NotContain("Trample");

        // CR 514.2 — both effects expire at cleanup, reverting the creatures.
        effects.ExpireEndOfTurn();
        effects.Compute(big).Power.Should().Be(4);
        effects.Compute(big).Keywords.Should().NotContain("Trample");
    }

    [Fact]
    public void FinalChapterIII_SelfSacrifices_SagaStateNotCleared()
    {
        var (owner, saga) = MakeSaga();
        var effects = new ContinuousEffectsService();
        SagaBinder.Bind(saga, MakeEntity(), effects: effects).Should().BeTrue();

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II
        saga.SagaState.AdvanceAndChapter();  // III

        // CR 714.5 / 704.5r — after the final chapter the Saga should be
        // sacrificed by the generic SBA. Unlike the transforming Sagas, the
        // SagaState is NOT cleared, so the sacrifice check fires.
        saga.SagaState.Should().NotBeNull();
        saga.SagaState!.ShouldBeSacrificed().Should().BeTrue(
            "a non-transforming Saga self-sacrifices after its final chapter");
    }
}

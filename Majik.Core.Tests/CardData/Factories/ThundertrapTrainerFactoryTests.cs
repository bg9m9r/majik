using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Thundertrap Trainer (Duskmourn, {1}{U}). Creature — Otter Wizard
/// 1/2. Oracle text (Scryfall, verified 2026-06-12):
///   "Offspring {4} (You may pay an additional {4} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    When this creature enters, look at the top four cards of your library.
///    You may reveal a noncreature, nonland card from among them and put it
///    into your hand. Put the rest on the bottom of your library in a random
///    order."
///
/// The Offspring {4} half rides the shared <see cref="Keywords.OffspringAbility"/>
/// subsystem (CR 702.169) exercised exhaustively by
/// <see cref="Majik.Core.Tests.Keywords.OffspringTests"/>; here we assert the
/// keyword marker + ETB token presence and focus on the bespoke ETB dig: look
/// at top four, may take a noncreature-nonland card to hand, rest to the bottom
/// in random order.
/// </summary>
public class ThundertrapTrainerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, ICard card)
    {
        var concrete = (Card)card;
        concrete.SetOwner(owner);
        owner.Zones.Library.AddCard(concrete);
        concrete.SetZone(ZoneType.Library);
        return concrete;
    }

    [Fact]
    public void Identity_OtterWizard_1_2_At1U_WithOffspring()
    {
        var trainer = ThundertrapTrainerFactory.Create(_alice);

        trainer.Name.Should().Be("Thundertrap Trainer");
        trainer.ManaCost.Should().Be("{1}{U}");
        trainer.HasType(CardType.Creature).Should().BeTrue();
        trainer.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        trainer.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        trainer.BasePower.Should().Be(1);
        trainer.BaseToughness.Should().Be(2);
        trainer.Owner.Should().BeSameAs(_alice);
        trainer.Controller.Should().BeSameAs(_alice);
        trainer.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Offspring");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FullShape()
    {
        var card = NamedCardFactory.Create("Thundertrap Trainer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thundertrap Trainer");
        card.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        // Both the Offspring ETB and the dig ETB are attached for observability.
        card.Abilities.OfType<TriggeredAbility>().Count()
            .Should().BeGreaterThanOrEqualTo(2);
    }

    // -----------------------------------------------------------------------
    // ETB dig (CR 603.6a) — look at top four, may take a noncreature-nonland
    // card to hand, rest to the bottom in a random order.
    // -----------------------------------------------------------------------

    private TriggeredAbility DigTrigger(Creature trainer) =>
        trainer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("top four")));

    private void FireDig(Creature trainer)
    {
        foreach (var e in DigTrigger(trainer).Effects) e.Execute();
    }

    [Fact]
    public void Etb_RevealsNoncreatureNonlandCardToHand_RestToBottom()
    {
        var trainer = ThundertrapTrainerFactory.Create(_alice);

        // Top four: a creature, a land, an instant (noncreature/nonland), a
        // sorcery. The instant is the topmost noncreature-nonland card so the
        // default selector reveals it.
        var creature = NewCardInLibrary(_alice, new Creature("Grizzly Bears", "1G", 2, 2));
        var land = NewCardInLibrary(_alice, new Land("Island"));
        var instant = NewCardInLibrary(_alice, new Instant("Lightning Bolt", "R"));
        var sorcery = NewCardInLibrary(_alice, new Sorcery("Divination", "2U"));
        // A 5th card deeper than the top four — must be untouched.
        var deep = NewCardInLibrary(_alice, new Instant("Counterspell", "UU"));

        FireDig(trainer);

        // The instant (first noncreature-nonland among the top four) is in hand.
        _alice.Zones.Hand.GetCards().Should().Contain(instant);
        _alice.Zones.Hand.GetCards().Should().NotContain(creature);
        _alice.Zones.Hand.GetCards().Should().NotContain(land);
        _alice.Zones.Hand.GetCards().Should().NotContain(sorcery);

        // The other three of the top four went to the bottom (no longer on top,
        // still in library), and the deeper card was untouched.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().NotContain(instant, "the revealed card left the library");
        lib.Should().Contain(creature);
        lib.Should().Contain(land);
        lib.Should().Contain(sorcery);
        lib.Should().Contain(deep);

        // The deep card stayed on top (the dug-through cards were bottomed
        // beneath it).
        lib[0].Should().BeSameAs(deep,
            "the card below the top four is now on top after the three are bottomed");
    }

    [Fact]
    public void Etb_NoNoncreatureNonlandInTopFour_TakesNothing_AllToBottom()
    {
        var trainer = ThundertrapTrainerFactory.Create(_alice);

        // Top four are all creatures or lands — nothing eligible.
        var c1 = NewCardInLibrary(_alice, new Creature("Grizzly Bears", "1G", 2, 2));
        var l1 = NewCardInLibrary(_alice, new Land("Island"));
        var c2 = NewCardInLibrary(_alice, new Creature("Hill Giant", "3R", 3, 3));
        var l2 = NewCardInLibrary(_alice, new Land("Mountain"));

        var before = _alice.Zones.Hand.GetCards().Count();

        FireDig(trainer);

        _alice.Zones.Hand.GetCards().Count().Should().Be(before,
            "no noncreature-nonland card is in the top four, so nothing is taken");
        _alice.Zones.Library.GetCards().Should()
            .Contain(new ICard[] { c1, l1, c2, l2 },
                "all four remain in the library (now on the bottom)");
    }

    [Fact]
    public void Etb_FewerThanFourInLibrary_DoesNotThrow_TakesEligible()
    {
        var trainer = ThundertrapTrainerFactory.Create(_alice);

        var enchantment = NewCardInLibrary(_alice, new Enchantment("Pacifism", "1W"));

        FireDig(trainer);

        _alice.Zones.Hand.GetCards().Should().Contain(enchantment,
            "an enchantment is noncreature/nonland and is the only card — it is taken");
    }

    [Fact]
    public void Etb_Offspring_PutsTokenOnBattlefield_WhenPaid()
    {
        var triggers = new TriggerManager(
            new Majik.Core.Stack.Stack(new EventBus()), new EventBus());
        var bus = new EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);
        Majik.Core.Services.ZoneServiceRegistry.SetDefault(zones);

        var trainer = ThundertrapTrainerFactory.Create(_alice, triggers);
        trainer.SetWasOffspringPaid(true);
        _alice.Zones.Battlefield.AddCard(trainer);
        trainer.SetZone(ZoneType.Battlefield);

        var offspringEtb = trainer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Offspring")));
        foreach (var e in offspringEtb.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken && c.Name == "Thundertrap Trainer")
            .Should().Be(1, "CR 702.169b — Offspring paid mints a 1/1 token copy");
    }
}

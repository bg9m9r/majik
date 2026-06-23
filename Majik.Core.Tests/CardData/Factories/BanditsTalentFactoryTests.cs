using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BanditsTalentFactory"/> — Bandit's Talent
/// (Outlaws of Thunder Junction, {1}{B}, Enchantment — Class).
///
/// Covers only Bandit's Talent's UNIQUE per-level behaviour (Class leveling
/// itself — CR 716 — is exercised by the analogue Talent suites). The
/// CardFactoryContractTests already asserts dispatch + well-formedness, so
/// there is no dispatch test here.
///
/// - Identity: {1}{B}, Enchantment — Class, MaxLevel=3.
/// - Level 1 ETB (CR 603.6a): "each opponent discards two cards unless they
///   discard a nonland card." v1 deterministic: an opponent with a nonland
///   card discards one nonland; an opponent with only lands discards two.
/// - Level 2 (CR 603.1): each opponent's upkeep, if that opponent has one or
///   fewer cards in hand, they lose 2 life. Gated on level >= 2 + re-checked
///   hand size at resolution (CR 603.4).
/// - Level 3 (CR 603.1): your draw step, draw an additional card per opponent
///   with one or fewer cards in hand. Gated on level >= 3.
/// </summary>
[Trait("Color", "B")]
public class BanditsTalentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private static Card MakeCard(string name, CardType type, Player owner)
    {
        Card c = type == CardType.Land
            ? new Land(name)
            : new Sorcery(name, "{1}");
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static void AddToHand(Player p, Card c) => p.Zones.Hand.AddCard(c);

    private static void AddToLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Sorcery($"Lib{i}", "{1}");
            c.SetOwner(p);
            c.SetController(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }

    // The factory attaches the three triggered abilities in printed (oracle)
    // order: ETB discard (0), Level-2 upkeep punisher (1), Level-3 draw step (2).
    private static TriggeredAbility EtbTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>().ElementAt(0);

    private static TriggeredAbility UpkeepTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>().ElementAt(1);

    private static TriggeredAbility DrawTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>().ElementAt(2);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BanditsTalent_Identity_BlackClassEnchantment_MaxLevelThree()
    {
        var c = BanditsTalentFactory.Create(_alice);

        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "CR 205.3h — Class is an enchantment subtype (CR 716)");
        c.ManaCost.Should().Be("{1}{B}");

        var parsed = ManaCost.Parse("{1}{B}");
        parsed.Black.Should().Be(1);
        parsed.Generic.Should().Be(1);

        ((Permanent)c).ClassState.Should().NotBeNull();
        ((Permanent)c).ClassState!.CurrentLevel.Should().Be(1);
        ((Permanent)c).ClassState!.MaxLevel.Should().Be(3);
    }

    [Fact]
    public void BanditsTalent_HasTwoSorcerySpeedLevelUpAbilities()
    {
        var c = BanditsTalentFactory.Create(_alice);

        var levelUps = c.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activation per printed level above 1 " +
            "({B}: Level 2 and {3}{B}: Level 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 — Class level-up activations are sorcery-speed only");
    }

    // -----------------------------------------------------------------------
    // Level 1 ETB — each opponent discards two cards unless they discard a nonland card
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_OpponentWithNonland_DiscardsExactlyOneNonland()
    {
        var talent = (Enchantment)NamedCardFactory.Create("Bandit's Talent", _alice);
        talent.SetZone(ZoneType.Battlefield);

        // Bob: 1 land + 2 nonland → may discard a single nonland.
        AddToHand(_bob, MakeCard("Swamp", CardType.Land, _bob));
        var spellA = MakeCard("Spell A", CardType.Sorcery, _bob);
        var spellB = MakeCard("Spell B", CardType.Sorcery, _bob);
        AddToHand(_bob, spellA);
        AddToHand(_bob, spellB);

        ContextResolve.Resolve(EtbTrigger(talent), _alice, _alice, _bob);

        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            "CR 701.8 — discarding ONE nonland is the printed alternative to discarding two");
        _bob.Zones.Hand.GetCards().Count(c => !c.HasType(CardType.Land))
            .Should().Be(1, "exactly one of the two nonland cards was discarded");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.HasType(CardType.Land).Should().BeFalse("a nonland card was discarded");
    }

    [Fact]
    public void Etb_OpponentWithOnlyLands_DiscardsTwoCards()
    {
        var talent = (Enchantment)NamedCardFactory.Create("Bandit's Talent", _alice);
        talent.SetZone(ZoneType.Battlefield);

        AddToHand(_bob, MakeCard("Swamp 1", CardType.Land, _bob));
        AddToHand(_bob, MakeCard("Swamp 2", CardType.Land, _bob));
        AddToHand(_bob, MakeCard("Swamp 3", CardType.Land, _bob));

        ContextResolve.Resolve(EtbTrigger(talent), _alice, _alice, _bob);

        _bob.Zones.Hand.GetCards().Should().HaveCount(1,
            "no nonland to discard → the opponent discards two cards");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Etb_DoesNotAffectController()
    {
        var talent = (Enchantment)NamedCardFactory.Create("Bandit's Talent", _alice);
        talent.SetZone(ZoneType.Battlefield);

        AddToHand(_alice, MakeCard("My Spell", CardType.Sorcery, _alice));

        ContextResolve.Resolve(EtbTrigger(talent), _alice, _alice, _bob);

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "CR 102.1 — the controller is never their own opponent");
    }

    // -----------------------------------------------------------------------
    // Level 2 — each opponent's upkeep, ≤1 card in hand → lose 2 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Level2_OpponentUpkeep_OneOrFewerCards_LosesTwoLife()
    {
        var talent = BanditsTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        ((Permanent)talent).ClassState!.LevelUpTo(2);

        AddToHand(_bob, MakeCard("Lone Card", CardType.Sorcery, _bob)); // exactly 1 in hand

        var trigger = UpkeepTrigger(talent);
        trigger.SetTriggeringPlayer(_bob);
        ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(18, "CR 603.4 — ≤1 card in hand → that player loses 2 life");
    }

    [Fact]
    public void Level2_OpponentUpkeep_MoreThanOneCard_NoLifeLoss()
    {
        var talent = BanditsTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        ((Permanent)talent).ClassState!.LevelUpTo(2);

        AddToHand(_bob, MakeCard("Card 1", CardType.Sorcery, _bob));
        AddToHand(_bob, MakeCard("Card 2", CardType.Sorcery, _bob));

        var trigger = UpkeepTrigger(talent);
        trigger.SetTriggeringPlayer(_bob);
        ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(20, "CR 603.4 — intervening-if fails with 2 cards in hand");
    }

    [Fact]
    public void Level2_OpponentUpkeep_NotTriggeredBelowLevelTwo()
    {
        var talent = BanditsTalentFactory.Create(_alice); // stays level 1
        talent.SetZone(ZoneType.Battlefield);

        AddToHand(_bob, MakeCard("Lone Card", CardType.Sorcery, _bob));

        var trigger = UpkeepTrigger(talent);
        trigger.SetTriggeringPlayer(_bob);
        ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(20,
            "CR 716.2 — the Level-2 ability is inactive while the Class is below level 2");
    }

    [Fact]
    public void Level2_DoesNotFireOnControllersOwnUpkeep()
    {
        var talent = BanditsTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        ((Permanent)talent).ClassState!.LevelUpTo(2); // upkeep ability active at level 2

        var trigger = UpkeepTrigger(talent);

        trigger.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice))
            .Should().BeFalse("CR 102.1 — only an opponent's upkeep fires this");
    }

    // -----------------------------------------------------------------------
    // Level 3 — your draw step, draw one extra per opponent with ≤1 card in hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Level3_DrawStep_DrawsOneExtraPerLowHandOpponent()
    {
        var talent = BanditsTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        ((Permanent)talent).ClassState!.LevelUpTo(2);
        ((Permanent)talent).ClassState!.LevelUpTo(3);

        AddToLibrary(_alice, 5);

        // Bob: 1 card (qualifies). Carol: 3 cards (does not).
        AddToHand(_bob, MakeCard("Bob Card", CardType.Sorcery, _bob));
        AddToHand(_carol, MakeCard("Carol 1", CardType.Sorcery, _carol));
        AddToHand(_carol, MakeCard("Carol 2", CardType.Sorcery, _carol));

        ContextResolve.Resolve(DrawTrigger(talent), _alice, _alice, _bob, _carol);

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "exactly one opponent (Bob) has ≤1 card in hand → draw one additional card");
    }

    [Fact]
    public void Level3_DrawStep_NotTriggeredBelowLevelThree()
    {
        var talent = BanditsTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        ((Permanent)talent).ClassState!.LevelUpTo(2); // only level 2

        AddToLibrary(_alice, 5);
        AddToHand(_bob, MakeCard("Bob Card", CardType.Sorcery, _bob));

        ContextResolve.Resolve(DrawTrigger(talent), _alice, _alice, _bob);

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "CR 716.2 — the Level-3 ability is inactive while the Class is below level 3");
    }
}

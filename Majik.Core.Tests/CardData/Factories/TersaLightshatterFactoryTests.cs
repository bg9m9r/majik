using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TersaLightshatterFactory"/>
/// (Tarkir: Dragonstorm, {2}{R}). Legendary Creature — Orc Wizard 3/3, Haste.
///
/// Covers ONLY Tersa's unique behaviour (the contract test handles dispatch +
/// well-formedness for every implemented card):
/// - Identity ({2}{R}, 3/3, Legendary Creature — Orc Wizard, Haste).
/// - ETB trigger: discard up to two cards, then draw THAT MANY.
///   - Two cards in hand → discard 2, draw 2.
///   - One card in hand → discard 1, draw 1 ("that many" link).
/// - Attack trigger: intervening-if "7+ cards in your graveyard" (CR 603.4);
///   on resolve exiles a random graveyard card and grants "play it this turn".
/// - Attack-trigger self-attack predicate (fires only when Tersa attacks).
/// </summary>
[Trait("Color", "R")]
public class TersaLightshatterFactoryTests
{
    private static Creature MakeNonland(Player owner, string name) =>
        WithOwner(new Creature(name, "R", 1, 1), owner);

    private static Card MakeLand(Player owner, string name) =>
        WithOwner(new Land(name), owner);

    private static T WithOwner<T>(T c, Player owner) where T : Card
    {
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Tersa_Identity_LegendaryOrcWizard_3_3_At_2R_WithHaste()
    {
        var alice = new Player("Alice", 20);
        var tersa = TersaLightshatterFactory.Create(alice);

        tersa.Name.Should().Be("Tersa Lightshatter");
        tersa.ManaCost.Should().Be("{2}{R}");
        tersa.ManaCostValue.TotalValue.Should().Be(3, "{2}{R} = 2 + 1");
        tersa.HasType(CardType.Creature).Should().BeTrue();
        tersa.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Tersa is a Legendary creature");
        tersa.HasSubtype(CardSubtype.Orc).Should().BeTrue();
        tersa.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        tersa.BasePower.Should().Be(3);
        tersa.BaseToughness.Should().Be(3);
        tersa.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue("Tersa has Haste");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — discard up to two, then draw that many
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Etb_TwoCardsInHand_DiscardsTwo_DrawsTwo()
    {
        var alice = new Player("Alice", 20);
        var tersa = TersaLightshatterFactory.Create(alice);

        var h1 = MakeNonland(alice, "Hand1");
        var h2 = MakeNonland(alice, "Hand2");
        alice.Zones.Hand.AddCard(h1); h1.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(h2); h2.SetZone(ZoneType.Hand);

        var lib1 = MakeNonland(alice, "Lib1");
        var lib2 = MakeNonland(alice, "Lib2");
        alice.Zones.Library.AddCard(lib1); lib1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib2); lib2.SetZone(ZoneType.Library);

        var etb = SingleEtb(tersa);
        await etb.ResolveAsync(agent: null, game: null);

        // Discarded 2 → 2 in graveyard; drew 2 → net 2 in hand.
        alice.Zones.Graveyard.GetCards().Should().HaveCount(2,
            "discard up to two cards moves them to the graveyard");
        alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "discarded 2 then drew 2 → net 2 in hand");
        alice.Zones.Library.GetCards().Should().BeEmpty("both library cards drawn");
    }

    [Fact]
    public async Task Etb_OneCardInHand_DiscardsOne_DrawsExactlyOne()
    {
        // "that many" link: discarding fewer than two draws fewer than two.
        var alice = new Player("Alice", 20);
        var tersa = TersaLightshatterFactory.Create(alice);

        var only = MakeNonland(alice, "OnlyCard");
        alice.Zones.Hand.AddCard(only); only.SetZone(ZoneType.Hand);

        var lib1 = MakeNonland(alice, "Lib1");
        var lib2 = MakeNonland(alice, "Lib2");
        alice.Zones.Library.AddCard(lib1); lib1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib2); lib2.SetZone(ZoneType.Library);

        var etb = SingleEtb(tersa);
        await etb.ResolveAsync(agent: null, game: null);

        alice.Zones.Graveyard.GetCards().Should().HaveCount(1, "only one card to discard");
        alice.Zones.Hand.GetCards().Should().HaveCount(1, "discarded 1 → draw exactly 1");
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "only ONE card drawn (not two) — draw equals discard count");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — intervening-if 7+ graveyard, exile random, may play
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_InterveningIf_GatesOnSevenGraveyardCards()
    {
        var alice = new Player("Alice", 20);
        var tersa = OnBattlefield(alice);
        var attack = SingleAttack(tersa);

        // 6 in graveyard → intervening-if false → not put on stack (CR 603.4).
        FillGraveyard(alice, 6);
        attack.CanBePutOnStack().Should().BeFalse(
            "intervening-if 'seven or more cards in your graveyard' is false at 6");

        // 7th card → intervening-if true.
        FillGraveyard(alice, 1);
        attack.CanBePutOnStack().Should().BeTrue(
            "intervening-if becomes true at 7 cards in graveyard");
    }

    [Fact]
    public void AttackTrigger_ExilesRandomGraveyardCard_AndGrantsPlayThisTurn()
    {
        var alice = new Player("Alice", 20);
        var tersa = OnBattlefield(alice);
        FillGraveyard(alice, 7);

        var attack = SingleAttack(tersa);
        attack.Resolve();

        // One graveyard card exiled (7 → 6).
        alice.Zones.Graveyard.GetCards().Should().HaveCount(6,
            "exactly one card is exiled at random from the graveyard");
        alice.Zones.Exile.GetCards().Should().HaveCount(1,
            "the exiled card moves to the exile zone");

        var exiled = alice.Zones.Exile.GetCards().OfType<Card>().Single();
        // "You may play that card this turn" — the runtime exile-cast grant
        // (CR 118.9) nominates the controller as the allowed caster.
        exiled.RuntimeExileCastAllowedCaster.Should().BeSameAs(alice,
            "the exiled card may be played by Tersa's controller this turn");
    }

    [Fact]
    public void AttackTrigger_NoOp_WhenFewerThanSevenInGraveyardAtResolution()
    {
        var alice = new Player("Alice", 20);
        var tersa = OnBattlefield(alice);
        FillGraveyard(alice, 5);

        var attack = SingleAttack(tersa);
        attack.Resolve();

        alice.Zones.Exile.GetCards().Should().BeEmpty(
            "intervening-if re-checked on resolution (CR 603.4): < 7 → no exile");
        alice.Zones.Graveyard.GetCards().Should().HaveCount(5, "graveyard untouched");
    }

    [Fact]
    public void AttackTrigger_FiresOnlyWhenTersaIsAmongAttackers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var tersa = OnBattlefield(alice);
        var attack = SingleAttack(tersa);

        // Some OTHER creature attacks (not Tersa) → trigger must not match.
        var other = MakeNonland(alice, "OtherAttacker");
        var otherCombat = new Majik.Core.Combat.Combat(alice, bob);
        otherCombat.AddAttacker(new Attacker(other, bob));
        attack.IsTriggered(new AttackersDeclaredEvent(otherCombat))
            .Should().BeFalse("trigger fires only when Tersa herself attacks");

        // Tersa attacks → trigger matches.
        var tersaCombat = new Majik.Core.Combat.Combat(alice, bob);
        tersaCombat.AddAttacker(new Attacker(tersa, bob));
        attack.IsTriggered(new AttackersDeclaredEvent(tersaCombat))
            .Should().BeTrue("trigger fires when Tersa attacks");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature OnBattlefield(Player owner)
    {
        var tersa = TersaLightshatterFactory.Create(owner);
        tersa.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(tersa);
        return tersa;
    }

    private static TriggeredAbility SingleEtb(Creature tersa) =>
        tersa.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield) && t.InterveningIf == null);

    private static TriggeredAbility SingleAttack(Creature tersa) =>
        tersa.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

    private static void FillGraveyard(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = MakeNonland(owner, $"Grave{owner.Zones.Graveyard.GetCards().Count()}");
            owner.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
    }
}

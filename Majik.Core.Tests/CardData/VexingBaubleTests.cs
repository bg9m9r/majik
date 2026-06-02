using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VexingBaubleFactory"/>.
///
/// Covers:
/// - Card identity (name, Artifact type)
/// - Owner and controller assignment
/// - Activated ability shape: ManaCostCost({1}) + Tap + Sacrifice
/// - Draw effect: moves top library card to hand
/// - Draw effect: no-op on empty library
/// - Free-spell counter trigger: counters any player's 0-mana cast
///   (including the controller's own); ignores mana-paid casts.
/// </summary>
public class VexingBaubleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_IsArtifact()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void VexingBauble_NameIsCorrect()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.Name.Should().Be("Vexing Bauble");
    }

    [Fact]
    public void VexingBauble_OwnerAndControllerAreSet()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.Owner.Should().BeSameAs(_alice);
        bauble.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VexingBauble_HasNoManaAbilities()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Vexing Bauble produces no mana");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {1}, {T}, Sacrifice: Draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_HasExactlyOneActivatedAbility()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasExactlyThreeCosts()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3,
            "ManaCostCost({1}) + Tap + Sacrifice");
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasManaCostCostOf1()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.Generic.Should().Be(1, "the {1} component");
        manaCost.White.Should().Be(0);
        manaCost.Blue.Should().Be(0);
        manaCost.Black.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasTapCost()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the {T} cost");
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasSacrificeCost()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the Sacrifice cost");
    }

    // -----------------------------------------------------------------------
    // Draw effect execution
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_DrawEffect_MovesTopLibraryCardToHand()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "draw effect moves the top library card to hand");
        alice.Zones.Library.GetCards().Should().NotContain(topCard,
            "card is removed from the library");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void VexingBauble_DrawEffect_OnlyMovesTopCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        var second = new Card("Second", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "only the top card is drawn");
        alice.Zones.Library.GetCards().Should().Contain(second,
            "second card is unaffected");
    }

    [Fact]
    public void VexingBauble_DrawEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        // Library intentionally empty

        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("drawing from an empty library is a no-op; SBAs handle loss");
    }

    [Fact]
    public void VexingBauble_DrawAbility_ResolvesWithoutThrowing()
    {
        var alice = new Player("Alice", 20);
        var card = new Card("Some Card", "");
        alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ability.Resolve();

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Free-spell counter trigger
    //   "Whenever a player casts a spell, if no mana was spent to cast it,
    //    counter that spell." (CR 603.1 / CR 118)
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_HasExactlyOneTriggeredAbility()
    {
        var bauble = (Artifact)NamedCardFactory.Create("Vexing Bauble", _alice);

        bauble.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the free-spell counter trigger");
    }

    private (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers, Artifact bauble)
        WireBaubleOnBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var bauble = VexingBaubleFactory.Create(_alice, triggers, stack);
        bauble.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bauble);
        return (bus, stack, triggers, bauble);
    }

    [Fact]
    public void FreeSpellCounter_CountersOpponentFreeCast()
    {
        var (bus, stack, triggers, _) = WireBaubleOnBattlefield();

        // Bob (opponent) casts a 0-mana spell.
        var memnite = new Card("Memnite", "{0}");
        memnite.SetOwner(_bob);
        memnite.SetZone(ZoneType.Stack);
        var freeSpell = new Majik.Core.Spells.Spell(memnite, _bob) { WasFreeCast = true };
        stack.Push(freeSpell);

        bus.Publish(new SpellCastEvent(freeSpell));
        triggers.PendingCount.Should().Be(1, "a player cast a free spell — counter trigger fires");

        triggers.PutPendingTriggersOnStack(_alice);
        var trigger = stack.Pop()!;
        trigger.Resolve();

        stack.GetAll().Should().NotContain(freeSpell, "the free spell was countered (CR 701.5)");
        memnite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FreeSpellCounter_CountersControllersOwnFreeCast()
    {
        var (bus, stack, triggers, _) = WireBaubleOnBattlefield();

        // Alice (the Bauble's controller) casts her OWN free spell. Unlike
        // Boromir ("an opponent"), Vexing Bauble watches "a player" — it
        // counters its controller's free casts too (CR 102).
        var ornithopter = new Card("Ornithopter", "{0}");
        ornithopter.SetOwner(_alice);
        ornithopter.SetZone(ZoneType.Stack);
        var ownFree = new Majik.Core.Spells.Spell(ornithopter, _alice) { WasFreeCast = true };
        stack.Push(ownFree);

        bus.Publish(new SpellCastEvent(ownFree));
        triggers.PendingCount.Should().Be(1,
            "'whenever a player casts' includes the controller — own free cast is countered too");

        triggers.PutPendingTriggersOnStack(_alice);
        var trigger = stack.Pop()!;
        trigger.Resolve();

        stack.GetAll().Should().NotContain(ownFree, "the controller's own free spell is countered");
        ornithopter.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FreeSpellCounter_IgnoresManaPaidCast()
    {
        var (bus, stack, triggers, _) = WireBaubleOnBattlefield();

        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        var paidSpell = new Majik.Core.Spells.Spell(bolt, _bob); // WasFreeCast defaults false
        stack.Push(paidSpell);

        bus.Publish(new SpellCastEvent(paidSpell));

        triggers.PendingCount.Should().Be(0, "mana was paid — Vexing Bauble does not counter");
        stack.GetAll().Should().Contain(paidSpell);
    }
}

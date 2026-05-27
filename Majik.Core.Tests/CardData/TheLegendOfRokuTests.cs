using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for The Legend of Roku // Avatar Roku (TLA, {2}{R}{R}). Transforming
/// Saga (same shape as Fable of the Mirror-Breaker).
///
/// Front — Enchantment — Saga:
///   I   — Exile the top three cards of your library. Until the end of your
///         next turn, you may play those cards.
///   II  — Add one mana of any color.
///   III — Exile this Saga, then return it transformed (Avatar Roku).
/// Back — Avatar Roku, Legendary Creature — Avatar 4/4:
///   Firebending 4 (Whenever this creature attacks, add {R}{R}{R}{R}. This
///   mana lasts until end of combat.)
///   "{8}: Create a 4/4 red Dragon creature token with flying and
///    firebending 4."
/// </summary>
public class TheLegendOfRokuTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public TheLegendOfRokuTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Roku_IsRedEnchantmentSaga_AtCost2RR()
    {
        var roku = TheLegendOfRokuFactory.Create(_alice);

        roku.Name.Should().Be("The Legend of Roku");
        roku.ManaCost.Should().Be("{2}{R}{R}");
        roku.HasType(CardType.Enchantment).Should().BeTrue();
        roku.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(roku)
            .Should().Contain(ManaColor.Red);
        roku.Owner.Should().BeSameAs(_alice);
        roku.Controller.Should().BeSameAs(_alice);
        roku.SagaState.Should().NotBeNull("the Saga binder must attach a SagaState");
        roku.SagaState!.FinalChapter.Should().Be(3);
        roku.MdfcState.Should().NotBeNull();
        roku.MdfcState!.IsBackFace.Should().BeFalse("front face on entry");
        roku.MdfcState.BackFaceName.Should().Be("Avatar Roku");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Roku()
    {
        var roku = NamedCardFactory.Create("The Legend of Roku", _alice);

        roku.Should().BeOfType<Enchantment>();
        roku.Name.Should().Be("The Legend of Roku");
        roku.HasSubtype(CardSubtype.Saga).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Chapter I — exile top 3, may play until end of next turn
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterI_ExilesTopThree_AndGrantsExileCastToController()
    {
        var roku = TheLegendOfRokuFactory.Create(_alice, _zones, _bus, triggers: null);
        _zones.MoveCard(roku, ZoneType.Library, ZoneType.Battlefield, _alice);

        var top = new Card[5];
        for (var i = 0; i < 5; i++)
        {
            top[i] = new Card($"l{i}", "{1}{R}");
            top[i].SetOwner(_alice);
            _alice.Zones.Library.AddCard(top[i]);
        }

        roku.SagaState!.AdvanceAndChapter(); // lore 1 → chapter I

        // Top three exiled.
        var exiled = _alice.Zones.Exile.GetCards().OfType<Card>().ToList();
        exiled.Should().HaveCount(3);
        _alice.Zones.Library.GetCards().Count().Should().Be(2);

        // Each exiled card carries a runtime exile-cast grant for Alice.
        foreach (var c in exiled)
        {
            c.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
                "controller may play the exiled cards");
            c.RuntimeExileCastCost.Should().NotBeNull();
        }
    }

    [Fact]
    public void ChapterI_ExileCastGrant_ClearsAtEndOfControllersNextTurn()
    {
        var roku = TheLegendOfRokuFactory.Create(_alice, _zones, _bus, triggers: null);
        _zones.MoveCard(roku, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"l{i}", "{R}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        roku.SagaState!.AdvanceAndChapter(); // chapter I exiles + grants
        var exiled = _alice.Zones.Exile.GetCards().OfType<Card>().ToList();
        exiled.Should().OnlyContain(c => c.RuntimeExileCastAllowedCaster == _alice);

        // First Cleanup belongs to the CURRENT turn — grant survives (Saga
        // resolves on the controller's turn).
        _bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        exiled.Should().OnlyContain(c => c.RuntimeExileCastAllowedCaster == _alice,
            "the grant survives until end of the controller's NEXT turn");

        // Second Cleanup = the controller's next turn → grant clears.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        exiled.Should().OnlyContain(c => c.RuntimeExileCastAllowedCaster == null,
            "the may-play window ends at end of the controller's next turn");
    }

    // -----------------------------------------------------------------------
    // Chapter II — add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterII_AddsOneManaOfChosenColor()
    {
        // Scripted color choice: white.
        var roku = TheLegendOfRokuFactory.Create(
            _alice, _zones, _bus, triggers: null,
            colorChoice: () => ManaColor.White);
        _zones.MoveCard(roku, ZoneType.Library, ZoneType.Battlefield, _alice);

        roku.SagaState!.AdvanceAndChapter(); // I (exile — empty library, no-op)
        roku.SagaState.AdvanceAndChapter();  // II → add one white mana

        _alice.ManaPool.White.Should().Be(1, "chapter II adds one mana of the chosen color");
        _alice.ManaPool.Total.Should().Be(1, "exactly one mana is added");
    }

    [Fact]
    public void ChapterII_DefaultColorIsRed()
    {
        var roku = TheLegendOfRokuFactory.Create(_alice, _zones, _bus, triggers: null);
        _zones.MoveCard(roku, ZoneType.Library, ZoneType.Battlefield, _alice);

        roku.SagaState!.AdvanceAndChapter(); // I
        roku.SagaState.AdvanceAndChapter();  // II

        _alice.ManaPool.Red.Should().Be(1, "default color is red");
        _alice.ManaPool.Total.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Chapter III — transform into Avatar Roku
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterIII_TransformsToAvatarRoku()
    {
        var roku = TheLegendOfRokuFactory.Create(_alice, _zones, _bus, triggers: null);
        _zones.MoveCard(roku, ZoneType.Library, ZoneType.Battlefield, _alice);

        roku.SagaState!.AdvanceAndChapter(); // I
        roku.SagaState.AdvanceAndChapter();  // II
        roku.SagaState.AdvanceAndChapter();  // III → transform

        // The Saga (front) is gone from the battlefield (exiled).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(roku);
        roku.Zone.Should().Be(ZoneType.Exile);

        // The transformed permanent — Avatar Roku — is on the battlefield as a
        // 4/4 Legendary Creature — Avatar.
        var avatar = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Avatar Roku");

        avatar.Should().NotBeNull("chapter III returns the Saga transformed");
        avatar!.BasePower.Should().Be(4);
        avatar.BaseToughness.Should().Be(4);
        avatar.HasType(CardType.Creature).Should().BeTrue();
        avatar.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        avatar.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        avatar.HasSubtype(CardSubtype.Saga).Should().BeFalse("Saga is gone");
        avatar.SagaState.Should().BeNull("transformed permanent is no longer a Saga");
        avatar.MdfcState.Should().NotBeNull();
        avatar.MdfcState!.IsBackFace.Should().BeTrue("transformed onto the back face");
        avatar.MdfcState.ActiveFaceName.Should().Be("Avatar Roku");
    }

    // -----------------------------------------------------------------------
    // Back face — Avatar Roku identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AvatarRoku_IsFourFourLegendaryAvatarCreature()
    {
        var avatar = AvatarRokuFactory.Create(_alice);

        avatar.Name.Should().Be("Avatar Roku");
        avatar.BasePower.Should().Be(4);
        avatar.BaseToughness.Should().Be(4);
        avatar.HasType(CardType.Creature).Should().BeTrue();
        avatar.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        avatar.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(avatar)
            .Should().Contain(ManaColor.Red);
        avatar.MdfcState.Should().NotBeNull();
        avatar.MdfcState!.IsBackFace.Should().BeTrue("back face only exists transformed");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AvatarRoku()
    {
        var avatar = NamedCardFactory.Create("Avatar Roku", _alice);

        avatar.Should().BeOfType<Creature>();
        avatar.Name.Should().Be("Avatar Roku");
        avatar.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Firebending 4 — attack adds {R}{R}{R}{R} until end of combat
    // -----------------------------------------------------------------------

    [Fact]
    public void AvatarRoku_Attacking_AddsFourRedMana()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var avatar = AvatarRokuFactory.Create(_alice, _zones, _bus, triggers);
        _zones.MoveCard(avatar, ZoneType.Library, ZoneType.Battlefield, _alice);

        _alice.ManaPool.Red.Should().Be(0);

        // Avatar attacks → Firebending 4 fires.
        _bus.Publish(new CreatureAttacksEvent(avatar, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        _alice.ManaPool.Red.Should().Be(4, "Firebending 4 adds {R}{R}{R}{R}");
    }

    [Fact]
    public void AvatarRoku_Firebending_ManaEmptiesAtEndOfCombat()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var avatar = AvatarRokuFactory.Create(_alice, _zones, _bus, triggers);
        _zones.MoveCard(avatar, ZoneType.Library, ZoneType.Battlefield, _alice);

        _bus.Publish(new CreatureAttacksEvent(avatar, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);
        _alice.ManaPool.Red.Should().Be(4);

        // End of combat empties the firebending mana (CR 500.4 — "lasts until
        // end of combat").
        _bus.Publish(new StepStartedEvent(PhaseStateType.EndOfCombat, _alice));

        _alice.ManaPool.Red.Should().Be(0, "the firebending mana lasts only until end of combat");
    }

    // -----------------------------------------------------------------------
    // Back face — {8}: create a 4/4 red Dragon with flying + firebending 4
    // -----------------------------------------------------------------------

    [Fact]
    public void AvatarRoku_HasEightManaActivatedAbility()
    {
        var avatar = AvatarRokuFactory.Create(_alice);

        var activated = avatar.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle("Avatar Roku has the {8} Dragon-token ability");
        activated[0].Costs.OfType<Majik.Core.Costs.ManaCostCost>().Should()
            .ContainSingle("the printed activation costs {8}");
    }

    [Fact]
    public void AvatarRoku_Activate_CreatesFourFourRedDragonWithFlyingAndFirebending()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var avatar = AvatarRokuFactory.Create(_alice, _zones, _bus, triggers);
        _zones.MoveCard(avatar, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = avatar.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        var dragons = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken && c.HasSubtype(CardSubtype.Dragon))
            .ToList();
        dragons.Should().ContainSingle();
        var dragon = dragons[0];
        dragon.BasePower.Should().Be(4);
        dragon.BaseToughness.Should().Be(4);
        Majik.Core.Cards.CardColors.GetColors(dragon).Should().Contain(ManaColor.Red);
        dragon.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");

        // The Dragon token also has firebending 4 — attacking adds 4 red mana.
        _alice.ManaPool.Red.Should().Be(0);
        _bus.Publish(new CreatureAttacksEvent(dragon, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);
        _alice.ManaPool.Red.Should().Be(4, "the Dragon token has firebending 4");
    }
}

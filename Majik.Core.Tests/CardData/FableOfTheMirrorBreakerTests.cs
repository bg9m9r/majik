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
/// Tests for Fable of the Mirror-Breaker // Reflection of Kiki-Jiki
/// (Kamigawa: Neon Dynasty, {2}{R}). Transforming Saga.
///
/// Front — Enchantment — Saga:
///   I   — Create a 2/2 red Goblin Shaman creature token with "Whenever
///         this creature attacks, create a Treasure token."
///   II  — You may discard up to two cards, then draw that many cards.
///   III — Exile this Saga, then return it transformed (Reflection of
///         Kiki-Jiki).
/// Back — Reflection of Kiki-Jiki, Enchantment Creature — Goblin Shaman 2/2:
///   "{1}, {T}: Create a token that's a copy of another target
///    nonlegendary creature you control. That token has haste. Sacrifice
///    it at the beginning of the next end step."
/// (Scryfall-confirmed: activation cost is {1} generic only, no red pip.)
/// </summary>
public class FableOfTheMirrorBreakerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public FableOfTheMirrorBreakerTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Fable_IsRedEnchantmentSaga_AtCost2R()
    {
        var fable = FableOfTheMirrorBreakerFactory.Create(_alice);

        fable.Name.Should().Be("Fable of the Mirror-Breaker");
        fable.ManaCost.Should().Be("{2}{R}");
        fable.HasType(CardType.Enchantment).Should().BeTrue();
        fable.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(fable)
            .Should().Contain(ManaColor.Red);
        fable.Owner.Should().BeSameAs(_alice);
        fable.Controller.Should().BeSameAs(_alice);
        fable.SagaState.Should().NotBeNull("the Saga binder must attach a SagaState");
        fable.SagaState!.FinalChapter.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Fable()
    {
        var fable = NamedCardFactory.Create("Fable of the Mirror-Breaker", _alice);

        fable.Should().BeOfType<Enchantment>();
        fable.Name.Should().Be("Fable of the Mirror-Breaker");
        fable.HasSubtype(CardSubtype.Saga).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Chapter I — create a 2/2 red Goblin with "attacks → Treasure"
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterI_CreatesTwoTwoRedGoblinToken()
    {
        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, eventBus: _bus, triggers: null);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);

        fable.SagaState!.AdvanceAndChapter(); // lore 1 → chapter I

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();

        tokens.Should().ContainSingle();
        var goblin = tokens[0];
        goblin.BasePower.Should().Be(2);
        goblin.BaseToughness.Should().Be(2);
        goblin.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        goblin.HasSubtype(CardSubtype.Shaman).Should()
            .BeTrue("Scryfall: chapter I creates a 2/2 red Goblin Shaman token");
        Majik.Core.Cards.CardColors.GetColors(goblin)
            .Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void ChapterI_Goblin_AttackTrigger_CreatesTreasure()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, _bus, triggers);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);

        // CR 714.2b — chapter I is enqueued as a triggered ability. Drain it
        // onto the stack and resolve to spawn the Goblin token.
        fable.SagaState!.AdvanceAndChapter();
        var resolver = new StackResolver(_bus, _zones);
        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        var goblin = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken);

        // No Treasures yet.
        _alice.Zones.Battlefield.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Treasure)).Should().BeFalse();

        // Goblin attacks → the embedded trigger fires.
        _bus.Publish(new CreatureAttacksEvent(goblin, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure))
            .Should().Be(1, "the Goblin's attack trigger creates a Treasure token");
    }

    // -----------------------------------------------------------------------
    // Chapter II — you may discard up to two, then draw that many
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterII_DiscardTwo_DrawsTwo()
    {
        // Scripted "you may" choice: discard 2.
        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, _bus, triggers: null, rummageChoice: () => 2);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 3; i++)
            _alice.Zones.Hand.AddCard(new Card($"h{i}", ""));
        for (var i = 0; i < 5; i++)
            _alice.Zones.Library.AddCard(new Card($"l{i}", ""));

        fable.SagaState!.AdvanceAndChapter(); // I (token, no hand impact)
        fable.SagaState.AdvanceAndChapter();  // II

        _alice.Zones.Graveyard.GetCards().Count().Should().Be(2);
        _alice.Zones.Hand.GetCards().Count().Should().Be(3, "discarded 2, drew 2");
        _alice.Zones.Library.GetCards().Count().Should().Be(3);
    }

    [Fact]
    public void ChapterII_DiscardZero_DrawsZero()
    {
        // Scripted "you may" opt-out: discard 0.
        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, _bus, triggers: null, rummageChoice: () => 0);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 3; i++)
            _alice.Zones.Hand.AddCard(new Card($"h{i}", ""));
        for (var i = 0; i < 5; i++)
            _alice.Zones.Library.AddCard(new Card($"l{i}", ""));

        fable.SagaState!.AdvanceAndChapter(); // I
        fable.SagaState.AdvanceAndChapter();  // II

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("discarded nothing");
        _alice.Zones.Hand.GetCards().Count().Should().Be(3, "no discard, no draw");
        _alice.Zones.Library.GetCards().Count().Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Chapter III — transform into Reflection of Kiki-Jiki
    // -----------------------------------------------------------------------

    [Fact]
    public void ChapterIII_TransformsToReflectionOfKikiJiki()
    {
        var fable = FableOfTheMirrorBreakerFactory.Create(
            _alice, _zones, _bus, triggers: null);
        _zones.MoveCard(fable, ZoneType.Library, ZoneType.Battlefield, _alice);

        fable.SagaState!.AdvanceAndChapter(); // I
        fable.SagaState.AdvanceAndChapter();  // II
        fable.SagaState.AdvanceAndChapter();  // III → transform

        // The Saga (Fable front) is gone from the battlefield (exiled).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fable);
        fable.Zone.Should().Be(ZoneType.Exile);

        // The transformed permanent — Reflection of Kiki-Jiki — is on the
        // battlefield as a 2/2 Enchantment Creature — Goblin Shaman.
        var reflection = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Reflection of Kiki-Jiki");

        reflection.Should().NotBeNull("chapter III returns the Saga transformed");
        reflection!.BasePower.Should().Be(2);
        reflection.BaseToughness.Should().Be(2);
        reflection.HasType(CardType.Creature).Should().BeTrue();
        reflection.HasType(CardType.Enchantment).Should().BeTrue();
        reflection.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        reflection.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        reflection.HasSubtype(CardSubtype.Saga).Should().BeFalse("Saga is gone");
        reflection.SagaState.Should().BeNull("transformed permanent is no longer a Saga");
        reflection.MdfcState.Should().NotBeNull();
        reflection.MdfcState!.IsBackFace.Should().BeTrue("transformed onto the back face");
        reflection.MdfcState.ActiveFaceName.Should().Be("Reflection of Kiki-Jiki");
    }

    // -----------------------------------------------------------------------
    // Back face — Reflection of Kiki-Jiki {1},{T} copy ability
    // (Scryfall-confirmed: activation is {1} generic + {T}, NOT {1}{R} + {T})
    // -----------------------------------------------------------------------

    [Fact]
    public void Reflection_IsTwoTwoEnchantmentCreatureGoblinShaman_NonLegendary()
    {
        var reflection = ReflectionOfKikiJikiFactory.Create(_alice);

        reflection.Name.Should().Be("Reflection of Kiki-Jiki");
        reflection.BasePower.Should().Be(2);
        reflection.BaseToughness.Should().Be(2);
        reflection.HasType(CardType.Creature).Should().BeTrue();
        reflection.HasType(CardType.Enchantment).Should().BeTrue();
        reflection.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        reflection.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        reflection.HasSupertype(CardSupertype.Legendary).Should()
            .BeFalse("Reflection of Kiki-Jiki is NOT legendary");
    }

    [Fact]
    public void Reflection_HasActivatedAbilityWithManaAndTapCost_SingleTarget()
    {
        var reflection = ReflectionOfKikiJikiFactory.Create(_alice);

        var activated = reflection.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle();
        var ability = activated[0];
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);

        // Cost includes a mana component {1} (one generic, NO red pip) plus a tap.
        // Scryfall oracle: "{1}, {T}: Create a token..." — not {1}{R}.
        var manaCost = ability.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
            .Should().ContainSingle("the printed activation costs {1} generic + {T}").Which;
        manaCost.Cost.TotalValue.Should().Be(1,
            "activation mana value is 1 (one generic), not 2 (which {1}{R} would give)");
        manaCost.Cost.Red.Should().Be(0,
            "Scryfall confirms no red pip in Reflection's activation cost");
    }

    [Fact]
    public void Reflection_PrintedManaCost_IsOneGeneric_NotRedPipGated()
    {
        // Scryfall: Reflection of Kiki-Jiki activates for {1},{T} (no red pip).
        ReflectionOfKikiJikiFactory.PrintedManaCost.Should().Be("{1}",
            "Scryfall oracle text: activation cost is {1},{T}, no red pip");
    }

    [Fact]
    public void Reflection_Activate_CreatesHasteTokenCopy_OfTargetCreature()
    {
        var reflection = ReflectionOfKikiJikiFactory.Create(
            _alice, _zones, triggers: null);
        _zones.MoveCard(reflection, ZoneType.Library, ZoneType.Battlefield, _alice);
        reflection.HasSummoningSickness = false;

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = reflection.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)bears } });
        ability.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        tokens.Should().ContainSingle();
        var token = tokens[0];
        token.Name.Should().Be("Grizzly Bears");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Haste");
        token.HasSummoningSickness.Should().BeFalse();
    }

    [Fact]
    public void Reflection_Activate_RegistersDelayedEndStepSacrifice_ForSpawnedToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var reflection = ReflectionOfKikiJikiFactory.Create(_alice, _zones, triggers);
        _zones.MoveCard(reflection, ZoneType.Library, ZoneType.Battlefield, _alice);
        reflection.HasSummoningSickness = false;

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = reflection.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)bears } });
        ability.Resolve();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Single(c => c.IsToken);
        token.Zone.Should().Be(ZoneType.Battlefield);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        token.Zone.Should().Be(ZoneType.Graveyard,
            "Reflection sacrifices the token at the next end step");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(token);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Reflection()
    {
        var reflection = NamedCardFactory.Create("Reflection of Kiki-Jiki", _alice);

        reflection.Should().BeOfType<Creature>();
        reflection.Name.Should().Be("Reflection of Kiki-Jiki");
        reflection.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }
}

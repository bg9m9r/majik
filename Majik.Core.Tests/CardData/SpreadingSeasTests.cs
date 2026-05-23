using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Spreading Seas — Enchantment — Aura {1}{U}.
///
///   "Enchant land.
///    When this Aura enters, draw a card.
///    Enchanted land is an Island and has '{T}: Add {U}'."
///
/// Validates the Layer 4 retype via
/// <see cref="AttachedAuraRetypeStaticEffect"/> + PR #155's
/// <see cref="EffectiveManaAbilities"/>. Cast-time targeting is deferred;
/// tests manually <see cref="Permanent.AttachTo"/> the bearer after
/// putting both permanents onto the battlefield.
/// </summary>
public class SpreadingSeasTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public SpreadingSeasTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SpreadingSeas_IsAura_AtCost1U()
    {
        var ss = SpreadingSeasFactory.Create(_alice);

        ss.Name.Should().Be("Spreading Seas");
        ss.HasType(CardType.Enchantment).Should().BeTrue();
        ss.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        ss.IsAura.Should().BeTrue();
        ss.ManaCost.Should().Be("{1}{U}");
        ss.Owner.Should().BeSameAs(_alice);
        ss.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpreadingSeas()
    {
        var ss = NamedCardFactory.Create("Spreading Seas", _alice);

        ss.Should().BeOfType<Enchantment>();
        ss.Name.Should().Be("Spreading Seas");
        ss.ManaCost.Should().Be("{1}{U}");
        ss.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // End-to-end: Spreading Seas retypes its enchanted land to Island
    // -----------------------------------------------------------------------

    /// <summary>
    /// Forest (Basic supertype, printed {T}: Add {G}). Once Spreading Seas
    /// is attached, the Forest is retyped to Island — CR 305.6 drops the
    /// printed {G} mana ability and EffectiveManaAbilities derives
    /// {T}: Add {U}. The Basic supertype is preserved because the layer
    /// effect only rewrites the subtype slot.
    /// </summary>
    [Fact]
    public void Attached_To_Forest_RetypeIsland_TapsForBlue()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Baseline: a printed Forest taps for {G}.
        var baseline = EffectiveManaAbilities.For(forest, _effects, _alice);
        baseline.Should().ContainSingle().Which.ManaGenerated.Green.Should().Be(1);

        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);
        // Bypass cast-time targeting (not yet wired): attach BEFORE moving
        // the aura onto the battlefield so once it enters, the lifecycle
        // sync sees AttachedTo populated. Either order is supported (the
        // scope predicate is evaluated lazily), but doing it pre-move
        // exercises the simpler control flow.
        ss.AttachTo(forest);
        _zones.MoveCard(ss, ZoneType.Library, ZoneType.Battlefield, _alice);

        var attached = EffectiveManaAbilities.For(forest, _effects, _alice);
        attached.Should().HaveCount(1, "CR 305.6 strips printed {G} and adds {U}");
        attached[0].ManaGenerated.Blue.Should().Be(1);
        attached[0].ManaGenerated.Green.Should().Be(0);
    }

    /// <summary>
    /// Lifecycle: when Spreading Seas leaves the battlefield, the layer
    /// effect is unregistered. The Forest's subtypes revert to printed,
    /// so its printed {T}: Add {G} mana ability applies again.
    /// </summary>
    [Fact]
    public void LeavesBattlefield_RestoresPrintedMana()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);
        ss.AttachTo(forest);
        _zones.MoveCard(ss, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Sanity: Forest is currently an Island, tapping for {U}.
        EffectiveManaAbilities.For(forest, _effects, _alice)
            .Should().ContainSingle().Which.ManaGenerated.Blue.Should().Be(1);

        // Send Spreading Seas to the graveyard — the
        // AttachedAuraRetypeStaticEffect should unregister on its
        // CardMovedEvent.
        _zones.MoveCard(ss, ZoneType.Battlefield, ZoneType.Graveyard);

        var restored = EffectiveManaAbilities.For(forest, _effects, _alice);
        restored.Should().ContainSingle("layer effect dropped → printed abilities apply")
            .Which.ManaGenerated.Green.Should().Be(1);
    }

    /// <summary>
    /// Structural test for the ETB draw trigger. End-to-end firing
    /// requires a live <see cref="TriggerManager"/>; here we just verify
    /// the <see cref="TriggeredAbility"/> is wired on the card's
    /// <see cref="Card.Abilities"/> collection with a condition that
    /// matches "this card moved to battlefield".
    /// </summary>
    [Fact]
    public void Etb_DrawTriggerIsWired()
    {
        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);

        var triggers = ss.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().ContainSingle("Spreading Seas has one ETB draw trigger");

        var trigger = triggers[0];
        trigger.Controller.Should().BeSameAs(_alice);

        // Fire a synthetic CardMovedEvent for this card hitting the
        // battlefield — IsTriggered should be true.
        // Library → Battlefield via ZoneService to set zone state too.
        _zones.MoveCard(ss, ZoneType.Library, ZoneType.Battlefield, _alice);
        var etb = new CardMovedEvent(ss, ZoneType.Library, ZoneType.Battlefield);
        trigger.IsTriggered(etb).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Cast-time targeting + auto-attach on resolution (CR 303.4f / 601.2c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end cast flow: agent picks the target Forest at cast time;
    /// on resolution, the aura attaches to the Forest BEFORE the engine
    /// moves it to the battlefield. The Layer 4 retype then activates as
    /// the aura ETBs, so the Forest taps for {U}.
    /// </summary>
    [Fact]
    public async Task SpreadingSeas_CastFlow_AutoAttachesToTargetLand_AndRetypes()
    {
        // Arrange: Forest on battlefield, Spreading Seas in hand.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);
        _alice.Zones.Library.RemoveCard(ss);
        _alice.Zones.Hand.AddCard(ss);
        ss.SetZone(ZoneType.Hand);

        var stack = new Majik.Core.Stack.Stack(_bus);
        var castFlow = new SpellCastFlow(stack, _zones, _bus);
        var resolver = new StackResolver(_bus, _zones);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { forest });
        agent.QueueMana(ManaPayment.Empty);

        var def = SpreadingSeasFactory.BuildSpellDefinition(
            ss, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());
        var ctx = new GameContext(_alice, new[] { _alice }, _alice, 1,
            PhaseStateType.Main, stack);

        // Act: cast (push to stack) + resolve (auto-attach + ETB move).
        var spell = await castFlow.CastAsync(_alice, ss, def, agent, ctx);
        resolver.ResolveTop(stack);

        // Assert: aura attached + on battlefield + Forest taps for {U}.
        ss.Zone.Should().Be(ZoneType.Battlefield,
            "StackResolver moves the permanent to the battlefield on resolution");
        ss.AttachedTo.Should().BeSameAs(forest,
            "CR 303.4f — Aura enters the battlefield attached to its chosen target");
        forest.Attachments.Should().Contain(ss);

        var attached = EffectiveManaAbilities.For(forest, _effects, _alice);
        attached.Should().HaveCount(1,
            "CR 305.6 — Forest's printed {G} dropped; new Island subtype derives {U}");
        attached[0].ManaGenerated.Blue.Should().Be(1);
        attached[0].ManaGenerated.Green.Should().Be(0);
    }

    /// <summary>
    /// No legal target → cast aborts. With no Lands on the battlefield,
    /// the candidate pool is empty; the scripted agent returns an empty
    /// target list, which violates the MinTargets=1 contract and
    /// SpellCastFlow throws an InvalidOperationException (CR 601.2c).
    /// </summary>
    [Fact]
    public async Task SpreadingSeas_NoLegalTarget_CannotBeCast()
    {
        // Arrange: no lands in play.
        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);
        _alice.Zones.Library.RemoveCard(ss);
        _alice.Zones.Hand.AddCard(ss);
        ss.SetZone(ZoneType.Hand);

        var stack = new Majik.Core.Stack.Stack(_bus);
        var castFlow = new SpellCastFlow(stack, _zones, _bus);
        var agent = new ScriptedAgent();
        // Agent has no land to pick → returns an empty list when prompted.
        agent.QueueTargets(Array.Empty<object>());
        agent.QueueMana(ManaPayment.Empty);

        var def = SpreadingSeasFactory.BuildSpellDefinition(
            ss, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());
        var ctx = new GameContext(_alice, new[] { _alice }, _alice, 1,
            PhaseStateType.Main, stack);

        // Act & Assert: cast is rejected; nothing reaches the stack.
        Func<Task> cast = async () =>
            await castFlow.CastAsync(_alice, ss, def, agent, ctx);

        await cast.Should().ThrowAsync<InvalidOperationException>(
                "no legal target → CR 601.2c illegal cast")
            .WithMessage("*target*");
        stack.Count.Should().Be(0);
        ss.Zone.Should().Be(ZoneType.Hand,
            "rejected cast must not mutate the card's zone");
    }

    /// <summary>
    /// Bonus: end-to-end ETB-draw assertion. Cast Spreading Seas via the
    /// real cast flow with a live TriggerManager + StackResolver loop; the
    /// ETB trigger should fire when the aura enters the battlefield, and
    /// the controller's hand size should go up by one (net effect after
    /// paying the Spreading Seas card itself out of hand).
    /// </summary>
    [Fact]
    public async Task SpreadingSeas_CastFlow_TriggersEtbDraw_HandSizePlusOne()
    {
        // Arrange: Forest on battlefield, Spreading Seas in hand, a stub
        // card in the library to draw.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ss = SpreadingSeasFactory.Create(_alice, _effects, _bus);
        _alice.Zones.Library.RemoveCard(ss);
        _alice.Zones.Hand.AddCard(ss);
        ss.SetZone(ZoneType.Hand);

        var libraryCard = new Card("Mox Pearl", "");
        libraryCard.SetOwner(_alice);
        libraryCard.SetController(_alice);
        _alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        var stack = new Majik.Core.Stack.Stack(_bus);
        // Live TriggerManager wired to the bus — without it the ETB draw
        // trigger never gets put on the stack. Construction auto-subscribes
        // to the bus; CardMovedEvent → auto-bind + EvaluateTriggers.
        var triggerManager = new TriggerManager(stack, _bus);
        triggerManager.BindCard(ss);

        var castFlow = new SpellCastFlow(stack, _zones, _bus);
        var resolver = new StackResolver(_bus, _zones);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { forest });
        agent.QueueMana(ManaPayment.Empty);

        var def = SpreadingSeasFactory.BuildSpellDefinition(
            ss, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());
        var ctx = new GameContext(_alice, new[] { _alice }, _alice, 1,
            PhaseStateType.Main, stack);

        var handCountBeforeResolve = _alice.Zones.Hand.GetCards().Count();

        // Act: cast (push spell to stack) + resolve loop.
        await castFlow.CastAsync(_alice, ss, def, agent, ctx);
        resolver.ResolveTop(stack); // resolves the Spreading Seas spell
        // ETB-fired trigger is now in TriggerManager's pending queue —
        // drain to stack (CR 603.3b) and resolve.
        triggerManager.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        // Assert: aura attached, ETB draw fired, stub card now in hand.
        ss.AttachedTo.Should().BeSameAs(forest);
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            "ETB-on-aura trigger drew the top of library");
        // Hand started with 0 (Spreading Seas was in hand and got cast,
        // i.e. moved to stack pre-resolution), then we drew 1 → hand has 1.
        _alice.Zones.Hand.GetCards().Count().Should().Be(handCountBeforeResolve,
            "Spreading Seas left hand on cast; ETB draw replaced it");
    }
}

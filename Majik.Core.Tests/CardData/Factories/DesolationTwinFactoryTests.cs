using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DesolationTwinFactory"/> (Battle for Zendikar,
/// {10}).
///
/// Creature — Eldrazi 10/10 (colorless). Oracle text (verified against
/// Scryfall):
///   "When you cast this spell, create a 10/10 colorless Eldrazi creature
///    token."
///
/// Covers:
///   - Identity (Eldrazi 10/10 at {10}, colorless, owner / controller, MV 10).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - One cast trigger attached structurally on the shape-only path.
///   - Cast trigger is over <see cref="SpellCastEvent"/>, active on the Stack,
///     with no external targets (it just creates a token).
///   - Self-cast match: matches a SpellCastEvent for THIS card; ignores other
///     spells.
///   - Effect mints ONE 10/10 colorless Eldrazi token (no abilities) under the
///     caster's control.
/// </summary>
[Trait("Color", "C")]
public class DesolationTwinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DesolationTwin_Identity()
    {
        var c = DesolationTwinFactory.Create(_alice);

        c.Name.Should().Be("Desolation Twin");
        c.ManaCost.Should().Be("{10}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.BasePower.Should().Be(10);
        c.BaseToughness.Should().Be(10);
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(10,
            "{10} has mana value 10 (CR 202.3)");
        CardColors.GetColors(c).Should().BeEmpty(
            "{10} is an all-generic cost — Desolation Twin is colorless (CR 105.2)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DesolationTwin_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Desolation Twin", _alice);

        c.Should().BeOfType<Creature>("Desolation Twin is a Creature instance");
        c.Name.Should().Be("Desolation Twin");
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Cast trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void DesolationTwin_HasOneCastTrigger()
    {
        var c = DesolationTwinFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the 'when you cast this spell' token trigger is attached");
    }

    [Fact]
    public void DesolationTwin_CastTrigger_OverSpellCast_ActiveOnStack_NoTargets()
    {
        var c = DesolationTwinFactory.Create(_alice);
        var trigger = GetCastTrigger(c);

        trigger.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>(
            "the trigger fires off a SpellCastEvent (CR 603.2e)");
        trigger.ActiveZones.Should().Contain(ZoneType.Stack,
            "a 'when you cast this spell' trigger fires while the spell is on " +
            "the stack (CR 603.2e), not on ETB");
        trigger.TargetRequests.Should().BeEmpty(
            "the token is simply created — no external target is chosen");
    }

    [Fact]
    public void DesolationTwin_CastTrigger_MatchesSelfCast()
    {
        var c = DesolationTwinFactory.Create(_alice);
        var trigger = GetCastTrigger(c);

        var spell = new StubSpell(c, _alice);
        var ev = new SpellCastEvent(spell);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue();
    }

    [Fact]
    public void DesolationTwin_CastTrigger_DoesNotMatchOtherSpellCast()
    {
        var c = DesolationTwinFactory.Create(_alice);
        var trigger = GetCastTrigger(c);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var spell = new StubSpell(other, _alice);
        var ev = new SpellCastEvent(spell);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Cast trigger effect
    // -----------------------------------------------------------------------

    [Fact]
    public void CastEffect_CreatesOne10x10ColorlessEldraziToken_UnderController()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = DesolationTwinFactory.Create(_alice, zones, triggers);

        // Fire the self-cast condition so the captured caster is set, then run
        // the resolve effect (mirrors the live cast-trigger flow).
        var trigger = GetCastTrigger(card);
        var ev = new SpellCastEvent(new StubSpell(card, _alice));
        trigger.Condition.Matches(ev, trigger).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken)
            .ToList();

        tokens.Should().HaveCount(1,
            "the cast trigger creates exactly one Eldrazi token (CR 111.10)");

        var token = tokens[0];
        token.Name.Should().Be("Eldrazi");
        token.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        token.BasePower.Should().Be(10);
        token.BaseToughness.Should().Be(10);
        CardColors.GetColors(token).Should().BeEmpty(
            "the token is colorless (CR 111.4)");
        token.Abilities.Should().BeEmpty(
            "the printed token is a vanilla 10/10 colorless Eldrazi — no abilities");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility GetCastTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    private sealed class StubSpell : ISpell
    {
        public StubSpell(ICard card, Player controller)
        {
            Card = card;
            Controller = controller;
        }

        public ICard Card { get; }
        public Player Controller { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public IReadOnlyList<ITarget> Targets { get; } = Array.Empty<ITarget>();
        public IReadOnlyList<ICost> Costs { get; } = Array.Empty<ICost>();
        public bool CannotBeCountered => false;
        public void Resolve() { }
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ProwessTests
{
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NoncreatureSpellCast_BoostsProwess_UntilEndOfTurn()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var monk = new Creature("Monastery Swiftspear", "R", 1, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };
        var prowess = ProwessFactory.Build(monk, _effects);
        monk.AddAbility(prowess);
        triggers.BindCard(monk);

        // Alice casts an instant.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bolt, _alice);
        _bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        var trig = stack.Pop()!;
        trig.Resolve();

        monk.Power.Should().Be(2);

        // After end-of-turn cleanup, prowess fades.
        _effects.ExpireEndOfTurn();
        monk.Power.Should().Be(1);
    }

    [Fact]
    public void CreatureSpellCast_DoesNotTriggerProwess()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var monk = new Creature("Monastery Swiftspear", "R", 1, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };
        var prowess = ProwessFactory.Build(monk, _effects);
        monk.AddAbility(prowess);
        triggers.BindCard(monk);

        var bears = new Creature("Bears", "1G", 2, 2) { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bears, _alice);
        _bus.Publish(new SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0);
        monk.Power.Should().Be(1);
    }
}

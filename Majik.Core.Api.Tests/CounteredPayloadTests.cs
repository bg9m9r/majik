using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 701.6 / 608.2b — the SpellCountered payload says what countered the
/// spell (its stack entry id and display name) and why; the
/// TriggeredAbilityCountered payload names the removed firing's entry id.
/// </summary>
public class CounteredPayloadTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Spell NewSpell(string name, Player owner) =>
        new(new Instant(name, "U") { Owner = owner, Controller = owner, Zone = ZoneType.Stack }, owner);

    [Fact]
    public void CounteredByASpell_NamesThatSpellsStackEntry()
    {
        var bolt = NewSpell("Lightning Bolt", _bob);
        var counterspell = NewSpell("Counterspell", _alice);
        var entry = Guid.NewGuid();
        var e = new SpellCounteredEvent(bolt, _alice)
        {
            CounteringObject = counterspell, CounteringEntryId = entry,
        };

        var p = EventPayloadBuilder.Build(e);
        p.GetProperty("counteringStackId").GetGuid().Should().Be(entry);
        p.GetProperty("counteringName").GetString().Should().Be("Counterspell");
        p.GetProperty("reason").GetString().Should().Be("CounteredByEffect");
    }

    [Fact]
    public void CounteredByTheRules_HasNoSource_AndSaysAllTargetsIllegal()
    {
        var e = new SpellCounteredEvent(NewSpell("Lightning Bolt", _bob), null)
        {
            Reason = SpellCounterReason.AllTargetsIllegal,
        };

        var p = EventPayloadBuilder.Build(e);
        p.GetProperty("counteringStackId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        p.GetProperty("counteringName").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        p.GetProperty("reason").GetString().Should().Be("AllTargetsIllegal");
    }

    [Fact]
    public void CounteredByAFaceDownPermanentsAbility_IsNotNamedToAnOpponent()
    {
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_alice); goyf.SetController(_alice);
        _alice.Zones.Library.AddCard(goyf); goyf.SetZone(ZoneType.Library);
        var wrapper = Majik.Core.Effects.ManifestEffect.Resolve(_alice)!;
        var ability = new ActivatedAbility(wrapper, _alice);
        var e = new SpellCounteredEvent(NewSpell("Lightning Bolt", _bob), _alice)
        {
            CounteringObject = ability, CounteringEntryId = ability.Id,
        };

        EventPayloadBuilder.RequiresPerViewerMasking(e).Should().BeTrue();
        EventPayloadBuilder.Build(e, _bob).GetProperty("counteringName").GetString().Should().Be(EventPayloadBuilder.FaceDownName);
        EventPayloadBuilder.Build(e, _alice).GetProperty("counteringName").GetString().Should().Be("Tarmogoyf");
    }

    [Fact]
    public void ARemovedTriggerFiring_IsNamedByItsEntryId()
    {
        var source = new Creature("Pinger", "R", 1, 1) { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var trigger = new TriggeredAbility(source, _bob, Triggers.OnEnterBattlefieldSelf(source));
        var entry = Guid.NewGuid();
        var e = new TriggeredAbilityCounteredEvent(trigger, TriggeredAbilityCounteredEvent.InterveningIfFailed) { EntryId = entry };

        var p = EventPayloadBuilder.Build(e);
        p.GetProperty("stackId").GetGuid().Should().Be(entry);
        p.GetProperty("reason").GetString().Should().Be(TriggeredAbilityCounteredEvent.InterveningIfFailed);
    }
}

using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class EntersAsCopyTests
{
    private static Creature MakeCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Theory]
    [InlineData("You may have this creature enter as a copy of any creature on the battlefield.")]
    [InlineData("You may have this creature enter as a copy of a creature you control.")]
    [InlineData("You may have this creature enter as a copy of any creature card in a graveyard.")]
    public void Binder_RegistersReplacement_OnMatchingOracle(string oracle)
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();
        var card = MakeCreature(new Player("o", 20), "Clone", 0, 0);
        var entity = new CardEntity { Name = "Clone", OracleText = oracle };

        EntersAsCopyBinder.Bind(card, entity, bus, effects).Should().BeTrue();
    }

    [Fact]
    public void Binder_NoMatch_OnUnrelatedOracle()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();
        var card = MakeCreature(new Player("o", 20), "Bear", 2, 2);
        var entity = new CardEntity { Name = "Bear", OracleText = "Flying. First strike." };

        EntersAsCopyBinder.Bind(card, entity, bus, effects).Should().BeFalse();
    }

    [Fact]
    public void ETB_ResolvesToCopyOfFirstBattlefieldCreature()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();
        var owner = new Player("Alice", 20);

        // Source already on battlefield.
        var src = MakeCreature(owner, "Grizzly Bears", 2, 2);
        owner.Zones.Battlefield.AddCard(src);
        src.SetZone(ZoneType.Battlefield);

        // Clone-like entering from hand.
        var copier = MakeCreature(owner, "Clone", 0, 0);
        copier.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(copier);
        copier.ActiveEffects = effects;

        EntersAsCopyBinder.Bind(copier,
            new CardEntity { Name = "Clone",
                OracleText = "You may have this creature enter as a copy of any creature on the battlefield." },
            bus, effects).Should().BeTrue();

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(copier, ZoneType.Hand, ZoneType.Battlefield, owner);

        // Copier now has source's printed P/T via CopyEffect at Layer 1.
        copier.Power.Should().Be(2);
        copier.Toughness.Should().Be(2);
    }

    [Fact]
    public void ETB_NoCandidates_LeavesPrintedCharacteristics()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();
        var owner = new Player("Alice", 20);

        var copier = MakeCreature(owner, "Clone", 0, 0);
        copier.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(copier);
        copier.ActiveEffects = effects;

        EntersAsCopyBinder.Bind(copier,
            new CardEntity { Name = "Clone",
                OracleText = "You may have this creature enter as a copy of any creature on the battlefield." },
            bus, effects).Should().BeTrue();

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(copier, ZoneType.Hand, ZoneType.Battlefield, owner);

        // No valid source → copier keeps printed (0/0) and dies to SBA at
        // next check. Here we just verify P/T weren't mutated.
        copier.Power.Should().Be(0);
        copier.Toughness.Should().Be(0);
    }
}

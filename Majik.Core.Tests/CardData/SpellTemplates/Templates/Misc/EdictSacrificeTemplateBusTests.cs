using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Misc;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Misc;

/// <summary>
/// Production-path coverage for thread-bus-into-edict-sacrifice-closures: the
/// edict spell templates that bind Diabolic Edict / Cruel Edict / "each
/// opponent sacrifices a creature" oracle text at cast time now publish a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) for each forced
/// sacrifice when the <see cref="SpellBindContext"/> carries an event bus, so
/// "whenever an opponent sacrifices …" aristocrat payoffs (It That Betrays,
/// Mayhem Devil, Writhing Chrysalis) fire on the live cast path — not just the
/// named-factory test path.
/// </summary>
[Trait("Color", "B")]
public class EdictSacrificeTemplateBusTests
{
    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static SpellBindContext Ctx(string name, string text, Player caster, IEventBus? bus)
        => new(new CardEntity { Name = name, OracleText = text },
            caster, o => o, Effects: null, Stack: null,
            Replacements: null, Triggers: null, EventBus: bus);

    [Fact]
    public void TargetPlayerSacrifices_WithEventBus_PublishesEvent_CreditingTarget()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bear = SeedCreature(bob, "Runeclaw Bear");

        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(ev => sacrificed.Add(ev));

        var template = new TargetPlayerSacrificesCreatureTemplate();
        var spell = template.Rehydrate(EmptyParams.Instance,
            Ctx("Diabolic Edict",
                "Target player sacrifices a creature of their choice.", alice, bus));

        // Resolve: the chosen target is Bob (resolver is identity).
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bob } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell.EffectFactory(chosen)) fx.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        sacrificed.Should().ContainSingle()
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == bear && ev.SacrificingPlayer == bob && !ev.WasToken);
    }

    [Fact]
    public void EachOpponentSacrifices_WithEventBus_PublishesEvent_PerOpponent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var bobBear = SeedCreature(bob, "Runeclaw Bear");
        var carolBear = SeedCreature(carol, "Grizzly Bears");
        // Alice (the caster) is excluded from "each opponent".
        var aliceBear = SeedCreature(alice, "Centaur Courser");

        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(ev => sacrificed.Add(ev));

        var template = new EachOpponentSacrificesCreatureTemplate();
        var spell = template.Rehydrate(EmptyParams.Instance,
            Ctx("Innocent Blood-ish",
                "Each opponent sacrifices a creature.", alice, bus));

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { alice, bob, carol });
        foreach (var fx in spell.EffectFactory(chosen)) fx.Execute();

        // Only the two opponents sacrificed; Alice's creature is untouched.
        aliceBear.Zone.Should().Be(ZoneType.Battlefield);
        sacrificed.Should().HaveCount(2);
        sacrificed.Should().Contain(ev =>
            ev.SacrificedCard == bobBear && ev.SacrificingPlayer == bob);
        sacrificed.Should().Contain(ev =>
            ev.SacrificedCard == carolBear && ev.SacrificingPlayer == carol);
    }

    [Fact]
    public void TargetPlayerSacrifices_NoEventBus_StillSacrifices_PublishesNothing()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bear = SeedCreature(bob, "Runeclaw Bear");

        var template = new TargetPlayerSacrificesCreatureTemplate();
        var spell = template.Rehydrate(EmptyParams.Instance,
            Ctx("Diabolic Edict",
                "Target player sacrifices a creature of their choice.", alice, bus: null));

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bob } },
            Mana: ManaPayment.Empty);
        foreach (var fx in spell.EffectFactory(chosen)) fx.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
    }
}

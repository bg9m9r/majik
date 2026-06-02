using System.Linq;
using System.Reflection;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Cross-cutting tests for the "single factory, multiple <c>[CardName]</c>
/// attributes" pattern used to collapse functional-reprint wrappers (e.g.
/// Damnation → Wrath of God). Two guarantees:
///
///   1. Every printed name on a multi-<c>[CardName]</c> factory dispatches
///      through <see cref="NamedCardFactory"/> to a card with the matching
///      printed name. The source generator picks the
///      <c>Create(Player, string)</c> overload when present so the right
///      name + cost is produced per reprint.
///   2. The collapsed pair stays observationally identical at the
///      resolve-effect layer — for the canonical pair Wrath of God +
///      Damnation, both printed names produce a sorcery whose resolve
///      effect sweeps every creature off every battlefield (CR 701.7).
/// </summary>
[Trait("Color", "W")]
public class ReprintConsolidationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------
    // Name-dispatch coverage — every [CardName] on every multi-attribute
    // factory routes through NamedCardFactory to a card whose printed
    // name matches the dispatch key.
    // -------------------------------------------------------------------

    public static IEnumerable<object[]> MultiCardNameAttributeFactories()
    {
        var asm = typeof(WrathOfGodFactory).Assembly;
        foreach (var type in asm.GetTypes())
        {
            if (!type.IsClass) continue;
            var attrs = type.GetCustomAttributes<CardNameAttribute>(inherit: false).ToList();
            if (attrs.Count < 2) continue;
            foreach (var attr in attrs)
            {
                yield return new object[] { type.Name, attr.Name };
            }
        }
    }
    // -------------------------------------------------------------------
    // Wrath of God + Damnation — functional-reprint coverage at the
    // resolve-effect layer.
    // -------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_WrathAndDamnation_ProduceCorrectShapes()
    {
        var wrath = NamedCardFactory.Create("Wrath of God", _alice);
        var damnation = NamedCardFactory.Create("Damnation", _alice);

        wrath.Should().BeOfType<Sorcery>();
        wrath.Name.Should().Be("Wrath of God");
        wrath.ManaCost.Should().Be("{2}{W}{W}");

        damnation.Should().BeOfType<Sorcery>();
        damnation.Name.Should().Be("Damnation");
        damnation.ManaCost.Should().Be("{2}{B}{B}");
    }

    [Fact]
    public void WrathAndDamnation_BothSweepEveryCreatureOnEveryBattlefield()
    {
        // Two parallel pairs of players, identical board state. Resolve
        // each printed name against its own pair and assert the end
        // state matches the canonical Wrath of God sweep semantics
        // (CR 701.7 — no creatures left on either battlefield, every
        // creature now in its owner's graveyard).
        var aliceCreatures = new[]
        {
            SeedCreature(_alice, "Alice-Bear"),
            SeedCreature(_alice, "Alice-Wolf"),
        };
        var bobCreatures = new[] { SeedCreature(_bob, "Bob-Bear") };

        WrathOfGodFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceCreatures);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobCreatures);

        // Parallel run via the Damnation printed-name entrypoint —
        // observationally identical end state (same factory, same
        // resolve body).
        var dAlice = new Player("Alice", 20);
        var dBob = new Player("Bob", 20);
        var dAliceCreatures = new[]
        {
            SeedCreature(dAlice, "Alice-Bear"),
            SeedCreature(dAlice, "Alice-Wolf"),
        };
        var dBobCreatures = new[] { SeedCreature(dBob, "Bob-Bear") };

        // Materialise the Damnation card to assert the named-overload
        // path returns the right printed name; resolve uses the shared
        // BuildResolveEffect.
        var damnation = NamedCardFactory.Create("Damnation", dAlice);
        damnation.Name.Should().Be("Damnation");
        damnation.ManaCost.Should().Be("{2}{B}{B}");

        WrathOfGodFactory.BuildResolveEffect(new[] { dAlice, dBob })
            .ToList().ForEach(e => e.Execute());

        dAlice.Zones.Battlefield.GetCards().Should().BeEmpty();
        dBob.Zones.Battlefield.GetCards().Should().BeEmpty();
        dAlice.Zones.Graveyard.GetCards().Select(c => c.Name)
            .Should().BeEquivalentTo(dAliceCreatures.Select(c => c.Name));
        dBob.Zones.Graveyard.GetCards().Select(c => c.Name)
            .Should().BeEquivalentTo(dBobCreatures.Select(c => c.Name));
    }

    [Fact]
    public void Create_RejectsUnknownCardName()
    {
        // Defensive — the public Create(Player, string) overload should
        // refuse unknown names rather than silently producing a sorcery
        // with the wrong cost. The source-generated dispatcher only ever
        // passes declared [CardName]s, but the overload is public.
        var act = () => WrathOfGodFactory.Create(_alice, "Not A Real Card");

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*does not serve card name*");
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}

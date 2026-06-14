using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Twincast ({U}{U}) / Reverberate ({R}{R}) — "Copy target instant or sorcery
/// spell. You may choose new targets for the copy." (CR 707.10).
///
/// Verifies the deferral pay-down end to end through the PROD binder path
/// (<see cref="OracleSpellBinder.Bind"/> → <c>CopyTargetSpellTemplate</c> →
/// <c>SpellCopier.PushCopyOfTopSpell</c>): the copy is a DISTINCT stack object
/// (CR 706.10a) that resolves first and then ceases to exist (CR 707.10c),
/// leaving the original spell on the stack.
/// </summary>
public class TwincastReverberateTests
{
    private const string CopyOracle =
        "Copy target instant or sorcery spell. You may choose new targets for the copy.";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Majik.Core.Spells.Spell BuildTargetInstant(
        Player controller, List<Player> hits)
    {
        var bolt = new Instant("Lightning Bolt", "R") { Owner = controller };
        bolt.SetZone(ZoneType.Stack);
        // A targeted-verb analogue: reads its target off ChosenTargets[0][0]
        // (exactly how every JSON targeted verb resolves) and records the hit.
        var effect = new Effect("bolt", ctx =>
        {
            if (ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0
                && ctx.ChosenTargets[0][0] is Player p)
                hits.Add(p);
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
        var spell = new Majik.Core.Spells.Spell(
            bolt, controller, effects: new IEffect[] { effect });
        spell.ChosenTargets.Add(_alice); // Bolt aimed at Alice
        return spell;
    }

    [Fact]
    public void Twincast_BindsViaCopyTemplate_OneTargetSpellRequest()
    {
        var stack = new Majik.Core.Stack.Stack();
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Twincast", ManaCost = "{U}{U}", OracleText = CopyOracle },
            _alice, raw => raw, stack);

        def.Should().NotBeNull("Twincast's oracle text must match the copy template");
        def!.TargetRequests.Should().HaveCount(1, "Twincast targets one instant/sorcery spell");
        def.TargetRequests[0].Description.Should().Contain("instant or sorcery spell");
    }

    [Fact]
    public void Twincast_CopiesTargetSpell_AsDistinctStackObject_ThatResolvesAndCeasesToExist()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts Lightning Bolt onto the stack.
        var hits = new List<Player>();
        var bobBolt = BuildTargetInstant(_bob, hits);
        stack.Push(bobBolt);
        stack.Count.Should().Be(1);

        // Alice resolves Twincast targeting Bob's Bolt.
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Twincast", ManaCost = "{U}{U}", OracleText = CopyOracle },
            _alice, raw => raw, stack);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobBolt } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // CR 706.10a — a distinct copy is now on the stack ABOVE Bob's Bolt.
        stack.Count.Should().Be(2, "Twincast put a distinct copy on the stack");
        var copy = stack.Top.Should().BeOfType<Majik.Core.Spells.Spell>().Subject;
        copy.IsCopy.Should().BeTrue();
        copy.Should().NotBeSameAs(bobBolt, "the copy is its own stack object");
        copy.Controller.Should().BeSameAs(_alice,
            "CR 707.10 — the copy is controlled by Twincast's controller, not Bob");

        // Resolve the copy — it runs the Bolt's effect once (against the
        // original's chosen target, CR 707.10a) then ceases to exist (CR 707.10c).
        new Majik.Core.Services.StackResolver().ResolveTop(stack);

        hits.Should().ContainSingle("the copy resolved the Bolt effect once");
        stack.Count.Should().Be(1, "the copy ceased to exist; Bob's Bolt remains");
        stack.Top.Should().BeSameAs(bobBolt, "the original spell is left where it was (CR 707.10)");
        bobBolt.Card.Zone.Should().NotBe(ZoneType.Graveyard,
            "resolving the copy must not drag the original card to a zone");
    }

    [Fact]
    public void Reverberate_BindsViaSameTemplate_AndCopies()
    {
        var stack = new Majik.Core.Stack.Stack();
        var hits = new List<Player>();
        var bobBolt = BuildTargetInstant(_bob, hits);
        stack.Push(bobBolt);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Reverberate", ManaCost = "{R}{R}", OracleText = CopyOracle },
            _alice, raw => raw, stack);
        def.Should().NotBeNull("Reverberate shares Twincast's copy template");

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobBolt } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.Count.Should().Be(2);
        ((Majik.Core.Spells.Spell)stack.Top!).IsCopy.Should().BeTrue();
    }

    [Fact]
    public void Twincast_TargetLeftStack_Fizzles_NoCopy()
    {
        var stack = new Majik.Core.Stack.Stack();

        // The "target" is a permanent, not a spell on the stack → CR 608.2b
        // the copy effect does nothing.
        var notASpell = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Twincast", ManaCost = "{U}{U}", OracleText = CopyOracle },
            _alice, raw => raw, stack);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)notASpell } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.Count.Should().Be(0, "a non-instant/sorcery target produces no copy (CR 608.2b)");
    }

    [Fact]
    public void Factories_FlipImplemented_AndCarryPrintedCost()
    {
        var twincast = TwincastFactory.Create(_alice);
        twincast.Name.Should().Be("Twincast");
        twincast.HasType(Majik.Core.Cards.Types.CardType.Instant).Should().BeTrue();

        var reverberate = ReverberateFactory.Create(_alice);
        reverberate.Name.Should().Be("Reverberate");
        reverberate.HasType(Majik.Core.Cards.Types.CardType.Instant).Should().BeTrue();

        ImplementedCardNames.Contains("Twincast").Should().BeTrue();
        ImplementedCardNames.Contains("Reverberate").Should().BeTrue();
    }
}

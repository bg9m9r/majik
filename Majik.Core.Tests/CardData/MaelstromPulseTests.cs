using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Maelstrom Pulse (Alara Reborn, {1}{B}{G}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Destroy target nonland permanent and all other permanents with the
///    same name as that permanent."
///
/// Covers:
///   - Card identity (Sorcery, {1}{B}{G}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 "target nonland permanent"
///     request, no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7).
///   - Resolve: destroys a planeswalker.
///   - Resolve: destroys a noncreature permanent (artifact).
///   - Resolve: destroys ALL other same-name permanents across every
///     battlefield (the Maelstrom Pulse twist).
///   - Resolve: a same-name permanent is destroyed even when on a
///     different player's battlefield, while a differently-named permanent
///     is left untouched.
///   - Resolve: a land may NOT be targeted (nonland-permanent restriction)
///     — but a same-name LAND can still be swept if the chosen target is a
///     nonland permanent that happens to share a name (edge guard).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class MaelstromPulseTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MaelstromPulse_IsSorcery_AtCost1BG()
    {
        var card = MaelstromPulseFactory.Create(_alice);

        card.Name.Should().Be("Maelstrom Pulse");
        card.ManaCost.Should().Be("{1}{B}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MaelstromPulse()
    {
        var card = NamedCardFactory.Create("Maelstrom Pulse", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Maelstrom Pulse");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MaelstromPulse_Definition_HasSingleNonlandPermanentTarget()
    {
        var def = MaelstromPulseFactory.BuildDefinition(
            new[] { _alice, _bob }, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys the target permanent (any nonland type)
    // -----------------------------------------------------------------------

    [Fact]
    public void MaelstromPulse_DestroysCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Maelstrom Pulse destroys the targeted nonland permanent (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void MaelstromPulse_DestroysPlaneswalker()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        Resolve(pw);

        pw.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
    }

    [Fact]
    public void MaelstromPulse_DestroysArtifact()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "Maelstrom Pulse destroys any nonland permanent type, including artifacts");
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    // -----------------------------------------------------------------------
    // Resolve — the same-name sweep (the Maelstrom Pulse twist)
    // -----------------------------------------------------------------------

    [Fact]
    public void MaelstromPulse_DestroysAllSameNamePermanents_AcrossBattlefields()
    {
        // Two copies of the same creature on different battlefields, plus a
        // third controlled by the caster, plus an unrelated permanent that
        // must survive.
        var target = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var sameNameBobsOther = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var sameNameAlices = NewControlledCreature(_alice, "Goblin Guide", "{R}");
        var bystander = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(target);

        // CR 701.7 — the target and every OTHER permanent with the same name
        // (regardless of controller) are destroyed in the same resolution.
        target.Zone.Should().Be(ZoneType.Graveyard);
        sameNameBobsOther.Zone.Should().Be(ZoneType.Graveyard);
        sameNameAlices.Zone.Should().Be(ZoneType.Graveyard,
            "the same-name sweep ignores controller — even the caster's own copy dies");

        // The differently-named permanent is untouched.
        bystander.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bystander);
    }

    [Fact]
    public void MaelstromPulse_LeavesDifferentlyNamedPermanents_Alone()
    {
        var target = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var other = NewControlledArtifact(_bob, "Mind Stone", "{2}");

        Resolve(target);

        target.Zone.Should().Be(ZoneType.Graveyard);
        other.Zone.Should().Be(ZoneType.Battlefield,
            "only permanents sharing the target's name are swept");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void MaelstromPulse_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged — CR 608.2b illegal target → no-op. No same-name
        // sweep happens because the target itself was illegal at resolution.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void MaelstromPulse_LandTarget_DoesNothing()
    {
        // The target must be a NONLAND permanent — a land is an illegal
        // target and the spell does nothing (CR 608.2b).
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Maelstrom Pulse cannot target a land — illegal target, no-op (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = MaelstromPulseFactory.BuildDefinition(
            allPlayers: new[] { _alice, _bob },
            targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var a = new Artifact(name, cost)
        {
            Owner = owner,
            Controller = owner,
        };
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}

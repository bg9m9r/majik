using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GoblinTrashmasterFactory"/> — Goblin Trashmaster
/// (Mercadian Masques, {2}{R}{R}). Creature — Goblin Warrior 3/3. Oracle
/// text (verified against Scryfall):
///   "Other Goblins you control get +1/+1.
///    Sacrifice a Goblin: Destroy target artifact."
///
/// Covers:
///   - Card identity (Creature, {2}{R}{R}, 3/3, Goblin + Warrior subtypes,
///     red, owner / controller) sourced from the embedded JSON definition.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Lord static (CR 613.7c): other controller-Goblins get +1/+1.
///   - includeSelf: false — Trashmaster doesn't self-pump.
///   - Non-Goblins / opponent Goblins not pumped.
///   - LTB lifts the bonus.
///   - "Sacrifice a Goblin: Destroy target artifact" activated ability shape:
///     1..1 "target artifact" request, BotIntent.Removal.
///   - Resolve: sacrifices a controller Goblin + destroys the chosen artifact.
///   - Resolve: illegal target (creature) → sacrifice still happens, no destroy.
///   - Resolve: no Goblin to sacrifice → clean no-op (cost can't be paid).
/// </summary>
public class GoblinTrashmasterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void GoblinTrashmaster_Identity_Creature_GoblinWarrior_3_3_At2RR()
    {
        var card = GoblinTrashmasterFactory.Create(_alice);

        card.Name.Should().Be("Goblin Trashmaster");
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinTrashmaster_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Goblin Trashmaster", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Trashmaster");
        card.ManaCost.Should().Be("{2}{R}{R}");
        ((Creature)card).HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // ── Lord static — "Other Goblins you control get +1/+1" ─────────────

    [Fact]
    public void GoblinTrashmaster_BuffsOtherControllerGoblin_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.Zone = ZoneType.Battlefield;
        trashmaster.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2,
            "other Goblins controlled by Trashmaster's controller get +1/+1 (1 → 2 power).");
        otherGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GoblinTrashmaster_DoesNotSelfPump()
    {
        // includeSelf: false — "Other Goblins" excludes Trashmaster itself.
        var svc = new ContinuousEffectsService();

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.Zone = ZoneType.Battlefield;
        trashmaster.ActiveEffects = svc;

        trashmaster.GetPower().Should().Be(3, "Trashmaster doesn't self-buff via 'Other Goblins'.");
        trashmaster.GetToughness().Should().Be(3);
    }

    [Fact]
    public void GoblinTrashmaster_DoesNotPump_NonGoblin()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.Zone = ZoneType.Battlefield;
        trashmaster.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Trashmaster only buffs Goblins.");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GoblinTrashmaster_DoesNotPump_OpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.Zone = ZoneType.Battlefield;
        trashmaster.ActiveEffects = svc;

        oppGoblin.GetPower().Should().Be(1,
            "Trashmaster's static is scoped to its controller's Goblins (CR 109.5 — 'you').");
        oppGoblin.GetToughness().Should().Be(1);
    }

    [Fact]
    public void GoblinTrashmaster_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.Zone = ZoneType.Battlefield;
        trashmaster.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2);

        trashmaster.SetZone(ZoneType.Graveyard);

        otherGoblin.GetPower().Should().Be(1, "bonus lifts on LTB");
        otherGoblin.GetToughness().Should().Be(1);
    }

    // ── Activated ability — structural ──────────────────────────────────

    [Fact]
    public void SacAbility_HasOneArtifactTarget_RemovalIntent()
    {
        var card = GoblinTrashmasterFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact");
    }

    // ── Activated ability — resolution ──────────────────────────────────

    [Fact]
    public void Resolve_SacrificesGoblin_AndDestroysChosenArtifact()
    {
        var svc = new ContinuousEffectsService();

        var artifact = new Artifact("Bob's Trinket", "{2}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var trashmaster = GoblinTrashmasterFactory.Create(_alice, svc);
        trashmaster.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(trashmaster);
        trashmaster.SetZone(ZoneType.Battlefield);

        var ability = trashmaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });
        foreach (var effect in ability.Effects) effect.Execute();

        fodder.Zone.Should().Be(ZoneType.Graveyard, "a Goblin was sacrificed to pay the cost.");
        artifact.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact is destroyed.");
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Resolve_IllegalTarget_SacrificesGoblin_NoDestroy()
    {
        // A creature is not an artifact — destroy is a no-op (CR 608.2b),
        // but the sacrifice cost was still paid.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var trashmaster = GoblinTrashmasterFactory.Create(_alice);
        trashmaster.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(trashmaster);
        trashmaster.SetZone(ZoneType.Battlefield);

        var ability = trashmaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in ability.Effects) effect.Execute();

        fodder.Zone.Should().Be(ZoneType.Graveyard);
        bear.Zone.Should().Be(ZoneType.Battlefield, "a creature is not a legal artifact target.");
    }

    [Fact]
    public void Resolve_NoGoblinToSacrifice_IsCleanNoOp()
    {
        var artifact = new Artifact("Bob's Trinket", "{2}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        // Trashmaster itself is a Goblin, but it is NOT on the battlefield in
        // this scenario — so there is no Goblin to sacrifice.
        var trashmaster = GoblinTrashmasterFactory.Create(_alice);
        trashmaster.SetController(_alice);

        var ability = trashmaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };
        act.Should().NotThrow();

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "no Goblin available to sacrifice → cost unpayable → effect does nothing.");
    }
}

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
/// Tests for Bloodchief's Thirst (Zendikar Rising, {B}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Kicker {2}{B} (You may pay an additional {2}{B} as you cast this spell.)
///    Destroy target creature or planeswalker with mana value 2 or less. If
///    this spell was kicked, instead destroy target creature or planeswalker."
///
/// Covers:
///   - Card identity (Sorcery, {B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 creature-or-PW target, no modes,
///     no variable X, BotIntent.Removal.
///   - Resolve unkicked: destroys a mana-value-2 creature (CR 701.7).
///   - Resolve unkicked: mana-value-3 creature survives — illegal target gate
///     (CR 608.2b / printed mana-value clause, Rule 202.3).
///   - Resolve kicked: destroys a mana-value-3 creature (kicker removes the
///     mana-value restriction, CR 702.33b).
///   - Resolve kicked: destroys a planeswalker (any mana value).
/// </summary>
public class BloodchiefsThirstTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodchiefsThirst_IsSorcery_AtCostB()
    {
        var card = BloodchiefsThirstFactory.Create(_alice);

        card.Name.Should().Be("Bloodchief's Thirst");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodchiefsThirst()
    {
        var card = NamedCardFactory.Create("Bloodchief's Thirst", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bloodchief's Thirst");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodchiefsThirst_Definition_HasSingleCreatureOrPlaneswalkerTarget()
    {
        var def = BloodchiefsThirstFactory.BuildSpellDefinition(wasKicked: false, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — unkicked (mana value 2 or less)
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodchiefsThirst_Unkicked_DestroysManaValue2Creature()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}"); // mv 2

        Resolve(creature, wasKicked: false);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "unkicked destroys a creature with mana value 2 or less (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void BloodchiefsThirst_Unkicked_ManaValue3Creature_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Watchwolf", "{1}{G}{W}"); // mv 3

        Resolve(creature, wasKicked: false);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "unkicked cannot destroy a creature with mana value greater than 2 (Rule 202.3 / CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
    }

    // -----------------------------------------------------------------------
    // Resolve — kicked (any mana value)
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodchiefsThirst_Kicked_DestroysManaValue3Creature()
    {
        var creature = NewControlledCreature(_bob, "Watchwolf", "{1}{G}{W}"); // mv 3

        Resolve(creature, wasKicked: true);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "kicked removes the mana-value restriction (CR 702.33b)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void BloodchiefsThirst_Kicked_DestroysPlaneswalker()
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

        Resolve(pw, wasKicked: true);

        pw.Zone.Should().Be(ZoneType.Graveyard,
            "kicked destroys the targeted planeswalker regardless of mana value (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
    }

    [Fact]
    public void BloodchiefsThirst_Unkicked_ArtifactTarget_DoesNothing()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact, wasKicked: false);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Bloodchief's Thirst can only destroy creatures or planeswalkers (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken, bool wasKicked)
    {
        var def = BloodchiefsThirstFactory.BuildSpellDefinition(wasKicked, targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

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
}

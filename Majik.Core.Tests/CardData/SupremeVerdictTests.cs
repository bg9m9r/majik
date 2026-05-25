using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Supreme Verdict (Return to Ravnica, {1}{W}{W}{U}, Sorcery).
///
/// Oracle: "This spell can't be countered. Destroy all creatures."
///
/// Coverage:
///   - Identity (name, type, cost) via factory + <see cref="NamedCardFactory"/>.
///   - Uncounterable <see cref="KeywordAbility"/> marker present (CR 701.5b —
///     <see cref="Majik.Core.Game.SpellCastFlow"/> reads this at cast time
///     and stamps <see cref="ISpell.CannotBeCountered"/> on the resolving
///     spell, vetoing counters via
///     <see cref="OracleSpellBinder.RemoveFromStack"/>).
///   - Sweep destroys every creature on every supplied player's
///     battlefield, both sides (CR 701.7).
///   - Indestructible (CR 702.12b) survives the sweep.
///   - Regeneration (CR 701.15) is honoured — Supreme Verdict does NOT
///     print the "can't be regenerated" rider, so an active regen shield
///     saves the creature (contrast Wrath of God / Damnation, which pass
///     <see cref="ZoneMoveReason.DestroyNoRegeneration"/> and bypass the
///     shield).
///   - Non-creature permanents survive the sweep.
/// </summary>
public class SupremeVerdictTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SupremeVerdict_IsSorcery_At1WWU()
    {
        var sv = SupremeVerdictFactory.Create(_alice);

        sv.Name.Should().Be("Supreme Verdict");
        sv.ManaCost.Should().Be("{1}{W}{W}{U}");
        sv.HasType(CardType.Sorcery).Should().BeTrue();
        sv.Owner.Should().BeSameAs(_alice);
        sv.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SupremeVerdict()
    {
        var card = NamedCardFactory.Create("Supreme Verdict", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Supreme Verdict");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}{W}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Uncounterable marker (CR 701.5b)
    // -----------------------------------------------------------------------

    [Fact]
    public void SupremeVerdict_HasUncounterableMarker()
    {
        var sv = SupremeVerdictFactory.Create(_alice);

        sv.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(SupremeVerdictFactory.UncounterableMarker,
                "CR 701.5b — SpellCastFlow reads the \"Uncounterable\" " +
                "marker at cast time to stamp Spell.CannotBeCountered");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_AttachesUncounterableMarker()
    {
        // The dispatcher path is what production hosts use; verify the
        // marker survives that path (the source-generated dispatch goes
        // through SupremeVerdictFactory.Create which AddAbility'd it).
        var card = NamedCardFactory.Create("Supreme Verdict", _alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(SupremeVerdictFactory.UncounterableMarker);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreaturesOnBothBattlefields_ToOwnerGraveyards()
    {
        var aliceCreatures = new[]
        {
            SeedCreature(_alice, "Alice-Bear"),
            SeedCreature(_alice, "Alice-Wolf"),
        };
        var bobCreatures = new[]
        {
            SeedCreature(_bob, "Bob-Bear"),
            SeedCreature(_bob, "Bob-Wolf"),
        };

        var effects = SupremeVerdictFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceCreatures);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobCreatures);

        foreach (var c in aliceCreatures) c.Zone.Should().Be(ZoneType.Graveyard);
        foreach (var c in bobCreatures) c.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_LeavesNonCreaturePermanentsAlone()
    {
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");
        var aliceLand = SeedLand(_alice, "Alice-Plains");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Aura");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Sol-Ring");

        var effects = SupremeVerdictFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceLand, aliceEnchantment, aliceArtifact });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { aliceCreature });
        aliceCreature.Zone.Should().Be(ZoneType.Graveyard);
        aliceLand.Zone.Should().Be(ZoneType.Battlefield);
        aliceEnchantment.Zone.Should().Be(ZoneType.Battlefield);
        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Indestructible / regeneration interaction (the SV / Wrath delta)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_IndestructibleCreature_SurvivesTheSweep()
    {
        var indestructible = SeedCreature(_alice, "Darksteel-Bear");
        indestructible.AddAbility(new KeywordAbility("Indestructible", indestructible, _alice));

        var mortal = SeedCreature(_alice, "Mortal-Bear");

        var effects = SupremeVerdictFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        // CR 702.12b — indestructible cancels the destroy.
        _alice.Zones.Battlefield.GetCards().Should().Contain(indestructible);
        indestructible.Zone.Should().Be(ZoneType.Battlefield);
        // Mortal creature still dies.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mortal);
    }

    [Fact]
    public void Resolve_RegenerationShield_SavesCreature()
    {
        // Supreme Verdict does NOT print "can't be regenerated" — a regen
        // shield on a creature should be consumed in place of the destroy
        // (CR 701.15c). This is the headline behavioural delta vs Wrath
        // of God / Damnation, which pass DestroyNoRegeneration.
        var protectedCreature = SeedCreature(_alice, "Regen-Bear");
        protectedCreature.AddRegenerationShield();

        var mortal = SeedCreature(_alice, "Mortal-Bear");

        var effects = SupremeVerdictFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        // CR 701.15c — regen consumed; creature stays on the battlefield.
        _alice.Zones.Battlefield.GetCards().Should().Contain(protectedCreature);
        protectedCreature.Zone.Should().Be(ZoneType.Battlefield);
        protectedCreature.HasRegenerationShield.Should().BeFalse(
            "the regeneration shield was consumed by the destroy attempt");
        // Mortal creature still dies.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mortal);
    }

    [Fact]
    public void Resolve_DistinctFromWrath_RegenSavesAgainstSupremeVerdict_NotWrath()
    {
        // Parallel-board regression: a regen-shielded creature on one
        // player must survive Supreme Verdict's sweep but die to Wrath
        // of God's "can't be regenerated" sweep. Documents the headline
        // CR 701.15 / printed-rider delta.

        // SV side
        var svPlayer = new Player("SV-Player", 20);
        var svRegen = SeedCreature(svPlayer, "Regen-Bear");
        svRegen.AddRegenerationShield();
        foreach (var e in SupremeVerdictFactory.BuildResolveEffect(new[] { svPlayer }))
            e.Execute();

        svPlayer.Zones.Battlefield.GetCards().Should().Contain(svRegen,
            "Supreme Verdict honours regeneration (CR 701.15c)");

        // Wrath side
        var wPlayer = new Player("Wrath-Player", 20);
        var wRegen = SeedCreature(wPlayer, "Regen-Bear");
        wRegen.AddRegenerationShield();
        foreach (var e in WrathOfGodFactory.BuildResolveEffect(new[] { wPlayer }))
            e.Execute();

        wPlayer.Zones.Battlefield.GetCards().Should().NotContain(wRegen,
            "Wrath of God's \"can't be regenerated\" rider bypasses the shield");
        wPlayer.Zones.Graveyard.GetCards().Should().Contain(wRegen);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Enchantment SeedEnchantment(Player owner, string name)
    {
        var e = new Enchantment(name, "");
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }

    private static Artifact SeedArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}

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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Ondu Inversion // Ondu Skyruins (Zendikar Rising).
///
/// Oracle text (verified against Scryfall):
///   Front — Ondu Inversion, Sorcery, {6}{W}{W}:
///     "Destroy all nonland permanents."
///   Back — Ondu Skyruins, Land:
///     "This land enters tapped."
///     "{T}: Add {W}."
///
/// The front face is an untargeted board wipe; the back face is an
/// unconditional enters-tapped mana land. MDFC cast-either-face wiring mirrors
/// <see cref="EmeriasCallFactory"/> (front carries a castable
/// <see cref="Majik.Core.CardData.MDFCs.MdfcFace.Land"/> back-face descriptor).
/// </summary>
[Trait("Color", "W")]
public class OnduInversionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Front face — Ondu Inversion identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OnduInversion_Identity_Sorcery_White6WW()
    {
        var inversion = OnduInversionFactory.Create(_alice);

        inversion.Name.Should().Be("Ondu Inversion");
        inversion.HasType(CardType.Sorcery).Should().BeTrue();
        inversion.ManaCost.Should().Be("{6}{W}{W}");
        inversion.ManaCostValue.TotalValue.Should().Be(8);
        CardColors.GetColors(inversion).Should().Contain(ManaColor.White);
        inversion.Owner.Should().BeSameAs(_alice);
        inversion.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OnduInversion_NamedCardFactory_Dispatch_ProducesSorcery()
    {
        var card = NamedCardFactory.Create("Ondu Inversion", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Ondu Inversion");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{6}{W}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OnduInversion_HasMdfcState_WithCastableLandBackFace()
    {
        var inversion = OnduInversionFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        inversion.MdfcState.Should().NotBeNull();
        inversion.MdfcState!.FrontFaceName.Should().Be("Ondu Inversion");
        inversion.MdfcState.BackFaceName.Should().Be("Ondu Skyruins");
        inversion.MdfcState.IsBackFace.Should().BeFalse("the sorcery is the front face");
        inversion.MdfcState.CastableBackFace.Should().NotBeNull();
        inversion.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        inversion.MdfcState.CastableBackFace.Name.Should().Be("Ondu Skyruins");
    }

    // -----------------------------------------------------------------------
    // Front face — resolve sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysAllNonlandPermanents_OnBothBattlefields()
    {
        // Alice: creature + enchantment + artifact + land.
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Aura");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Sol-Ring");
        var aliceLand = SeedLand(_alice, "Alice-Plains");
        // Bob: creature + land.
        var bobCreature = SeedCreature(_bob, "Bob-Wolf");
        var bobLand = SeedLand(_bob, "Bob-Forest");

        var effects = OnduInversionFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Every nonland permanent dies to its owner's graveyard (CR 701.7).
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceCreature, aliceEnchantment, aliceArtifact });
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(
            new[] { bobCreature });

        // Lands survive.
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(new[] { aliceLand });
        _bob.Zones.Battlefield.GetCards().Should().BeEquivalentTo(new[] { bobLand });
        aliceLand.Zone.Should().Be(ZoneType.Battlefield);
        bobLand.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = OnduInversionFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void SpellDefinition_NoModes_NoX_NoTargets()
    {
        var def = OnduInversionFactory.BuildSpellDefinition(new[] { _alice, _bob });

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Back face — Ondu Skyruins identity + mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void OnduSkyruins_Identity_Land_TapsForWhite_BackFace()
    {
        var skyruins = OnduSkyruinsFactory.Create(_alice);

        skyruins.Name.Should().Be("Ondu Skyruins");
        skyruins.HasType(CardType.Land).Should().BeTrue();
        skyruins.Owner.Should().BeSameAs(_alice);
        skyruins.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        skyruins.MdfcState.Should().NotBeNull();
        skyruins.MdfcState!.IsBackFace.Should().BeTrue();
        skyruins.MdfcState.ActiveFaceName.Should().Be("Ondu Skyruins");

        // {T}: Add {W} — single mana ability producing one white.
        skyruins.Abilities.OfType<ManaAbility>().Should().ContainSingle();
    }

    [Fact]
    public void OnduSkyruins_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Ondu Skyruins", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Ondu Skyruins");
    }

    [Fact]
    public void OnduSkyruins_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var skyruins = OnduSkyruinsFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: skyruins,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Ondu Skyruins always enters tapped (CR 614.1c)");
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

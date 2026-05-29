using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KazuulsFuryFactory"/> and
/// <see cref="KazuulsCliffsFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Kazuul's Fury // Kazuul's Cliffs.
///
/// Front face (Kazuul's Fury, {2}{R}):
///   Instant. "As an additional cost to cast this spell, sacrifice a
///   creature. Kazuul's Fury deals damage equal to the sacrificed
///   creature's power to any target."
///
/// Back face (Kazuul's Cliffs):
///   Land. "This land enters tapped." "{T}: Add {R}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: additional cost is SacrificeCreatureCost.
/// - Front: damage to any target equals the sacrificed creature's power.
/// - Front: planeswalker target loses loyalty equal to power.
/// - Front: player target loses life equal to power.
/// - Front: 0-power sacrifice deals no damage.
/// - Front: illegal target at resolution → no damage (CR 608.2b).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {R} mana ability, no other abilities.
/// - Back: enters tapped (unconditional replacement, CR 614.1c).
/// </summary>
public class KazuulsFuryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static ChosenSpellParams MakeChosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void KazuulsFury_Identity_2R_Instant()
    {
        var card = KazuulsFuryFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Kazuul's Fury");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KazuulsFury()
    {
        var card = NamedCardFactory.Create("Kazuul's Fury", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Kazuul's Fury");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void KazuulsFury_IsRed()
    {
        var card = KazuulsFuryFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "the {R} pip makes it red");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
    }

    [Fact]
    public void KazuulsFury_CarriesMdfcState_FrontFace()
    {
        var card = KazuulsFuryFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Kazuul's Fury is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Kazuul's Fury");
        card.MdfcState!.BackFaceName.Should().Be("Kazuul's Cliffs");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Kazuul's Fury");
    }

    // =========================================================================
    // Front face — additional cost + SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_HasSacrificeCreatureAdditionalCost_AndOneAnyTarget()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);
        var cost = new SacrificeCreatureCost(bear);

        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, o => o!);

        def.HasVariableX.Should().BeFalse("Kazuul's Fury is not an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "one any target");
        def.TargetRequests[0].MaxTargets.Should().Be(1, "one any target");
        def.AdditionalCosts.Should().ContainSingle()
            .Which.Should().BeSameAs(cost,
                "the sacrifice-a-creature additional cost (CR 601.2f) is composed in");
    }

    // =========================================================================
    // Front face — resolve: damage equals sacrificed creature's power
    // =========================================================================

    [Fact]
    public void Resolve_DamageEqualsSacrificedCreaturePower_ToCreatureTarget()
    {
        // Sacrifice a 4-power creature; damage to the target is 4.
        var fodder = new Creature("Big Fodder", "{3}{R}", 4, 1);
        PutOnBattlefield(_alice, fodder);
        var cost = new SacrificeCreatureCost(fodder);
        cost.Pay(_alice).Should().BeTrue("sacrifice succeeds");

        var victim = new Creature("Victim", "{2}{G}", 5, 5);
        PutOnBattlefield(_bob, victim);

        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, o => o!);
        foreach (var e in def.EffectFactory(MakeChosen(victim))) e.Execute();

        victim.Damage.Should().Be(4, "damage equals the sacrificed creature's power (4)");
    }

    [Fact]
    public void Resolve_DamageEqualsSacrificedCreaturePower_ToPlayerTarget()
    {
        var fodder = new Creature("Big Fodder", "{3}{R}", 4, 1);
        PutOnBattlefield(_alice, fodder);
        var cost = new SacrificeCreatureCost(fodder);
        cost.Pay(_alice).Should().BeTrue();

        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, o => o!);
        foreach (var e in def.EffectFactory(MakeChosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(16, "20 - 4 = 16 (damage equals sacrificed power)");
    }

    [Fact]
    public void Resolve_DamageEqualsSacrificedCreaturePower_ToPlaneswalkerTarget()
    {
        var fodder = new Creature("Big Fodder", "{3}{R}", 3, 1);
        PutOnBattlefield(_alice, fodder);
        var cost = new SacrificeCreatureCost(fodder);
        cost.Pay(_alice).Should().BeTrue();

        var jace = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);
        PutOnBattlefield(_bob, jace);

        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, o => o!);
        foreach (var e in def.EffectFactory(MakeChosen(jace))) e.Execute();

        jace.Loyalty.Should().Be(0, "3 damage removes 3 loyalty from a 3-loyalty PW");
    }

    [Fact]
    public void Resolve_ZeroPowerSacrifice_DealsNoDamage()
    {
        var wall = new Creature("Wall of Zero", "{R}", 0, 4);
        PutOnBattlefield(_alice, wall);
        var cost = new SacrificeCreatureCost(wall);
        cost.Pay(_alice).Should().BeTrue();

        var victim = new Creature("Victim", "{2}{G}", 5, 5);
        PutOnBattlefield(_bob, victim);

        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, o => o!);
        foreach (var e in def.EffectFactory(MakeChosen(victim))) e.Execute();

        victim.Damage.Should().Be(0, "0-power sacrifice deals 0 damage");
    }

    [Fact]
    public void Resolve_IllegalTargetAtResolution_NoDamage()
    {
        var fodder = new Creature("Big Fodder", "{3}{R}", 4, 1);
        PutOnBattlefield(_alice, fodder);
        var cost = new SacrificeCreatureCost(fodder);
        cost.Pay(_alice).Should().BeTrue();

        // Resolver returns a non-targetable object → fizzle (CR 608.2b).
        var def = KazuulsFuryFactory.BuildSpellDefinition(cost, _ => "not-a-valid-target");
        var before = _bob.LifeTotal;
        foreach (var e in def.EffectFactory(MakeChosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(before, "illegal target at resolution → no damage (CR 608.2b)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void KazuulsCliffs_Identity_Land()
    {
        var land = KazuulsCliffsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Kazuul's Cliffs");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Kazuul's Cliffs is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KazuulsCliffs()
    {
        var card = NamedCardFactory.Create("Kazuul's Cliffs", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Kazuul's Cliffs");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void KazuulsCliffs_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = KazuulsCliffsFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull("Kazuul's Cliffs is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Kazuul's Fury");
        land.MdfcState!.BackFaceName.Should().Be("Kazuul's Cliffs");
        land.MdfcState!.IsBackFace.Should().BeTrue("the back-face card is constructed pre-flipped");
        land.MdfcState!.ActiveFaceName.Should().Be("Kazuul's Cliffs");
    }

    [Fact]
    public void KazuulsCliffs_HasSingleManaAbility_AddingRed()
    {
        var land = KazuulsCliffsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void KazuulsCliffs_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = KazuulsCliffsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("enters-tapped is a replacement effect, not triggered (CR 614.1c)");
    }

    [Fact]
    public void KazuulsCliffs_EntersTapped_Unconditional()
    {
        var bus = new ReplacementBus();
        var land = KazuulsCliffsFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("Kazuul's Cliffs always enters tapped (CR 614.1c)");
    }
}

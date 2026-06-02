using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for <see cref="KabiraTakedownFactory"/> and
/// <see cref="KabiraPlateauFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Kabira Takedown // Kabira Plateau.
///
/// Front face (Kabira Takedown, {1}{W}):
///   Instant. "Kabira Takedown deals damage equal to the number of creatures
///   you control to target creature or planeswalker."
///
/// Back face (Kabira Plateau):
///   Land. "This land enters tapped." "{T}: Add {W}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: no X, single 1..1 "target creature or planeswalker" request.
/// - Front: 3 creatures controlled → 3 damage to the target.
/// - Front: damage to a creature counts the creature itself.
/// - Front: planeswalker target loses loyalty.
/// - Front: zero creatures controlled → no damage.
/// - Front: illegal-at-resolution target → no damage (CR 608.2b).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {W} mana ability.
/// - Back: unconditional EntersTappedReplacement → enters tapped.
/// </summary>
[Trait("Color", "W")]
public class KabiraTakedownFactoryTests
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
    // Front face — identity + colour
    // =========================================================================

    [Fact]
    public void KabiraTakedown_Identity_OneW_Instant()
    {
        var card = KabiraTakedownFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Kabira Takedown");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KabiraTakedown_IsWhite()
    {
        var card = KabiraTakedownFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "the {W} pip makes it white");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void KabiraTakedown_CarriesMdfcState_FrontFace()
    {
        var card = KabiraTakedownFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Kabira Takedown is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Kabira Takedown");
        card.MdfcState!.BackFaceName.Should().Be("Kabira Plateau");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Kabira Takedown");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_NoX_SingleCreatureOrPWTarget()
    {
        var def = KabiraTakedownFactory.BuildSpellDefinition(_alice, o => o!);

        def.HasVariableX.Should().BeFalse("Kabira Takedown is not an X-spell");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "exactly one target");
        def.TargetRequests[0].MaxTargets.Should().Be(1, "exactly one target");
    }

    // =========================================================================
    // Front face — resolve: damage = creatures you control
    // =========================================================================

    [Fact]
    public void Resolve_ThreeCreaturesControlled_Deals3ToTarget()
    {
        // Caster (Alice) controls three creatures.
        PutOnBattlefield(_alice, new Creature("Soldier 1", "{W}", 1, 1));
        PutOnBattlefield(_alice, new Creature("Soldier 2", "{W}", 1, 1));
        PutOnBattlefield(_alice, new Creature("Soldier 3", "{W}", 1, 1));

        var enemy = new Creature("Big Threat", "{4}{G}", 6, 6);
        PutOnBattlefield(_bob, enemy);

        var def = KabiraTakedownFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(enemy));
        foreach (var e in effects) e.Execute();

        enemy.Damage.Should().Be(3, "Alice controls 3 creatures → 3 damage");
    }

    [Fact]
    public void Resolve_DamageCountsCasterCreaturesNotOpponents()
    {
        // Alice controls 1 creature; Bob controls 5. Only Alice's count.
        PutOnBattlefield(_alice, new Creature("Lone Soldier", "{W}", 1, 1));
        for (var i = 0; i < 5; i++)
            PutOnBattlefield(_bob, new Creature($"Goblin {i}", "{R}", 1, 1));

        var target = new Creature("Target", "{2}{G}", 3, 3);
        PutOnBattlefield(_bob, target);

        var def = KabiraTakedownFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(target));
        foreach (var e in effects) e.Execute();

        target.Damage.Should().Be(1, "only the CASTER's creature count matters");
    }

    [Fact]
    public void Resolve_PlaneswalkerTarget_RemovesLoyalty()
    {
        PutOnBattlefield(_alice, new Creature("Soldier 1", "{W}", 1, 1));
        PutOnBattlefield(_alice, new Creature("Soldier 2", "{W}", 1, 1));

        var jace = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);
        PutOnBattlefield(_bob, jace);

        var def = KabiraTakedownFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(jace));
        foreach (var e in effects) e.Execute();

        jace.Loyalty.Should().Be(1, "2 creatures → 2 damage → removes 2 loyalty from 3");
    }

    [Fact]
    public void Resolve_ZeroCreaturesControlled_NoDamage()
    {
        // Alice controls no creatures.
        var enemy = new Creature("Big Threat", "{4}{G}", 6, 6);
        PutOnBattlefield(_bob, enemy);

        var def = KabiraTakedownFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(enemy));
        foreach (var e in effects) e.Execute();

        enemy.Damage.Should().Be(0, "0 creatures controlled → 0 damage");
    }

    [Fact]
    public void Resolve_IllegalTargetAtResolution_NoDamage()
    {
        PutOnBattlefield(_alice, new Creature("Soldier 1", "{W}", 1, 1));

        // Resolver returns a non-creature/PW object → illegal → fizzle.
        var def = KabiraTakedownFactory.BuildSpellDefinition(
            _alice, _ => "not-a-valid-target");
        var before = _bob.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(before,
            "illegal target at resolution → spell does nothing (CR 608.2b)");
    }

    // =========================================================================
    // Back face — identity
    // =========================================================================

    [Fact]
    public void KabiraPlateau_Identity_Land()
    {
        var land = KabiraPlateauFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Kabira Plateau");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Kabira Plateau is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // =========================================================================
    // Back face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void KabiraPlateau_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = KabiraPlateauFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Kabira Plateau is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Kabira Takedown");
        land.MdfcState!.BackFaceName.Should().Be("Kabira Plateau");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Kabira Plateau");
    }

    // =========================================================================
    // Back face — {T}: Add {W}
    // =========================================================================

    [Fact]
    public void KabiraPlateau_HasSingleManaAbility_AddingWhite()
    {
        var land = KabiraPlateauFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {W} ability");
        manaAbilities[0].ManaGenerated.White.Should().BeGreaterThan(0, "produces white mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void KabiraPlateau_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = KabiraPlateauFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("enters-tapped is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — unconditional enters-tapped (CR 614.1c)
    // =========================================================================

    [Fact]
    public void KabiraPlateau_EntersTapped_WhenReplacementBusSupplied()
    {
        var bus = new ReplacementBus();
        var land = KabiraPlateauFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Kabira Plateau always enters tapped (CR 614.1c)");
    }

    [Fact]
    public void KabiraPlateau_ShapeOnlyPath_NoEntersTappedReplacement()
    {
        // Single-arg path: no bus → no enters-tapped replacement registered.
        var bus = new ReplacementBus();
        var land = KabiraPlateauFactory.Create(_alice); // no bus wired

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeFalse(
            "no bus wired into the land → no enters-tapped replacement");
    }
}

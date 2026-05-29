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
/// Tests for <see cref="ShatterskullSmashingFactory"/> and
/// <see cref="ShatterskullTheHammerPassFactory"/> — the front + back faces
/// of the Zendikar Rising modal double-faced card
/// Shatterskull Smashing // Shatterskull, the Hammer Pass.
///
/// Front face (Shatterskull Smashing, {X}{R}{R}):
///   Sorcery. "Shatterskull Smashing deals X damage divided as you choose
///   among up to two target creatures and/or planeswalkers. If X is 6 or
///   more, Shatterskull Smashing deals twice X damage divided as you choose
///   among them instead."
///
/// Back face (Shatterskull, the Hammer Pass):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {R}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: HasVariableX=true, 0..2 target request for creatures/PWs.
/// - Front: X=3, single target — 3 damage to that target.
/// - Front: X=3, two targets, default split (2+1).
/// - Front: X=3, two targets, caller-supplied split (1+2).
/// - Front: X=6, single target — 12 damage (2X).
/// - Front: X=6, two targets, default split (6+6).
/// - Front: X&lt;6 (X=5), single target — 5 damage (NOT doubled).
/// - Front: X=0 — no damage dealt.
/// - Front: all targets illegal at resolution — no damage (CR 608.2b).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {R} mana ability.
/// - Back: pay 3 life → enters untapped.
/// - Back: decline → enters tapped.
/// - Back: life &lt; 3 → enters tapped (CR 119.4).
/// - Back: exactly 3 life → legal to pay, enters untapped.
/// - Back: no agent → enters tapped.
/// </summary>
public class ShatterskullSmashinFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ShatterskullSmashinFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static ChosenSpellParams MakeChosen(int x, params object[] targets) =>
        new(
            ModeIndex: null,
            X: x,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void ShatterskullSmashing_Identity_XRR_Sorcery()
    {
        var card = ShatterskullSmashingFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Shatterskull Smashing");
        card.ManaCost.Should().Be("{X}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShatterskullSmashing()
    {
        var card = NamedCardFactory.Create("Shatterskull Smashing", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Shatterskull Smashing");
        card.ManaCost.Should().Be("{X}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void ShatterskullSmashing_IsRed()
    {
        var card = ShatterskullSmashingFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red, "two {R} pips make it red");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void ShatterskullSmashing_CarriesMdfcState_FrontFace()
    {
        var card = ShatterskullSmashingFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Shatterskull Smashing is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Shatterskull Smashing");
        card.MdfcState!.BackFaceName.Should().Be("Shatterskull, the Hammer Pass");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Shatterskull Smashing");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndUpToTwoCreatureOrPWTargets()
    {
        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);

        def.HasVariableX.Should().BeTrue("Shatterskull Smashing is an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0,
            "up to two targets — zero is valid");
        def.TargetRequests[0].MaxTargets.Should().Be(2,
            "up to two targets");
    }

    // =========================================================================
    // Front face — resolve: X < 6, total = X
    // =========================================================================

    [Fact]
    public void Resolve_X3_SingleCreatureTarget_Deals3Damage()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(3, bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(3, "X=3 < 6, total is X=3, all on single target");
    }

    [Fact]
    public void Resolve_X3_TwoTargets_DefaultSplit_2And1()
    {
        // Default split: remainder on first target.
        var bear1 = new Creature("Bear 1", "{1}{G}", 2, 2);
        var bear2 = new Creature("Bear 2", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear1);
        PutOnBattlefield(_bob, bear2);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(3, bear1, bear2));
        foreach (var e in effects) e.Execute();

        var total = bear1.Damage + bear2.Damage;
        total.Should().Be(3, "total damage = X = 3 (CR 119.4)");
        // Default split: first target gets ceil(3/2)=2, second gets 1.
        bear1.Damage.Should().Be(2, "default split front-loads remainder on first target");
        bear2.Damage.Should().Be(1);
    }

    [Fact]
    public void Resolve_X3_TwoTargets_CallerSplit_1And2()
    {
        var bear1 = new Creature("Bear 1", "{1}{G}", 2, 2);
        var bear2 = new Creature("Bear 2", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear1);
        PutOnBattlefield(_bob, bear2);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(
            resolver: o => o!,
            distribute: (legal, total) => new Dictionary<object, int>
            {
                [legal[0]] = 1,
                [legal[1]] = 2,
            });
        var effects = def.EffectFactory(MakeChosen(3, bear1, bear2));
        foreach (var e in effects) e.Execute();

        bear1.Damage.Should().Be(1, "caller allocated 1 to bear1");
        bear2.Damage.Should().Be(2, "caller allocated 2 to bear2");
    }

    // =========================================================================
    // Front face — resolve: X >= 6, total = 2X
    // =========================================================================

    [Fact]
    public void Resolve_X6_SingleTarget_Deals12Damage()
    {
        // X=6 >= 6 → total = 2*6 = 12.
        var bear = new Creature("Big Bear", "{4}{G}", 6, 6);
        PutOnBattlefield(_bob, bear);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(6, bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(12, "X=6 → total = 2X = 12");
    }

    [Fact]
    public void Resolve_X6_TwoTargets_DefaultSplit_6And6()
    {
        var bear1 = new Creature("Bear 1", "{4}{G}", 6, 6);
        var bear2 = new Creature("Bear 2", "{4}{G}", 6, 6);
        PutOnBattlefield(_bob, bear1);
        PutOnBattlefield(_bob, bear2);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(6, bear1, bear2));
        foreach (var e in effects) e.Execute();

        var total = bear1.Damage + bear2.Damage;
        total.Should().Be(12, "X=6 → total = 2X = 12");
        bear1.Damage.Should().Be(6, "even split of 12 across two targets");
        bear2.Damage.Should().Be(6);
    }

    [Fact]
    public void Resolve_X5_SingleTarget_Deals5_NotDoubled()
    {
        // X=5 < 6 → NOT doubled, total = X = 5.
        var bear = new Creature("Big Bear", "{4}{G}", 6, 6);
        PutOnBattlefield(_bob, bear);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(5, bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(5, "X=5 < 6 → total is X=5, not doubled");
    }

    // =========================================================================
    // Front face — resolve: Planeswalker target
    // =========================================================================

    [Fact]
    public void Resolve_X3_PlaneswalkerTarget_RemovesLoyalty()
    {
        var jace = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);
        PutOnBattlefield(_bob, jace);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(3, jace));
        foreach (var e in effects) e.Execute();

        jace.Loyalty.Should().Be(0, "3 damage → removes 3 loyalty from 3-loyalty PW");
    }

    // =========================================================================
    // Front face — resolve: X=0 no-op
    // =========================================================================

    [Fact]
    public void Resolve_X0_NoTargets_NoDamage()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear);

        var def = ShatterskullSmashingFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(0, bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(0, "X=0 → 0 total damage, no damage dealt");
    }

    // =========================================================================
    // Front face — resolve: all targets illegal (CR 608.2b)
    // =========================================================================

    [Fact]
    public void Resolve_AllTargetsIllegal_NoDamage()
    {
        // Resolver returns a non-creature/PW object → all illegal → fizzle.
        var def = ShatterskullSmashingFactory.BuildSpellDefinition(
            resolver: _ => "not-a-valid-target");
        var before = _bob.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(5, _bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(before,
            "illegal targets at resolution → spell fizzles, no damage (CR 608.2b)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void ShatterskullHammerPass_Identity_Land()
    {
        var land = ShatterskullTheHammerPassFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Shatterskull, the Hammer Pass");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Shatterskull, the Hammer Pass is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShatterskullTheHammerPass()
    {
        var card = NamedCardFactory.Create("Shatterskull, the Hammer Pass", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Shatterskull, the Hammer Pass");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // =========================================================================
    // Back face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void ShatterskullHammerPass_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = ShatterskullTheHammerPassFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Shatterskull, the Hammer Pass is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Shatterskull Smashing");
        land.MdfcState!.BackFaceName.Should().Be("Shatterskull, the Hammer Pass");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Shatterskull, the Hammer Pass");
    }

    // =========================================================================
    // Back face — {T}: Add {R}
    // =========================================================================

    [Fact]
    public void ShatterskullHammerPass_HasSingleManaAbility_AddingRed()
    {
        var land = ShatterskullTheHammerPassFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void ShatterskullHammerPass_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = ShatterskullTheHammerPassFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void ShatterskullHammerPass_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = ShatterskullTheHammerPassFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    [Fact]
    public void ShatterskullHammerPass_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = ShatterskullTheHammerPassFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("enters tapped when agent declines");
        _alice.LifeTotal.Should().Be(20, "no life paid");
    }

    [Fact]
    public void ShatterskullHammerPass_EntersTapped_WhenLifeBelowThree()
    {
        // CR 119.4 — can't pay life you don't have.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if prompted, ScriptedAgent would throw.
        AgentRegistry.Set(_alice, agent);

        var land = ShatterskullTheHammerPassFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue(
            "can't pay 3 life with only 2 — enters tapped (CR 119.4)");
        _alice.LifeTotal.Should().Be(2, "no payment taken");
    }

    [Fact]
    public void ShatterskullHammerPass_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 — payment to 0 is legal.
        var bus = new ReplacementBus();
        _alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = ShatterskullTheHammerPassFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeFalse(
            "at exactly 3 life the payment is legal — enters untapped");
        _alice.LifeTotal.Should().Be(0, "3 - 3 = 0");
    }

    [Fact]
    public void ShatterskullHammerPass_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        // No AgentRegistry.Set — no agent at all.

        var land = ShatterskullTheHammerPassFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue("no agent → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }
}

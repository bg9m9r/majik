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
/// Tests for <see cref="EmeriasCallFactory"/> and
/// <see cref="EmeriaShatteredSkyclaveFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Emeria's Call // Emeria, Shattered Skyclave.
///
/// Front face (Emeria's Call, {4}{W}{W}{W}):
///   Sorcery. "Create two 4/4 white Angel Warrior creature tokens with flying.
///   Non-Angel creatures you control gain indestructible until your next turn."
///
/// Back face (Emeria, Shattered Skyclave):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {W}."
///
/// Front-face MDFC + token-creation + indestructible-grant analogue:
///   <see cref="AgadeemsAwakeningFactory"/> (MDFC front face shape),
///   <see cref="KrenkosCommandFactory"/> (TokenFactory.CreateOnBattlefield),
///   <see cref="BorosCharmFactory"/> (GrantKeywordUntilEndOfTurnEffect for the
///   indestructible grant; "until your next turn" is approximated as
///   until-end-of-turn — same posture as Karn the Great Creator / The One Ring).
/// Back-face land analogue: <see cref="AgadeemTheUndercryptFactory"/>.
/// </summary>
[Trait("Color", "W")]
public class EmeriasCallFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EmeriasCallFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static ChosenSpellParams MakeChosen() =>
        new(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void EmeriasCall_Identity_4WWW_Sorcery()
    {
        var card = EmeriasCallFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Emeria's Call");
        card.ManaCost.Should().Be("{4}{W}{W}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void EmeriasCall_IsWhite()
    {
        var card = EmeriasCallFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White, "three {W} pips make it white");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void EmeriasCall_CarriesMdfcState_FrontFace()
    {
        var card = EmeriasCallFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Emeria's Call is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Emeria's Call");
        card.MdfcState!.BackFaceName.Should().Be("Emeria, Shattered Skyclave");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Emeria's Call");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_NoTargets_NoModes_NoX()
    {
        var def = EmeriasCallFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse("Emeria's Call is not an X-spell");
        def.TargetRequests.Should().BeEmpty("the spell resolves entirely on the caster");
        def.Modes.Should().BeEmpty("Emeria's Call is not modal");
    }

    // =========================================================================
    // Front face — resolve: token creation
    // =========================================================================

    [Fact]
    public void Resolve_CreatesTwo_4_4_WhiteAngelWarrior_Flying_Tokens()
    {
        var def = EmeriasCallFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(MakeChosen());
        foreach (var e in effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(2, "create two 4/4 white Angel Warrior tokens");
        foreach (var token in tokens)
        {
            token.BasePower.Should().Be(4);
            token.BaseToughness.Should().Be(4);
            token.HasSubtype(CardSubtype.Angel).Should().BeTrue();
            token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();

            var colors = CardColors.GetColors(token);
            colors.Should().Contain(ManaColor.White, "tokens are white (CR 111.4)");
            colors.Should().HaveCount(1, "mono-white token");

            token.Abilities.OfType<KeywordAbility>()
                .Select(k => k.Keyword)
                .Should().Contain(kw => string.Equals(kw, "Flying", StringComparison.OrdinalIgnoreCase),
                    "tokens have flying");
        }
    }

    // =========================================================================
    // Front face — resolve: non-Angel creatures gain indestructible
    // =========================================================================

    [Fact]
    public void Resolve_GrantsIndestructible_ToNonAngelCreaturesYouControl_NotAngels()
    {
        var continuous = new ContinuousEffectsService();

        // A non-Angel creature the caster controls — should gain indestructible.
        var soldier = new Creature("Soldier", "{1}{W}", 2, 2,
            subtypes: new[] { CardSubtype.Soldier });
        soldier.SetOwner(_alice);
        soldier.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(soldier);
        soldier.SetZone(ZoneType.Battlefield);

        // An Angel the caster controls — should NOT gain indestructible.
        var angel = new Creature("Serra Angel", "{3}{W}{W}", 4, 4,
            subtypes: new[] { CardSubtype.Angel });
        angel.SetOwner(_alice);
        angel.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(angel);
        angel.SetZone(ZoneType.Battlefield);

        // An opponent's non-Angel creature — should NOT gain indestructible.
        var oppBear = new Creature("Opp Bear", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        oppBear.SetOwner(_bob);
        oppBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(oppBear);
        oppBear.SetZone(ZoneType.Battlefield);

        var def = EmeriasCallFactory.BuildSpellDefinition(_alice, continuousEffects: continuous);
        var effects = def.EffectFactory(MakeChosen());
        foreach (var e in effects) e.Execute();

        continuous.Compute(soldier).Keywords.Should().Contain(
            kw => string.Equals(kw, "Indestructible", StringComparison.OrdinalIgnoreCase),
            "non-Angel creatures you control gain indestructible");
        continuous.Compute(angel).Keywords.Should().NotContain(
            kw => string.Equals(kw, "Indestructible", StringComparison.OrdinalIgnoreCase),
            "Angels you control are excluded");
        continuous.Compute(oppBear).Keywords.Should().NotContain(
            kw => string.Equals(kw, "Indestructible", StringComparison.OrdinalIgnoreCase),
            "only creatures YOU control are affected");
    }

    [Fact]
    public void Resolve_CreatedAngelTokens_DoNotGainIndestructible()
    {
        var continuous = new ContinuousEffectsService();

        var def = EmeriasCallFactory.BuildSpellDefinition(_alice, continuousEffects: continuous);
        var effects = def.EffectFactory(MakeChosen());
        foreach (var e in effects) e.Execute();

        // The two tokens are Angels — the grant must exclude them (the grant
        // snapshots the non-Angel set BEFORE / regardless of token minting).
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(2);
        foreach (var token in tokens)
        {
            continuous.Compute(token).Keywords.Should().NotContain(
                kw => string.Equals(kw, "Indestructible", StringComparison.OrdinalIgnoreCase),
                "the Angel tokens are Angels — excluded from the non-Angel grant");
        }
    }

    [Fact]
    public void Resolve_NoContinuousService_StillCreatesTokens()
    {
        // Shape-only path: no continuous-effects service wired. Tokens are
        // still created; the indestructible grant performs no registration.
        var def = EmeriasCallFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(MakeChosen());

        Action act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsToken).Should().Be(2);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void EmeriaShatteredSkyclave_Identity_Land()
    {
        var land = EmeriaShatteredSkyclaveFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Emeria, Shattered Skyclave");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Emeria, Shattered Skyclave is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void EmeriaShatteredSkyclave_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = EmeriaShatteredSkyclaveFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Emeria, Shattered Skyclave is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Emeria's Call");
        land.MdfcState!.BackFaceName.Should().Be("Emeria, Shattered Skyclave");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Emeria, Shattered Skyclave");
    }

    // =========================================================================
    // Back face — {T}: Add {W}
    // =========================================================================

    [Fact]
    public void EmeriaShatteredSkyclave_HasSingleManaAbility_AddingWhite()
    {
        var land = EmeriaShatteredSkyclaveFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {W} ability");
        manaAbilities[0].ManaGenerated.White.Should().BeGreaterThan(0, "produces white mana");
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void EmeriaShatteredSkyclave_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = EmeriaShatteredSkyclaveFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void EmeriaShatteredSkyclave_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = EmeriaShatteredSkyclaveFactory.Create(_alice, replacements: bus);

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
    public void EmeriaShatteredSkyclave_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = EmeriaShatteredSkyclaveFactory.Create(_alice, replacements: bus);

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
    public void EmeriaShatteredSkyclave_EntersTapped_WhenLifeBelowThree()
    {
        // CR 119.4 — can't pay life you don't have.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        AgentRegistry.Set(_alice, agent);

        var land = EmeriaShatteredSkyclaveFactory.Create(_alice, replacements: bus);

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
    public void EmeriaShatteredSkyclave_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();

        var land = EmeriaShatteredSkyclaveFactory.Create(_alice, replacements: bus);

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

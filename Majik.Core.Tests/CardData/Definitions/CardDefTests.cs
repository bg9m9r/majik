using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the fluent <see cref="CardDef"/> DSL + <see cref="CardDefRuntime"/>.
///
/// Covers:
/// - Builder produces a CardDef with the right shape (name, cost, type,
///   P/T, supertypes, subtypes, keywords, mana abilities).
/// - <see cref="CardDefRuntime.Build"/> materializes the right runtime
///   C# class for each primary type.
/// - <see cref="Resolve"/> body wires the engine's
///   <see cref="IEffect"/> list.
/// </summary>
public class CardDefTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---- Builder shape ----------------------------------------------------

    [Fact]
    public void LightningBolt_ShapeIsSorceryWithRedCost()
    {
        // The spec's bolt example — sorcery used as a stand-in (the engine
        // doesn't gate Resolve on Instant vs Sorcery, so it's the cheapest
        // shape to exercise).
        CardDef def = CardDef
            .Sorcery("Lightning Bolt", "{R}")
            .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));

        def.Name.Should().Be("Lightning Bolt");
        def.ManaCost.Should().Be("{R}");
        def.PrimaryType.Should().Be(CardType.Sorcery);
        def.ResolveBody.Should().NotBeNull();
        def.ResolveBody!.Effects.Should().ContainSingle();
        var effect = def.ResolveBody.Effects[0];
        effect.Kind.Should().Be(ResolveEffectKind.DealDamage);
        effect.IntArg.Should().Be(3);
        effect.Target.Should().Be(TargetKind.AnyTarget);
    }

    [Fact]
    public void GiantGrowth_PumpUntilEot()
    {
        CardDef def = CardDef
            .Instant("Giant Growth", "{G}")
            .Resolve(c => c.PumpUntilEndOfTurn(3, 3).To(TargetKind.Creature));

        def.PrimaryType.Should().Be(CardType.Instant);
        def.ResolveBody!.Effects[0].Kind.Should().Be(ResolveEffectKind.PumpUntilEndOfTurn);
        def.ResolveBody.Effects[0].IntArg.Should().Be(3);
        def.ResolveBody.Effects[0].IntArg2.Should().Be(3);
        def.ResolveBody.Effects[0].Target.Should().Be(TargetKind.Creature);
    }

    [Fact]
    public void GrizzlyBears_VanillaCreatureShape()
    {
        CardDef def = CardDef
            .Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2)
            .WithSubtype(CardSubtype.Bear);

        def.PrimaryType.Should().Be(CardType.Creature);
        def.Power.Should().Be(2);
        def.Toughness.Should().Be(2);
        def.Subtypes.Should().ContainSingle().Which.Should().Be(CardSubtype.Bear);
        def.ResolveBody.Should().BeNull("vanilla creatures have no spell resolve body");
    }

    [Fact]
    public void Terror_DestroyTargetShortcut()
    {
        // Terror uses the convenience `DestroyTarget(kind)` overload — no
        // separate `.To(...)` call needed.
        CardDef def = CardDef
            .Instant("Terror", "{1}{B}")
            .Resolve(c => c.DestroyTarget(TargetKind.NonblackNonartifactCreature));

        def.ResolveBody!.Effects[0].Kind.Should().Be(ResolveEffectKind.DestroyTarget);
        def.ResolveBody.Effects[0].Target.Should().Be(TargetKind.NonblackNonartifactCreature);
    }

    [Fact]
    public void DarkRitual_AddManaInResolveBody()
    {
        CardDef def = CardDef
            .Instant("Dark Ritual", "{B}")
            .Resolve(c => c.AddMana("BBB"));

        def.ResolveBody!.Effects[0].Kind.Should().Be(ResolveEffectKind.AddMana);
        def.ResolveBody.Effects[0].StringArg.Should().Be("BBB");
    }

    [Fact]
    public void Planeswalker_HasLoyalty()
    {
        CardDef def = CardDef
            .Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", loyalty: 3)
            .WithSupertype(CardSupertype.Legendary)
            .WithSubtype(CardSubtype.Jace);

        def.PrimaryType.Should().Be(CardType.Planeswalker);
        def.Loyalty.Should().Be(3);
        def.Supertypes.Should().ContainSingle().Which.Should().Be(CardSupertype.Legendary);
    }

    [Fact]
    public void ImplicitConversion_DropsExplicitBuild()
    {
        // Allow factories to omit the explicit `.Build()` call at the end.
        CardDef def = CardDef.Instant("Test", "{1}"); // implicit conversion fires here

        def.Name.Should().Be("Test");
    }

    // ---- Runtime materialization -----------------------------------------

    [Fact]
    public void Runtime_BuildsInstantWithCorrectIdentity()
    {
        CardDef def = CardDef
            .Sorcery("Lightning Bolt", "{R}")
            .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));

        var card = CardDefRuntime.Build(def, _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Lightning Bolt");
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Runtime_BuildsCreatureWithSubtypeAndPT()
    {
        CardDef def = CardDef
            .Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2)
            .WithSubtype(CardSubtype.Bear);

        var card = (Creature)CardDefRuntime.Build(def, _alice);

        card.Name.Should().Be("Grizzly Bears");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        card.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Runtime_AttachesKeywordMarkers()
    {
        CardDef def = CardDef
            .Creature("Phoenix of Ash", "{2}{R}{R}", power: 3, toughness: 2)
            .WithSubtype(CardSubtype.Phoenix)
            .WithKeyword("Haste");

        var card = (Creature)CardDefRuntime.Build(def, _alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Haste");
    }

    [Fact]
    public void Runtime_AttachesManaAbilities()
    {
        CardDef def = CardDef
            .Artifact("Sol Ring", "{1}")
            .ManaAbility("CC");

        var card = (Artifact)CardDefRuntime.Build(def, _alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Runtime_BuildsLandWithoutManaCost()
    {
        CardDef def = CardDef.Land("Mishra's Workshop").ManaAbility("CCC");

        var card = (Land)CardDefRuntime.Build(def, _alice);

        card.Name.Should().Be("Mishra's Workshop");
        card.ManaCost.Should().Be("");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Runtime_AppliesAdditionalCardType()
    {
        // Grist: Planeswalker + Creature.
        CardDef def = CardDef
            .Planeswalker("Grist, the Hunger Tide", "{1}{B}{G}", loyalty: 3)
            .WithSupertype(CardSupertype.Legendary)
            .WithSubtype(CardSubtype.Insect)
            .WithType(CardType.Creature);

        var card = CardDefRuntime.Build(def, _alice);

        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Runtime_PlaneswalkerNeedsLoyalty()
    {
        var def = new CardDefBuilder("Bad Walker", "{U}", CardType.Planeswalker).Build();
        // No loyalty set — runtime must throw clearly.
        var act = () => CardDefRuntime.Build(def, _alice);
        act.Should().Throw<ArgumentException>().WithMessage("*loyalty*");
    }

    [Fact]
    public void Runtime_CreatureNeedsPowerToughness()
    {
        var def = new CardDefBuilder("Bad Creature", "{1}", CardType.Creature).Build();
        var act = () => CardDefRuntime.Build(def, _alice);
        act.Should().Throw<ArgumentException>().WithMessage("*power*");
    }

    // ---- Resolve-body materialization ------------------------------------

    [Fact]
    public void Resolve_DealDamage_RoutesToPlayerLifeLoss()
    {
        CardDef def = CardDef
            .Sorcery("Lightning Bolt", "{R}")
            .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget));

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, controller: _alice, targetResolver: t => t, chosenTarget: _bob);

        effects.Should().ContainSingle();
        var startBobLife = _bob.LifeTotal;
        effects[0].Execute();
        _bob.LifeTotal.Should().Be(startBobLife - 3, "3 damage to a player == 3 life loss");
    }

    [Fact]
    public void Resolve_GainLife_AppliesToController()
    {
        CardDef def = CardDef
            .Sorcery("Healing Salve", "{W}")
            .Resolve(c => c.GainLife(3));

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t);
        var startLife = _alice.LifeTotal;
        effects[0].Execute();
        _alice.LifeTotal.Should().Be(startLife + 3);
    }

    [Fact]
    public void Resolve_AddMana_PutsManaInControllerPool()
    {
        CardDef def = CardDef
            .Instant("Dark Ritual", "{B}")
            .Resolve(c => c.AddMana("BBB"));

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t);
        effects[0].Execute();
        _alice.ManaPool.Black.Should().Be(3);
    }

    [Fact]
    public void Resolve_ShapeOnly_BuildsZeroEffects()
    {
        CardDef def = CardDef
            .Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2)
            .WithSubtype(CardSubtype.Bear);

        var effects = CardDefRuntime.BuildSpellResolveEffects(def, _alice, t => t);
        effects.Should().BeEmpty();
    }

    // ---- Stub-fill: DestroyTarget / Counter / PumpUntilEndOfTurn / CreateToken

    [Fact]
    public void Resolve_DestroyTarget_MovesCreatureToGraveyard()
    {
        // Murder-shape: destroy target creature.
        CardDef def = CardDef
            .Instant("Murder", "{1}{B}{B}")
            .Resolve(c => c.DestroyTarget(TargetKind.Creature));

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob); target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(global::Majik.Core.Zones.ZoneType.Battlefield);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: target);
        effects[0].Execute();

        target.Zone.Should().Be(global::Majik.Core.Zones.ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
    }

    [Fact]
    public void Resolve_DestroyTarget_IndestructibleCancels()
    {
        // CR 702.12b — indestructible permanents can't be destroyed.
        CardDef def = CardDef
            .Instant("Murder", "{1}{B}{B}")
            .Resolve(c => c.DestroyTarget(TargetKind.Creature));

        var target = new Creature("Darksteel Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob); target.SetController(_bob);
        target.AddAbility(new KeywordAbility("Indestructible", target, _bob));
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(global::Majik.Core.Zones.ZoneType.Battlefield);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: target);
        effects[0].Execute();

        target.Zone.Should().Be(global::Majik.Core.Zones.ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(target);
    }

    [Fact]
    public void Resolve_DestroyTarget_RegenerationShieldConsumed()
    {
        // CR 701.15c — regeneration shield consumed; permanent stays.
        CardDef def = CardDef
            .Instant("Murder", "{1}{B}{B}")
            .Resolve(c => c.DestroyTarget(TargetKind.Creature));

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob); target.SetController(_bob);
        target.AddRegenerationShield();
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(global::Majik.Core.Zones.ZoneType.Battlefield);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: target);
        effects[0].Execute();

        target.Zone.Should().Be(global::Majik.Core.Zones.ZoneType.Battlefield);
        target.HasRegenerationShield.Should().BeFalse("shield was consumed");
    }

    [Fact]
    public void Resolve_PumpUntilEndOfTurn_RegistersLayer7c()
    {
        CardDef def = CardDef
            .Instant("Giant Growth", "{G}")
            .Resolve(c => c.PumpUntilEndOfTurn(3, 3).To(TargetKind.Creature));

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice); target.SetController(_alice);
        target.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(target);
        target.SetZone(global::Majik.Core.Zones.ZoneType.Battlefield);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: target);
        effects[0].Execute();

        target.Power.Should().Be(5);
        target.Toughness.Should().Be(5);
    }

    [Fact]
    public void Resolve_Counter_RemovesSpellFromStackAndGraveyards()
    {
        // CR 701.5 — Counterspell shape.
        CardDef def = CardDef
            .Instant("Counterspell", "{U}{U}")
            .Resolve(c => c.Counter());

        var bobsBolt = new Instant("Lightning Bolt", "{R}");
        bobsBolt.SetOwner(_bob); bobsBolt.SetController(_bob);
        bobsBolt.SetZone(global::Majik.Core.Zones.ZoneType.Stack);
        var spell = new global::Majik.Core.Spells.Spell(bobsBolt, _bob);
        var stack = new global::Majik.Core.Stack.Stack();
        stack.Push(spell);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: spell, stack: stack);
        effects[0].Execute();

        stack.IsEmpty.Should().BeTrue();
        bobsBolt.Zone.Should().Be(global::Majik.Core.Zones.ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_Counter_NoncreatureSpellFilter_GatesCreatureSpells()
    {
        // Negate shape — creature spell on stack is an illegal target,
        // effect does nothing.
        CardDef def = CardDef
            .Instant("Negate", "{1}{U}")
            .Resolve(c => c.Counter(TargetKind.NoncreatureSpell));

        var creature = new Creature("Goblin", "{R}", 1, 1);
        creature.SetOwner(_bob); creature.SetController(_bob);
        creature.SetZone(global::Majik.Core.Zones.ZoneType.Stack);
        var spell = new global::Majik.Core.Spells.Spell(creature, _bob);
        var stack = new global::Majik.Core.Stack.Stack();
        stack.Push(spell);

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, _alice, t => t, chosenTarget: spell, stack: stack);
        effects[0].Execute();

        stack.IsEmpty.Should().BeFalse("creature spell is an illegal target for Negate");
    }

    [Fact]
    public void Resolve_CreateToken_PutsTokenOnBattlefield()
    {
        // CR 111 — Raise the Alarm-shape: create two 1/1 white Soldier tokens.
        CardDef def = CardDef
            .Sorcery("Raise the Alarm", "{1}{W}")
            .Resolve(c => c
                .CreateToken("Soldier", 1, 1, CardSubtype.Soldier).Colors(ManaColor.White)
                .CreateToken("Soldier", 1, 1, CardSubtype.Soldier).Colors(ManaColor.White));

        var effects = CardDefRuntime.BuildSpellResolveEffects(def, _alice, t => t);
        effects.Should().HaveCount(2);
        foreach (var e in effects) e.Execute();

        var soldiers = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Soldier))
            .ToList();
        soldiers.Should().HaveCount(2);
        soldiers[0].BasePower.Should().Be(1);
        soldiers[0].BaseToughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_CreateToken_WithKeyword_AttachesKeyword()
    {
        // Lingering Souls-shape: 1/1 white Spirit with flying.
        CardDef def = CardDef
            .Sorcery("Spirit Token Maker", "{2}{W}")
            .Resolve(c => c
                .CreateToken("Spirit", 1, 1, CardSubtype.Spirit)
                    .Colors(ManaColor.White)
                    .WithKeyword("Flying"));

        var effects = CardDefRuntime.BuildSpellResolveEffects(def, _alice, t => t);
        effects[0].Execute();

        var spirit = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.HasSubtype(CardSubtype.Spirit));
        spirit.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
    }

    // ---- Source generator: Define()-only path ----------------------------

    [Fact]
    public void SourceGen_DispatchesDefineOnlyFactoryViaRuntime()
    {
        // TestDefineOnlyVanillaFactory has NO Create(Player) — only a
        // Define() returning a CardDef. The source generator must
        // synthesize the dispatch arm calling CardDefRuntime.Build(...)
        // directly. If the arm is missing, the dispatcher returns its
        // unknown-name fallback (Card shell), which has no subtype.
        var card = global::Majik.Core.CardData.NamedCardFactory.Create(
            "DSL Test Vanilla Elf", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SourceGen_DispatchesDefineWithCreateShim_ThroughShim()
    {
        // BadgermoleCubFactory has BOTH Define() and Create(Player) (typed
        // shim). The generator MUST prefer Create(Player) — we verify by
        // checking the dispatcher's arm returns a Creature with the
        // expected shape.
        var card = global::Majik.Core.CardData.NamedCardFactory.Create(
            "Badgermole Cub", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSubtype(CardSubtype.Bear).Should().BeTrue();
    }
}

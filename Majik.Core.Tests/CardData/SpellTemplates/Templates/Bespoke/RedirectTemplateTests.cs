using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class RedirectTemplateTests
{
    private static SpellBindContext Ctx(string text, Majik.Core.Stack.Stack? stack = null) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: stack ?? new Majik.Core.Stack.Stack());

    // The four oracle texts the family is supposed to bind. Deflection,
    // Shunt, and Swerve share the exact "Change the target of target spell
    // with a single target." text — one test entry per UNIQUE oracle.
    // Imp's Mischief carries the life-loss rider (dropped at v1).
    public static IEnumerable<object[]> BoundOracles => new[]
    {
        // Deflection / Shunt / Swerve — identical leading clause.
        new object[] { "Change the target of target spell with a single target." },
        // Imp's Mischief — rider after the leading clause.
        new object[]
        {
            "Change the target of target spell with a single target. " +
            "You lose life equal to that spell's mana value.",
        },
    };

    [Theory]
    [MemberData(nameof(BoundOracles))]
    public void RedirectTemplate_MatchesFamily(string oracle)
    {
        new RedirectTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull("the Deflection / Imp's Mischief / Shunt / Swerve family must bind");
    }

    [Theory]
    // No leading "Change the target" — counter / damage / generic spells must not bind.
    [InlineData("Counter target spell.")]
    [InlineData("Destroy target creature.")]
    [InlineData("Deal 3 damage to any target.")]
    // "Change the target of target spell" without the "single target" qualifier
    // is a different (broader) shape — must not bind here.
    [InlineData("Change the target of target spell.")]
    // "Change the target of target ability" — also out of family.
    [InlineData("Change the target of target ability with a single target.")]
    public void RedirectTemplate_DoesNotMatchOutOfFamily(string oracle)
    {
        new RedirectTemplate().TryBind(Ctx(oracle)).Should().BeNull();
    }

    [Fact]
    public void RedirectTemplate_CanBind_RequiresStack()
    {
        var template = new RedirectTemplate();
        var oracle = "Change the target of target spell with a single target.";

        // No Stack — must not bind.
        var ctxNoStack = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = oracle },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);
        template.TryBind(ctxNoStack).Should().BeNull();

        // Stack present — binds.
        template.TryBind(Ctx(oracle)).Should().NotBeNull();
    }

    [Fact]
    public void RedirectTemplate_Priority_90()
    {
        new RedirectTemplate().Priority.Should().Be(90);
    }

    [Fact]
    public void RedirectTemplate_Intent_ProtectionAndRemoval()
    {
        var intent = new RedirectTemplate().Intent;
        intent.HasAny(BotIntent.Protection).Should().BeTrue();
        intent.HasAny(BotIntent.Removal).Should().BeTrue();
    }

    [Fact]
    public void RedirectTemplate_Definition_HasOneSingletonTargetRequest()
    {
        var def = new RedirectTemplate().TryBind(Ctx(
            "Change the target of target spell with a single target."));
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ---- Integration: Player A puts a single-target spell on the stack,
    // ----              Player B casts Deflection, original target is replaced.
    [Fact]
    public void Integration_RedirectRewritesOriginalSpellsChosenTarget()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice's two creatures — the original target and the redirected target.
        var originalTarget = new Creature("OriginalBear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        var redirectedTarget = new Creature("DecoyBear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };

        // Bob casts Bolt → originalTarget. Mirror what SpellCastFlow does for
        // a single-target spell: push a Spell with one ChosenTargets entry.
        var bolt = new Instant("Bolt", "R") { Owner = bob, Zone = ZoneType.Stack };
        var boltSpell = new Spell(
            bolt, bob,
            effects: new[] { new Effect("dmg", () => originalTarget.TakeDamage(3)) });
        boltSpell.ChosenTargets.Add(originalTarget);
        stack.Push(boltSpell);

        // Alice casts Deflection on Bob's Bolt. The Deflection template's
        // effect is one call to SpellRedirector.RedirectTopSpellSingleTarget.
        var def = new RedirectTemplate().TryBind(Ctx(
            "Change the target of target spell with a single target.",
            stack));
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new[] { (object)redirectedTarget } },
            Mana: new ManaPayment(Array.Empty<ICard>()),
            AllPlayers: new[] { alice, bob });
        var effects = def!.EffectFactory(chosen);

        // Run Deflection's effect.
        foreach (var e in effects) e.Execute();

        // The Bolt's ChosenTargets should now be the redirectedTarget,
        // observable through the stack-resident Spell.
        boltSpell.ChosenTargets.Should().HaveCount(1);
        boltSpell.ChosenTargets[0].Should().BeSameAs(redirectedTarget);
    }

    [Fact]
    public void Integration_NoEligibleSpell_ReturnsFalse_NoCrash()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bob = new Player("Bob", 20);

        // Stack is empty — redirector cannot find anything to redirect.
        SpellRedirector.RedirectTopSpellSingleTarget(stack, bob).Should().BeFalse();
    }

    [Fact]
    public void SpellRedirector_PicksMostRecentSingleTargetSpell()
    {
        var stack = new Majik.Core.Stack.Stack();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear1 = new Creature("B1", "1G", 1, 1) { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var bear2 = new Creature("B2", "1G", 1, 1) { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var newTarget = new Creature("B3", "1G", 1, 1) { Owner = bob, Controller = bob, Zone = ZoneType.Battlefield };

        // Two single-target spells stacked; the second one (most recent / top)
        // is the one that should be retargeted.
        var older = new Spell(
            new Instant("Older", "R") { Owner = alice, Zone = ZoneType.Stack }, alice,
            effects: Array.Empty<IEffect>());
        older.ChosenTargets.Add(bear1);

        var newer = new Spell(
            new Instant("Newer", "R") { Owner = alice, Zone = ZoneType.Stack }, alice,
            effects: Array.Empty<IEffect>());
        newer.ChosenTargets.Add(bear2);

        stack.Push(older);
        stack.Push(newer);

        SpellRedirector.RedirectTopSpellSingleTarget(stack, newTarget).Should().BeTrue();

        // Top spell rewritten, older spell untouched.
        newer.ChosenTargets[0].Should().BeSameAs(newTarget);
        older.ChosenTargets[0].Should().BeSameAs(bear1);
    }

    [Fact]
    public void SpellRedirector_SkipsMultiTargetSpells()
    {
        var stack = new Majik.Core.Stack.Stack();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear1 = new Creature("B1", "1G", 1, 1) { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var bear2 = new Creature("B2", "1G", 1, 1) { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var single = new Creature("B3", "1G", 1, 1) { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var newTarget = new Creature("B4", "1G", 1, 1) { Owner = bob, Controller = bob, Zone = ZoneType.Battlefield };

        // Most recent has two targets — must be skipped.
        var multi = new Spell(
            new Instant("Multi", "RR") { Owner = alice, Zone = ZoneType.Stack }, alice,
            effects: Array.Empty<IEffect>());
        multi.ChosenTargets.Add(bear1);
        multi.ChosenTargets.Add(bear2);

        var singleSpell = new Spell(
            new Instant("Single", "R") { Owner = alice, Zone = ZoneType.Stack }, alice,
            effects: Array.Empty<IEffect>());
        singleSpell.ChosenTargets.Add(single);

        // Order: single (bottom), multi (top).
        stack.Push(singleSpell);
        stack.Push(multi);

        SpellRedirector.RedirectTopSpellSingleTarget(stack, newTarget).Should().BeTrue();

        // Multi unchanged, single rewritten.
        multi.ChosenTargets[0].Should().BeSameAs(bear1);
        multi.ChosenTargets[1].Should().BeSameAs(bear2);
        singleSpell.ChosenTargets[0].Should().BeSameAs(newTarget);
    }

    [Fact]
    public void OracleSpellBinder_RegistersRedirectTemplate()
    {
        // Walk the registry and confirm the template is present.
        Majik.Core.CardData.OracleSpellBinder.Registry.OrderedTemplates
            .Should().Contain(t => t.Name == "Redirect");
    }
}

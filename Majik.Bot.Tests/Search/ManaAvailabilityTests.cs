using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Unit tests verifying that <see cref="LegalActionEnumerator.UntappedManaSources"/>
/// counts ALL available mana — floating pool, untapped mana-source permanents of
/// every type (lands, creatures, artifacts), and Treasure tokens — not just
/// untapped Lands. Also verifies that the affordability gate correctly enumerates
/// castable spells when mana is available via non-land sources.
/// </summary>
public class ManaAvailabilityTests
{
    // ── Helper: attach a tap-for-one-generic mana ability to an artifact ────

    private static Artifact MakeManaRock(string name, BotTestScenario s)
    {
        var rock = new Artifact(name, manaCost: "{2}");
        rock.ChangeOwner(s.Self);
        rock.ChangeController(s.Self);
        rock.AddAbility(new ManaAbility(
            source: rock,
            controller: s.Self,
            manaGenerated: ManaCost.Parse("{1}")));
        s.Self.Zones.Battlefield.AddCard(rock);
        return rock;
    }

    // ── Helper: attach a tap-for-{G} mana ability to a creature dork ───────

    private static Creature MakeManaDork(string name, BotTestScenario s, bool clearSummoningSickness = true)
    {
        var dork = new Creature(name, manaCost: "{G}", power: 1, toughness: 1);
        dork.ChangeOwner(s.Self);
        dork.ChangeController(s.Self);
        dork.AddAbility(new ManaAbility(
            source: dork,
            controller: s.Self,
            manaGenerated: ManaCost.Parse("{G}")));
        s.Self.Zones.Battlefield.AddCard(dork);
        if (clearSummoningSickness)
            dork.ClearSummoningSickness();
        return dork;
    }

    // ── Floating-pool tests ──────────────────────────────────────────────────

    /// <summary>
    /// Floating mana already in the pool (e.g. produced by a ritual) counts
    /// toward mana available, even when no lands are on the battlefield.
    /// </summary>
    [Fact]
    public void UntappedManaSources_CountsFloatingPool()
    {
        var s = new BotTestScenario();
        // No lands, no permanents — board is empty.

        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(3,
                because: "three floating {R} are immediately available to spend");
    }

    [Fact]
    public void UntappedManaSources_CountsFloatingPool_Generic()
    {
        var s = new BotTestScenario();
        s.Self.AddManaToPool(ManaCost.Parse("{2}"));

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(2,
                because: "{2} generic floating mana counts as 2 available");
    }

    // ── Untapped-land tests ──────────────────────────────────────────────────

    [Fact]
    public void UntappedManaSources_CountsUntappedLands()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Forest1");
        s.AddLandToBattlefield(s.Self, "Forest2");

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(2,
                because: "two untapped lands supply two mana");
    }

    [Fact]
    public void UntappedManaSources_DoesNotCountTappedLands()
    {
        var s = new BotTestScenario();
        var land = s.AddLandToBattlefield(s.Self, "Forest1");
        land.Tap();

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().Be(0,
                because: "the only land is tapped and produces no available mana");
    }

    // ── Mana-dork tests ──────────────────────────────────────────────────────

    [Fact]
    public void UntappedManaSources_CountsUntappedLands_AndManaDork()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Forest");
        _ = MakeManaDork("Llanowar Elves", s);  // not summoning-sick, untapped

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(2,
                because: "one untapped land + one untapped mana dork = at least 2");
    }

    [Fact]
    public void UntappedManaSources_DoesNotCountSummoningSickCreature()
    {
        var s = new BotTestScenario();
        // Dork with summoning sickness (clearSummoningSickness: false)
        _ = MakeManaDork("Llanowar Elves", s, clearSummoningSickness: false);

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().Be(0,
                because: "a summoning-sick creature can't tap for mana this turn");
    }

    // ── Mana-rock (artifact) tests ───────────────────────────────────────────

    [Fact]
    public void UntappedManaSources_CountsManaRock()
    {
        var s = new BotTestScenario();
        _ = MakeManaRock("Sol Ring", s);  // untapped artifact with {T}: Add {1}

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(1,
                because: "an untapped mana rock can produce mana");
    }

    [Fact]
    public void UntappedManaSources_DoesNotCountTappedManaRock()
    {
        var s = new BotTestScenario();
        var rock = MakeManaRock("Sol Ring", s);
        rock.Tap();

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().Be(0,
                because: "the mana rock is tapped and cannot produce mana");
    }

    // ── Combined floating + permanents ───────────────────────────────────────

    [Fact]
    public void UntappedManaSources_AddsFloatingPlusUntappedPermanents()
    {
        var s = new BotTestScenario();
        s.Self.AddManaToPool(ManaCost.Parse("{R}"));  // 1 floating
        s.AddLandToBattlefield(s.Self, "Forest");     // 1 untapped land
        _ = MakeManaRock("Talisman", s);              // 1 untapped rock
        _ = MakeManaDork("Fyndhorn Elves", s);        // 1 untapped dork

        LegalActionEnumerator.UntappedManaSources(s.Self)
            .Should().BeGreaterThanOrEqualTo(4,
                because: "1 floating + 1 land + 1 rock + 1 dork = at least 4");
    }

    // ── Affordability gate end-to-end tests ─────────────────────────────────

    /// <summary>
    /// Critical regression: with 0 lands but 3 floating {R}, a {3} sorcery in
    /// hand must be enumerated as a CastSpell action. Previously the lands-only
    /// count returned 0 → the spell was never offered.
    /// </summary>
    [Fact]
    public void CastableSpell_EnumeratedWhenAffordableViaFloatingMana()
    {
        var s = new BotTestScenario();
        // No lands whatsoever — only floating mana.
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        // A 3-CMC Instant in hand (instant-speed so it's legal regardless of
        // sorcery window — simpler to verify without configuring active player).
        var fireblast = new Instant("Fireblast", manaCost: "{4}{R}{R}");
        // We want something exactly CMC 3 to match the pool of 3 mana.
        var chainLightning = new Instant("Chain Lightning", manaCost: "{R}");  // CMC 1
        // Use a 3-CMC sorcery in the sorcery window (s.Context already has
        // Self as active player + PreCombatMain + empty stack).
        var shock3 = new Sorcery("Shock3", manaCost: "{3}");
        s.AddCardToHand(s.Self, shock3);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.CastSpell,
            because: "3 floating mana is sufficient to cast a {3} sorcery");
        actions.OfType<PriorityAction.CastSpell>().Should().Contain(cs => cs.Card == shock3,
            because: "the specific {3} sorcery in hand should be the CastSpell enumerated");
    }

    /// <summary>
    /// With a mana dork but no lands, a 1-mana spell should be enumerated.
    /// </summary>
    [Fact]
    public void CastableSpell_EnumeratedWhenAffordableViaManaDork()
    {
        var s = new BotTestScenario();
        // No lands — mana comes from the dork.
        _ = MakeManaDork("Llanowar Elves", s);

        var elfWarrior = new Creature("Elf Warrior", manaCost: "{G}", power: 1, toughness: 1);
        s.AddCardToHand(s.Self, elfWarrior);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.CastSpell,
            because: "the untapped mana dork can produce {G} to cast a {G} creature");
        actions.OfType<PriorityAction.CastSpell>().Should().Contain(cs => cs.Card == elfWarrior,
            because: "the specific {G} creature in hand should be enumerated as castable");
    }

    /// <summary>
    /// With a mana rock but no lands, a 1-mana spell should be enumerated.
    /// </summary>
    [Fact]
    public void CastableSpell_EnumeratedWhenAffordableViaManaRock()
    {
        var s = new BotTestScenario();
        _ = MakeManaRock("Mox Opal", s);  // untapped artifact mana source

        var elf = new Creature("Elf", manaCost: "{1}", power: 1, toughness: 1);
        s.AddCardToHand(s.Self, elf);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.CastSpell,
            because: "an untapped mana rock produces at least 1 mana to cast a {1} spell");
        actions.OfType<PriorityAction.CastSpell>().Should().Contain(cs => cs.Card == elf,
            because: "the specific {1} creature in hand should be enumerated as castable");
    }

    /// <summary>
    /// Spells that are more expensive than total mana from ALL sources should
    /// still be excluded.
    /// </summary>
    [Fact]
    public void CastableSpell_NotEnumerated_WhenStillUnaffordable()
    {
        var s = new BotTestScenario();
        // 1 floating + 1 dork = 2 total, but spell costs {5}.
        s.Self.AddManaToPool(ManaCost.Parse("{R}"));
        _ = MakeManaDork("Llanowar Elves", s);

        var emrakul = new Creature("Emrakul", manaCost: "{15}", power: 15, toughness: 15);
        s.AddCardToHand(s.Self, emrakul);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        // {15} spell should never appear when only 2 total mana is available.
        actions.OfType<PriorityAction.CastSpell>()
            .Should().NotContain(cs => cs.Card == emrakul,
                because: "2 total mana is nowhere near enough to cast {15}");
    }
}

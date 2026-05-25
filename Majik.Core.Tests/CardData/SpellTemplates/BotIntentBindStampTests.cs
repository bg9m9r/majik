using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// End-to-end: oracle text → bind → SpellDefinition. Verifies that the
/// matched template's <see cref="BotIntent"/> stamps onto each
/// <c>TargetRequest.Intent</c> via the registry's centralized stamp.
/// </summary>
public class BotIntentBindStampTests
{
    private readonly Player _alice = new("Alice", 20);

    private SpellDefinition Bind(string oracleText, string typeLine = "Instant",
        string name = "Test")
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = name, OracleText = oracleText, TypeLine = typeLine },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        return def!;
    }

    [Fact]
    public void DamageAnyTarget_StampsBurnReach()
    {
        var def = Bind("Test deals 3 damage to any target.");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Burn | BotIntent.Reach);
    }

    [Fact]
    public void DamageCreature_StampsBurn()
    {
        var def = Bind("Test deals 3 damage to target creature.");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Burn);
    }

    [Fact]
    public void DestroyCreature_StampsRemoval()
    {
        var def = Bind("Destroy target creature.");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void Counterspell_StampsCounter()
    {
        var def = Bind("Counter target spell.");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Counter);
    }

    [Fact]
    public void PumpCreature_StampsBuffCombatTrick()
    {
        var def = Bind("Target creature gets +3/+3 until end of turn.");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Buff | BotIntent.CombatTrick);
    }

    [Fact]
    public void DestroyAllCreatures_NoTargets_NoStamp()
    {
        // Wrath-style — no TargetRequests; stamp is a no-op but bind succeeds.
        var def = Bind("Destroy all creatures.", typeLine: "Sorcery");
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void ModalSpell_ModeIntents_MapsPerClauseIntent()
    {
        // Bullet-prefixed mode bodies — mirrors real MTG oracle formatting
        // (e.g. Boros Charm) that ModalChooseOneTemplate splits on.
        var def = Bind(
            "Choose one —\n• Destroy target creature.\n• Draw two cards.");
        def.Modes.Should().HaveCount(2);
        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.ModeIntentsOrEmpty[0].Should().Be(BotIntent.Removal);
        def.ModeIntentsOrEmpty[1].Should().Be(BotIntent.Draw);
    }

    [Fact]
    public void NonModalSpell_ModeIntents_Empty()
    {
        var def = Bind("Destroy target creature.");
        def.ModeIntentsOrEmpty.Should().BeEmpty();
    }
}

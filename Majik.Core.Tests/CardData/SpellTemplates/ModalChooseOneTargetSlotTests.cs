using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// CR 700.2d / 601.2c — a modal spell bound by <c>ModalChooseOneTemplate</c>
/// collects targets per mode (SpellCastFlow lays the slots out by mode), and
/// each mode's body must read ITS OWN slot. Before the fix every mode body got
/// the whole target list and read slot 0 — the first mode's slot — so Drown in
/// the Loch's destroy mode threw (issue 3544).
/// </summary>
public class ModalChooseOneTargetSlotTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>The slot layout SpellCastFlow.CollectTargetsAsync builds for
    /// the chosen <paramref name="mode"/>: keyed by ModeIndex when every request
    /// carries one; index-aligned when there is one request per mode; otherwise
    /// one slot per request, all collected.</summary>
    private static IReadOnlyList<IReadOnlyList<object>> EngineLayout(SpellDefinition def, int mode, object target, int flatSlot = -1)
    {
        var reqs = def.TargetRequests;
        if (reqs.Count > 0 && reqs.All(r => r.ModeIndex.HasValue))
        {
            var slots = Enumerable.Range(0, def.Modes.Count).Select(_ => (IReadOnlyList<object>)Array.Empty<object>()).ToArray();
            foreach (var r in reqs.Where(r => r.ModeIndex == mode)) slots[mode] = new[] { target };
            return slots;
        }
        if (reqs.Count == def.Modes.Count)
        {
            return reqs.Select((_, i) => i == mode ? (IReadOnlyList<object>)new[] { target } : Array.Empty<object>()).ToArray();
        }
        // Flat: one slot per request; the caller names the chosen mode's slot.
        return reqs.Select((_, i) => i == flatSlot ? (IReadOnlyList<object>)new[] { target } : Array.Empty<object>()).ToArray();
    }

    private static void Run(SpellDefinition def, int mode, IReadOnlyList<IReadOnlyList<object>> targets)
    {
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: mode, X: null, Targets: targets, Mana: ManaPayment.Empty,
            AllPlayers: null, ModeIndexes: new[] { mode }));
        foreach (var e in effects) e.Execute();
    }

    private Creature OnBattlefield(string name, Player owner)
    {
        var c = new Creature(name, "{1}", 1, 1);
        c.SetOwner(owner); c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void DrownInTheLoch_DestroyMode_ReadsItsOwnSlot()
    {
        var entity = new EmbeddedCardRepository().GetByName("Drown in the Loch")!;
        var def = OracleSpellBinder.Bind(entity, _alice, raw => raw, new Majik.Core.Stack.Stack())!;
        def.Modes.Should().HaveCount(2);
        var destroyMode = def.Modes.ToList().FindIndex(m => m.StartsWith("Destroy", StringComparison.OrdinalIgnoreCase));
        destroyMode.Should().Be(1);

        var bear = OnBattlefield("Grizzly Bears", _bob);
        FluentActions.Invoking(() => Run(def, destroyMode, EngineLayout(def, destroyMode, bear)))
            .Should().NotThrow();
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void AModeAfterAnUntargetedMode_ReadsItsOwnSlot_NotTheFirstTargetedModesSlot()
    {
        // Three modes, two of them targeted: the third mode's body must get the
        // third mode's target, not the second's.
        var def = OracleSpellBinder.Bind(new CardEntity
        {
            Name = "Test Charm", TypeLine = "Instant",
            OracleText = "Choose one —\n• Draw a card.\n• Destroy target artifact.\n• Destroy target creature.",
        }, _alice, raw => raw, null)!;
        def.Modes.Should().HaveCount(3);

        var bear = OnBattlefield("Grizzly Bears", _bob);
        Run(def, 2, EngineLayout(def, 2, bear, flatSlot: 1));
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }
}

using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Mana;

/// <summary>
/// Slot-level mana-provenance ledger on <see cref="Player"/> (deferral #1).
/// Each unit of colored mana produced by a provenance-stamped source records
/// a <see cref="ManaProvenanceSlot"/> tagging the producing
/// <see cref="IManaAbility"/> (CR 106.4 — provenance is per-slot, not
/// player-scoped). Plain mana adds no slots. Provenance dies with the
/// floating mana when the pool empties (CR 500.4).
/// </summary>
public class ManaProvenanceLedgerTests
{
    private static ManaAbility RedSource(Player owner)
    {
        var land = new Land("Source");
        land.SetOwner(owner);
        land.SetController(owner);
        return new ManaAbility(land, owner, ManaCost.Parse("RR"));
    }

    [Fact]
    public void AddManaToPool_WithProvenance_RecordsOneSlotPerColoredUnit()
    {
        var alice = new Player("Alice", 20);
        var src = RedSource(alice);

        alice.AddManaToPool(ManaCost.Parse("RR"), src);

        alice.ManaProvenance.Should().HaveCount(2);
        alice.ManaProvenance.Should().OnlyContain(s =>
            s.Color == ManaColor.Red && ReferenceEquals(s.Source, src));
    }

    [Fact]
    public void AddManaToPool_WithoutProvenance_RecordsNoSlots()
    {
        var alice = new Player("Alice", 20);

        alice.AddManaToPool(ManaCost.Parse("RR"));

        alice.ManaProvenance.Should().BeEmpty();
    }

    [Fact]
    public void AddManaToPool_GenericProvenance_RecordsNoSlots()
    {
        // Provenance only meaningful for colored mana — generic pips aren't
        // tagged (no color to match at spend time).
        var alice = new Player("Alice", 20);
        var land = new Land("Sol Ring-ish");
        land.SetOwner(alice);
        land.SetController(alice);
        var src = new ManaAbility(land, alice, ManaCost.Parse("2"));

        alice.AddManaToPool(ManaCost.Parse("2"), src);

        alice.ManaProvenance.Should().BeEmpty();
    }

    [Fact]
    public void EmptyManaPool_ClearsProvenance()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"), RedSource(alice));
        alice.ManaProvenance.Should().HaveCount(1);

        alice.EmptyManaPool();

        alice.ManaProvenance.Should().BeEmpty();
    }

    [Fact]
    public void RemoveProvenanceSlots_RemovesUpToCount_ByColorAndSource()
    {
        var alice = new Player("Alice", 20);
        var src = RedSource(alice);
        alice.AddManaToPool(ManaCost.Parse("RRR"), src);

        var removed = alice.RemoveProvenanceSlots(src, ManaColor.Red, 2);

        removed.Should().Be(2);
        alice.ManaProvenance.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveProvenanceSlots_ClampsToAvailable()
    {
        var alice = new Player("Alice", 20);
        var src = RedSource(alice);
        alice.AddManaToPool(ManaCost.Parse("R"), src);

        var removed = alice.RemoveProvenanceSlots(src, ManaColor.Red, 5);

        removed.Should().Be(1);
        alice.ManaProvenance.Should().BeEmpty();
    }
}

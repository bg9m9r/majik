using FluentAssertions;
using Majik.Core.Cards;
using Xunit;

public class PermanentAttachmentTests
{
    [Fact]
    public void AttachTo_SetsAttachedTo_AndAddsToTargetAttachments()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        var aura = new Enchantment("Aura", "1G");

        aura.AttachTo(bear);

        aura.AttachedTo.Should().BeSameAs(bear);
        bear.Attachments.Should().ContainSingle().Which.Should().BeSameAs(aura);
    }

    [Fact]
    public void Unattach_ClearsBothEnds()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        var aura = new Enchantment("Aura", "1G");
        aura.AttachTo(bear);

        aura.Unattach();

        aura.AttachedTo.Should().BeNull();
        bear.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void AttachTo_NewTarget_RemovesFromOld()
    {
        var bear1 = new Creature("Bear1", "1G", 2, 2);
        var bear2 = new Creature("Bear2", "1G", 2, 2);
        var equipment = new Artifact("Sword", "2");
        equipment.AttachTo(bear1);

        equipment.AttachTo(bear2);

        equipment.AttachedTo.Should().BeSameAs(bear2);
        bear1.Attachments.Should().BeEmpty();
        bear2.Attachments.Should().ContainSingle();
    }

    [Fact]
    public void AttachTo_Self_Throws()
    {
        var aura = new Enchantment("Aura", "1G");

        var act = () => aura.AttachTo(aura);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AttachTo_SameTarget_NoOp()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        var aura = new Enchantment("Aura", "1G");
        aura.AttachTo(bear);

        aura.AttachTo(bear);

        bear.Attachments.Should().HaveCount(1);
    }
}

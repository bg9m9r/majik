using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.MultiFace;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards.MultiFace;

public class MultiFaceCardTests
{
    private readonly Player _alice = new("Alice", 20);

    private Card NewCard(string name = "Test Card") =>
        new(name, "1U") { Owner = _alice, Zone = ZoneType.Hand };

    // -------- IFaceTransform contract --------

    [Fact]
    public void PlotTransform_ApplyMarksActive_RevertClears()
    {
        var card = NewCard("Plot Card");
        var plot = new PlotFaceTransform();
        var ctx = new FaceContext();

        plot.IsActive(card).Should().BeFalse();

        plot.Apply(card, ctx);
        plot.IsActive(card).Should().BeTrue();

        plot.Revert(card, ctx);
        plot.IsActive(card).Should().BeFalse();
    }

    [Fact]
    public void PlotTransform_Apply_IsIdempotent()
    {
        var card = NewCard();
        var plot = new PlotFaceTransform();
        var ctx = new FaceContext();

        plot.Apply(card, ctx);
        plot.Apply(card, ctx);

        plot.IsActive(card).Should().BeTrue();
    }

    [Fact]
    public void PlotTransform_Revert_IsIdempotent()
    {
        var card = NewCard();
        var plot = new PlotFaceTransform();
        var ctx = new FaceContext();

        plot.Revert(card, ctx);
        plot.Revert(card, ctx);

        plot.IsActive(card).Should().BeFalse();
    }

    // -------- MultiFaceCard wrapper --------

    [Fact]
    public void MultiFaceCard_TransformSetsActive_UntransformClears()
    {
        var card = NewCard();
        var plot = new PlotFaceTransform();
        var mfc = new MultiFaceCard(card, new IFaceTransform[] { plot });
        var ctx = new FaceContext();

        mfc.ActiveTransform.Should().BeNull();

        mfc.Transform(plot, ctx);
        mfc.ActiveTransform.Should().BeSameAs(plot);
        plot.IsActive(card).Should().BeTrue();

        mfc.Untransform(ctx);
        mfc.ActiveTransform.Should().BeNull();
        plot.IsActive(card).Should().BeFalse();
    }

    [Fact]
    public void MultiFaceCard_Transform_RejectsUnregisteredTransform()
    {
        var card = NewCard();
        var registered = new PlotFaceTransform();
        var unregistered = new PlotFaceTransform();
        var mfc = new MultiFaceCard(card, new IFaceTransform[] { registered });

        Action act = () => mfc.Transform(unregistered, new FaceContext());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not registered*");
    }

    [Fact]
    public void MultiFaceCard_OnlyOneTransformActiveAtATime()
    {
        var card = NewCard();
        var a = new TestFaceTransform("A");
        var b = new TestFaceTransform("B");
        var mfc = new MultiFaceCard(card, new IFaceTransform[] { a, b });
        var ctx = new FaceContext();

        mfc.Transform(a, ctx);
        mfc.ActiveTransform.Should().BeSameAs(a);
        a.IsActive(card).Should().BeTrue();
        b.IsActive(card).Should().BeFalse();

        // Switching transforms reverts the previous one before applying the next.
        mfc.Transform(b, ctx);
        mfc.ActiveTransform.Should().BeSameAs(b);
        a.IsActive(card).Should().BeFalse();
        b.IsActive(card).Should().BeTrue();
    }

    [Fact]
    public void MultiFaceCard_Untransform_NoActive_IsNoOp()
    {
        var card = NewCard();
        var mfc = new MultiFaceCard(card, new IFaceTransform[] { new PlotFaceTransform() });

        Action act = () => mfc.Untransform(new FaceContext());

        act.Should().NotThrow();
        mfc.ActiveTransform.Should().BeNull();
    }

    [Fact]
    public void TestFixture_TransformCanRegisterAgainstCard_AndApplyCorrectly()
    {
        // Plug-in registration test — a brand-new IFaceTransform type
        // attaches via MultiFaceCard with no engine-side wiring required.
        var card = NewCard();
        var t = new TestFaceTransform("Custom");
        var mfc = new MultiFaceCard(card, new IFaceTransform[] { t });
        var ctx = new FaceContext();

        mfc.AvailableTransforms.Should().ContainSingle().Which.Should().BeSameAs(t);

        mfc.Transform(t, ctx);
        t.ApplyCalls.Should().Be(1);
        t.RevertCalls.Should().Be(0);
        t.IsActive(card).Should().BeTrue();

        mfc.Untransform(ctx);
        t.RevertCalls.Should().Be(1);
    }

    // -------- Fixture --------

    private sealed class TestFaceTransform : IFaceTransform
    {
        private readonly HashSet<Guid> _active = new();
        public string Name { get; }
        public int ApplyCalls { get; private set; }
        public int RevertCalls { get; private set; }

        public TestFaceTransform(string name) { Name = name; }

        public void Apply(ICard card, FaceContext ctx)
        {
            ApplyCalls++;
            _active.Add(card.InstanceId);
        }

        public void Revert(ICard card, FaceContext ctx)
        {
            RevertCalls++;
            _active.Remove(card.InstanceId);
        }

        public bool IsActive(ICard card) => _active.Contains(card.InstanceId);
    }
}

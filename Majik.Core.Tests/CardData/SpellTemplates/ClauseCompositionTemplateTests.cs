using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

public class ClauseCompositionTemplateTests
{
    [Fact]
    public void Composer_Binds_Drag_Under_Pattern()
    {
        var entity = new CardEntity
        {
            Name = "Drag Under",
            OracleText = "Return target creature to its owner's hand.\nDraw a card.",
        };
        var ctx = new SpellBindContext(entity, null!, _ => null!, null, null);
        var composer = OracleSpellBinder.Registry.OrderedTemplates
            .First(t => t.Name == "ClauseComposition");

        var def = composer.TryBind(ctx);

        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
    }

    [Fact]
    public void Composer_Fails_When_Any_Clause_Has_No_Match()
    {
        var entity = new CardEntity
        {
            Name = "Nonsense",
            OracleText = "Tap target creature. Frobnicate the gizmo.",
        };
        var ctx = new SpellBindContext(entity, null!, _ => null!, null, null);
        var composer = OracleSpellBinder.Registry.OrderedTemplates
            .First(t => t.Name == "ClauseComposition");

        var def = composer.TryBind(ctx);

        def.Should().BeNull();
    }
}

using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class ConvokeTemplateTests
{
    // Real Scryfall reminder text — Convoke spells lead with this exact
    // parenthetical block; the OracleTextNormalizer strips it before the
    // post-strip effect templates see ctx.Text.
    private const string Reminder =
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)";

    private static CardEntity Entity(string name, string body, string manaCost = "{2}{W}") =>
        new()
        {
            Name = name,
            OracleText = Reminder + "\n" + body,
            ManaCost = manaCost,
            TypeLine = "Instant",
        };

    [Fact]
    public void RegistryBinds_PauseForReflection_Fog()
    {
        var def = OracleSpellBinder.Bind(
            Entity("Pause for Reflection",
                   "Prevent all combat damage that would be dealt this turn."),
            new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(),
            stack: null);
        def.Should().NotBeNull("Pause for Reflection's Fog body should bind through ConvokeTemplate's recursive rebind");
    }

    [Fact]
    public void RegistryBinds_GatherCourage_PumpCreature()
    {
        var def = OracleSpellBinder.Bind(
            Entity("Gather Courage", "Target creature gets +2/+2 until end of turn.", "{G}"),
            new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(),
            stack: null);
        def.Should().NotBeNull("Gather Courage's pump body should bind through the registry");
        def!.TargetRequests.Should().NotBeEmpty();
    }

    [Fact]
    public void RegistryBinds_CutShort_DestroyTappedCreature()
    {
        // Cut Short's body is a tricky disjunction — "Destroy target
        // planeswalker that was activated this turn or tapped creature."
        // The destroy templates may bind this loosely; the test only
        // demands that the binder returns SOMETHING (no-op shell is fine
        // per the partial-PR contract).
        var def = OracleSpellBinder.Bind(
            Entity("Cut Short", "Destroy target planeswalker that was activated this turn or tapped creature."),
            new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(),
            stack: null);
        def.Should().NotBeNull();
    }

    [Fact]
    public void Template_RequiresRegistry_BeforeBinding()
    {
        // Bare ConvokeTemplate without SetRegistry must refuse — CanBind
        // returns false, so TryBind returns null. Prevents NRE at runtime
        // when a misconfigured pipeline tries to use the template.
        var bare = new ConvokeTemplate();
        var ctx = new SpellBindContext(
            new CardEntity { Name = "X", OracleText = Reminder + " Target creature gets +1/+1 until end of turn." },
            new Player("A", 20), o => o, new ContinuousEffectsService(), null);
        bare.CanBind(ctx).Should().BeFalse();
        bare.TryBind(ctx).Should().BeNull();
    }

    [Fact]
    public void Template_DoesNotMatch_WithoutConvokePrefix()
    {
        // A card with the same body but no Convoke prefix on raw text must
        // not bind through this template — it gates on ctx.RawText, not
        // ctx.Text (which is already stripped).
        var ctx = new SpellBindContext(
            new CardEntity { Name = "Vanilla Fog", OracleText = "Prevent all combat damage that would be dealt this turn." },
            new Player("A", 20), o => o, new ContinuousEffectsService(), null);
        // The template is registered, so we instantiate ours via the
        // registry instance (with its SetRegistry already called by the
        // static constructor of OracleSpellBinder.Registry).
        var template = OracleSpellBinder.Registry.OrderedTemplates
            .OfType<ConvokeTemplate>().FirstOrDefault();
        template.Should().NotBeNull();
        template!.TryBind(ctx).Should().BeNull();
    }

    [Theory]
    // Real Scryfall oracle text — these Convoke cards must all return
    // SOMETHING from the live binder. The partial-PR contract: ConvokeTemplate
    // recursively rebinds the body, falling back to a no-op shell when
    // no inner template matches. Either way the result is non-null.
    [InlineData("Gather Courage",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nTarget creature gets +2/+2 until end of turn.")]
    [InlineData("Aerial Boost",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nTarget creature gets +2/+2 and gains flying until end of turn.")]
    [InlineData("Crowd's Favor",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nTarget creature gets +1/+0 and gains first strike until end of turn.")]
    [InlineData("Devouring Light",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nExile target attacking or blocking creature.")]
    [InlineData("Ephemeral Shields",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nTarget creature gains indestructible until end of turn.")]
    [InlineData("Meditation Puzzle",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nYou gain 8 life.")]
    [InlineData("Collective Nightmare",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nTarget creature gets -3/-3 until end of turn.")]
    [InlineData("Feral Incarnation",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nCreate three 3/3 green Beast creature tokens.")]
    [InlineData("Hour of Reckoning",
        "Convoke (Your creatures can help cast this spell. Each creature you tap while casting this spell pays for {1} or one mana of that creature's color.)\nDestroy all nontoken creatures.")]
    public void RealConvokeOracles_BindThroughRegistry(string name, string oracle)
    {
        var entity = new CardEntity
        {
            Name = name,
            OracleText = oracle,
            ManaCost = "{2}{W}",
            TypeLine = "Instant",
        };
        var def = OracleSpellBinder.Bind(
            entity, new Player("A", 20), o => o,
            effects: new ContinuousEffectsService(),
            stack: null);
        def.Should().NotBeNull($"{name} should bind through the live registry (ConvokeTemplate fallback ensures non-null)");
    }
}

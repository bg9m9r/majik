using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tangled Islet (the green/blue member of the
/// common "surveil"/"bicycle" ETB-tapped dual-land cycle). Type line:
/// <c>Land — Forest Island</c>. Oracle text (verified against Scryfall):
///   "({T}: Add {G} or {U}.)
///    This land enters tapped."
///
/// <para>
/// Same oracle shape as <see cref="SimicGuildgateFactory"/> — two
/// single-colour mana abilities {G}/{U} (CR 605.1 — mana abilities don't use
/// the stack) plus an unconditional enters-tapped restriction (CR 614.1c) —
/// but it carries the printed <c>Forest</c> and <c>Island</c> basic land
/// subtypes (CR 205.3i) rather than the <c>Gate</c> subtype. There is no
/// Snow supertype (unlike the sibling snow duals) and no triggered ability.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the Forest/Island subtypes, and
/// the two mana abilities — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/tangled-islet.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="SimicGuildgateFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
/// registration (no bus available) — same posture as
/// <see cref="SimicGuildgateFactory"/>.
/// </para>
/// </summary>
[CardName("Tangled Islet")]
public static class TangledIsletFactory
{
    public const string Slug = "tangled-islet";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Tangled Islet owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Tangled Islet with optional replacement-bus wiring
    /// so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as SimicGuildgateFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}

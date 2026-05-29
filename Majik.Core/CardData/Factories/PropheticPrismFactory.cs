using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prophetic Prism (Conflux / Modern Masters et al.,
/// {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-05-29):
///   "When this artifact enters, draw a card.
///    {1}, {T}: Add one mana of any color."
///
/// A cantrip mana rock — the colour-fixing twin of
/// <see cref="PrismaticLensFactory"/> (Time Spiral). The two share the
/// "{1}, {T}: Add one mana of any color" body; the differences are:
///   1. Prophetic Prism has no free "{T}: Add {C}" ability (the Lens does).
///   2. Prophetic Prism cantrips via a leaves-... no — an enters-the-
///      battlefield triggered ability ("When this artifact enters, draw a
///      card"), whereas the Lens has no trigger.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>When this artifact enters, draw a card</b> — a single
///   <see cref="Majik.Core.Abilities.TriggeredAbility"/> on the
///   <c>etb_self</c> condition (<see cref="Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf"/>),
///   carrying a <c>draw_card</c> effect (amount 1). CR 603.6 — an
///   enters-the-battlefield trigger; it uses the stack. CR 120.2 — its
///   controller draws one card on resolution. Empty library is a silent
///   no-op in the effect closure; the loss is handled by SBAs elsewhere
///   (CR 104.3c / 704.5c). Same ETB-trigger shape as
///   <see cref="LibrarySurveyorFactory"/> (etb_self → surveil).
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG), each carrying the optional additional <c>cost</c> field
///   ("1"). <see cref="CardDefinitionFactory"/> threads that through the
///   additional-cost overload of <see cref="Majik.Core.Abilities.ManaAbility"/>
///   — gating activation on the untapped state plus affordability of the
///   {1} pip, and deducting it from the pool on activation. Identical
///   "Add one mana of any color" filter posture as
///   <see cref="PrismaticLensFactory"/> / <see cref="ChromaticStarFactory"/>:
///   one <c>ManaAbility</c> per colour, the bot's source-picker selecting
///   the colour at payment time. CR 605.1 — a mana ability; never uses the
///   stack; the {1} is part of the activation cost, not a resolution
///   effect.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/prophetic-prism.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>. The only engine shapes used are the
/// etb_self triggered ability with a draw_card effect and the
/// additional-cost mana ability, both already supported.
/// </summary>
[CardName("Prophetic Prism")]
public static class PropheticPrismFactory
{
    public const string CardName = "Prophetic Prism";
    public const string Slug = "prophetic-prism";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Prophetic Prism owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}

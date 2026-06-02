using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prophetic Prism (Conflux, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-02):
///   "When this artifact enters, draw a card.
///    {1}, {T}: Add one mana of any color."
///
/// ## Fully JSON-driven
/// Like <see cref="BorosSignetFactory"/>, Prophetic Prism's entire shape is
/// expressed in <c>Majik.Core/CardData/Cards/prophetic-prism.json</c>, so this
/// factory is a thin loader: it materializes the JSON definition through
/// <see cref="CardDefinitionFactory"/> and returns the finished
/// <see cref="Artifact"/> — no abilities are added here.
///
/// The JSON encodes:
/// <list type="bullet">
///   <item><b>"When this artifact enters, draw a card"</b> — a
///     <c>triggered</c> ability on the <c>etb_self</c> condition
///     (<see cref="Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf"/>)
///     carrying a <c>draw_card</c> effect (amount 1). CR 603.6 — an
///     enters-the-battlefield trigger; it uses the stack. CR 120.2 — its
///     controller draws one card on resolution. Empty library is a silent
///     no-op in the effect closure; the loss is handled by SBAs elsewhere
///     (CR 104.3c / 704.5c).</item>
///   <item><b>"{1}, {T}: Add one mana of any color"</b> — five <c>mana</c>
///     abilities (one per WUBRG), each with <c>cost: "1"</c>. The JSON mana
///     encoding routes a <c>cost</c>-bearing entry through the additional-cost
///     <see cref="Majik.Core.Abilities.ManaAbility"/> constructor:
///     <c>canActivateCheck</c> = <c>!IsTapped &amp;&amp; ManaPool.CanPay({1})</c>
///     (the {T} untap half plus the {1} affordability gate) and
///     <c>additionalCostPayer</c> = <c>PayMana({1})</c>, with {T} applied as a
///     cost on activation. CR 605.1 — the produced coloured mana ability never
///     uses the stack. Modeling "add one mana of any color" as five distinct
///     single-colour mana abilities is the engine's standard modal-colour
///     posture (Chromatic Star / Lotus Petal / Energy Refractor): the bot's
///     source-picker selects the right colour at payment time.</item>
/// </list>
///
/// Colour-fixing twin of Prismatic Lens / Energy Refractor; the difference is
/// the ETB cantrip trigger (and the absence of any free {C} ability — Prophetic
/// Prism only fixes coloured mana for {1}).
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color" is
///   bound as five separate mana abilities — same posture as Chromatic Star /
///   Lotus Petal / Energy Refractor.
/// - <b>{1} sourcing</b>: activation requires {1} already in the mana pool; the
///   engine does not auto-tap other sources to feed the cost (no look-ahead
///   mana planner) — identical to every other additional-mana-cost mana
///   ability (signets, filter lands).
/// </summary>
[CardName("Prophetic Prism")]
public static class PropheticPrismFactory
{
    public const string CardName = "Prophetic Prism";
    public const string Slug = "prophetic-prism";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Prophetic Prism owned and controlled by <paramref name="owner"/>.
    /// Card identity, the ETB cantrip trigger, and the five "{1}, {T}: Add
    /// &lt;color&gt;" mana abilities all come from the embedded JSON definition.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}

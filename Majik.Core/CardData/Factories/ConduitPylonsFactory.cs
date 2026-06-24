using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conduit Pylons (Edge of Eternities).
///
/// Land — Desert. Oracle text (verified against Scryfall 2026-06-24):
///   "When this land enters, surveil 1. (Look at the top card of your library.
///    You may put it into your graveyard.)
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// ## Fully JSON-driven
/// Like <see cref="PropheticPrismFactory"/> and the Crystal Grotto definition,
/// Conduit Pylons' entire shape is expressed in
/// <c>Majik.Core/CardData/Cards/conduit-pylons.json</c>, so this factory is a
/// thin loader: it materializes the JSON definition through
/// <see cref="CardDefinitionFactory"/> and returns the finished
/// <see cref="Land"/> — no abilities are added here.
///
/// Conduit Pylons is the colour-fixing twin of Crystal Grotto — identical
/// ability shape (free {C} + five {1}-gated any-colour modes + an ETB library
/// peek) — the only differences are the <c>Desert</c> land subtype and that the
/// ETB peek is <b>surveil 1</b> (CR 701.42) rather than scry 1.
///
/// The JSON encodes:
/// <list type="bullet">
///   <item><b>"When this land enters, surveil 1"</b> — a <c>triggered</c>
///     ability on the <c>etb_self</c> condition
///     (<see cref="Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf"/>)
///     carrying a <c>surveil_self</c> effect (amount 1). CR 603.6 — an
///     enters-the-battlefield trigger; it uses the stack. CR 701.42 — on
///     resolution the controller looks at the top card of their library and may
///     put it into their graveyard; the registered agent makes the choice,
///     falling back to all-to-graveyard when no agent is registered. An empty
///     library is a silent no-op.</item>
///   <item><b>"{T}: Add {C}"</b> — one cost-free colourless mana ability
///     (CR 605.1a); activatable from an empty pool.</item>
///   <item><b>"{1}, {T}: Add one mana of any color"</b> — five <c>mana</c>
///     abilities (one per WUBRG), each with <c>cost: "1"</c>. The JSON mana
///     encoding routes a <c>cost</c>-bearing entry through the additional-cost
///     <see cref="Majik.Core.Abilities.ManaAbility"/> constructor (the {T} untap
///     half plus the {1} affordability gate). CR 605.1 — the produced coloured
///     mana ability never uses the stack. Modeling "add one mana of any color"
///     as five distinct single-colour mana abilities is the engine's standard
///     modal-colour posture (Crystal Grotto / Prophetic Prism).</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color" is
///   bound as five separate mana abilities — same posture as Crystal Grotto /
///   Prophetic Prism.
/// - <b>{1} sourcing</b>: activation requires {1} already in the mana pool; the
///   engine does not auto-tap other sources to feed the cost — identical to
///   every other additional-mana-cost mana ability (signets, filter lands).
/// </summary>
[CardName("Conduit Pylons")]
public static class ConduitPylonsFactory
{
    public const string CardName = "Conduit Pylons";
    public const string Slug = "conduit-pylons";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Conduit Pylons owned and controlled by <paramref name="owner"/>.
    /// Card identity, the ETB surveil-1 trigger, the {T}: Add {C} ability, and
    /// the five "{1}, {T}: Add &lt;color&gt;" mana abilities all come from the
    /// embedded JSON definition.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}

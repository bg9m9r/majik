using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mana Cylix (Hour of Devastation, {1}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-06-23):
///   "{1}, {T}: Add one mana of any color."
///
/// ## Fully JSON-driven
/// Like <see cref="PropheticPrismFactory"/>, Mana Cylix's entire shape is
/// expressed in <c>Majik.Core/CardData/Cards/mana-cylix.json</c>, so this
/// factory is a thin loader: it materializes the JSON definition through
/// <see cref="CardDefinitionFactory"/> and returns the finished
/// <see cref="Artifact"/> — no abilities are added here.
///
/// The JSON encodes "{1}, {T}: Add one mana of any color" as five <c>mana</c>
/// abilities (one per WUBRG), each with <c>cost: "1"</c>. The JSON mana
/// encoding routes a <c>cost</c>-bearing entry through the additional-cost
/// <see cref="Majik.Core.Abilities.ManaAbility"/> constructor:
/// <c>canActivateCheck</c> = <c>!IsTapped &amp;&amp; ManaPool.CanPay({1})</c>
/// (the {T} untap half plus the {1} affordability gate) and
/// <c>additionalCostPayer</c> = <c>PayMana({1})</c>, with {T} applied as a cost
/// on activation. CR 605.1 — the produced coloured mana ability never uses the
/// stack. Modeling "add one mana of any color" as five distinct single-colour
/// mana abilities is the engine's standard modal-colour posture (Prophetic
/// Prism / Prismatic Lens / Chromatic Star): the bot's source-picker selects
/// the right colour at payment time.
///
/// Colour-fixing twin of Prophetic Prism, minus the ETB cantrip (and minus the
/// free {C} ability of Prismatic Lens): Mana Cylix only fixes coloured mana for
/// {1}.
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color" is
///   bound as five separate mana abilities — same posture as Prophetic Prism /
///   Chromatic Star.
/// - <b>{1} sourcing</b>: activation requires {1} already in the mana pool; the
///   engine does not auto-tap other sources to feed the cost (no look-ahead
///   mana planner) — identical to every other additional-mana-cost mana
///   ability (signets, filter lands).
/// </summary>
[CardName("Mana Cylix")]
public static class ManaCylixFactory
{
    public const string CardName = "Mana Cylix";
    public const string Slug = "mana-cylix";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mana Cylix owned and controlled by <paramref name="owner"/>.
    /// Card identity and the five "{1}, {T}: Add &lt;color&gt;" mana abilities
    /// all come from the embedded JSON definition.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}

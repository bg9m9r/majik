using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wren's Run Vanquisher (Lorwyn / reprints,
/// {1}{G}).
///
/// Creature — Elf Warrior 3/3. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, reveal an Elf card from
///    your hand or pay {3}.
///    Deathtouch (Any amount of damage this deals to a creature is enough
///    to destroy it.)"
///
/// ## Implemented (v1)
/// - <b>Creature — Elf Warrior {1}{G} 3/3, green</b>. The card shape comes
///   from the embedded JSON definition (<c>wrens-run-vanquisher.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the JSON carries no
///   abilities, and the keyword markers below are layered on afterward
///   (same pattern as <see cref="GlissaSunslayerFactory"/>).
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker. <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/>
///   consumes it for lethal-damage determination (same shape as
///   <see cref="TyphoidRatsFactory"/> / <see cref="GlissaSunslayerFactory"/>).
/// - <b>Reveal-an-Elf-or-pay-{3} additional cost (CR 601.2b)</b>:
///   represented as a structural-only <see cref="KeywordAbility"/> marker
///   ("RevealElfOrPay3") — the Elf-typed sibling of
///   <see cref="SilvergillAdeptFactory"/>'s "RevealMerfolkOrPay3" reveal
///   cost. The actual cast-time enforcement (agent prompt: reveal an Elf
///   card from hand OR pay {3} as an additional cost) is deferred until the
///   additional-cost framework supports reveal-based alternatives. In
///   production the cost MUST be enforced before the spell resolves.
///
/// ## Deferred (v1 gaps — shared with the reveal-cost card family)
/// - <b>Reveal-or-pay enforcement</b>: the additional cast cost is a marker
///   only; the cast-time framework must prompt the controller to either
///   reveal an Elf card from hand or pay {3} before allowing the spell to
///   be placed on the stack (CR 601.2b). Same gap as Silvergill Adept.
/// - <b>Reveal event</b>: the production reveal-an-Elf path should emit a
///   <see cref="Majik.Core.Domain.DomainEvents.CardRevealedEvent"/>. No
///   reveal event is emitted in v1.
///
/// ## Rules citations
/// - CR 601.2b — additional cost chosen/paid at announcement (reveal-or-pay).
/// - CR 702.2 — Deathtouch (any nonzero damage to a creature is lethal).
/// - CR 205.3m — Elf / Warrior creature subtypes.
///
/// No triggers, no activated abilities — a deathtouch creature with a
/// reveal-or-pay additional cast cost. Single-arg <see cref="Create(Player)"/>
/// is the canonical entry point.
/// </summary>
[CardName("Wren's Run Vanquisher")]
public static class WrensRunVanquisherFactory
{
    public const string CardName = "Wren's Run Vanquisher";
    public const string Slug = "wrens-run-vanquisher";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Wren's Run Vanquisher — a {1}{G} 3/3 Creature — Elf Warrior
    /// with a Deathtouch marker and the reveal-an-Elf-or-pay-{3}
    /// additional-cost marker. The base shape is loaded from the embedded
    /// JSON definition; the keyword markers are layered on afterward.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // CR 702.2 — Deathtouch. KeywordAbility marker consumed by
        // Majik.Core.Combat.CombatAbilities.HasDeathtouch for lethal-damage
        // determination.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // Additional cast cost marker — CR 601.2b.
        //   "As an additional cost to cast this spell, reveal an Elf card
        //    from your hand or pay {3}."
        // v1: structural-only keyword marker; actual cost enforcement at
        // cast-time is deferred (sibling of Silvergill Adept's
        // RevealMerfolkOrPay3). Production callers MUST enforce this cost
        // before the spell is placed on the stack. See class xmldoc.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("RevealElfOrPay3", card, owner));

        return card;
    }
}

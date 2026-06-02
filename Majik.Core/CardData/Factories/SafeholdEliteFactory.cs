using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Safehold Elite (Shadowmoor, {1}{G/W}).
/// Creature — Elf Scout 2/2. Oracle text (verified against Scryfall):
///   "Persist (When this creature dies, if it had no -1/-1 counters on it,
///    return it to the battlefield under its owner's control with a -1/-1
///    counter on it.)"
///
/// Safehold Elite is a vanilla Persist body — the negative-counter mirror of
/// Young Wolf's vanilla Undying body, and Kitchen Finks minus the ETB lifegain
/// rider.
///
/// The base shape (name, Creature, Elf + Scout subtypes, {1}{G/W}, 2/2) is
/// materialised from the embedded JSON definition (<c>safehold-elite.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Persist is layered on in code —
/// the JSON <c>AbilityDefinition</c> schema carries neither the Persist keyword
/// marker nor the death-trigger mechanic (same posture as
/// <see cref="DanithaCapashenParagonFactory"/> for code-layered keywords).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Scout, mana cost {1}{G/W} (CR 107.4e hybrid pip —
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> decomposes {G/W} into
///   a HybridPip, same as Kitchen Finks).
/// - <b>Persist (CR 702.79)</b>: wired via the shared
///   <see cref="PersistFactory.Build(Creature)"/> primitive, which attaches the
///   "Persist" keyword marker and the Battlefield → Graveyard death trigger with
///   the "no -1/-1 counter" interveningIf gate (CR 603.4). On resolution the
///   creature returns to the battlefield with exactly one -1/-1 counter
///   (CR 702.79b).
/// </summary>
[CardName("Safehold Elite")]
public static class SafeholdEliteFactory
{
    public const string CardName = "Safehold Elite";
    public const string Slug = "safehold-elite";

    /// <summary>
    /// Construct Safehold Elite owned and controlled by <paramref name="owner"/>.
    /// The Persist keyword marker + death trigger are attached to the card by
    /// <see cref="PersistFactory.Build(Creature)"/>; call
    /// <see cref="Majik.Core.Services.TriggerManager.BindCard"/> on the returned
    /// creature to register the trigger with a live trigger manager so it fires
    /// on bus events.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf + Scout, {1}{G/W}, 2/2). No abilities in the JSON — Persist is
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.79 — Persist. The shared primitive attaches the keyword
        // marker + the death trigger (with the "no -1/-1 counter" interveningIf).
        PersistFactory.Build(card);

        return card;
    }
}

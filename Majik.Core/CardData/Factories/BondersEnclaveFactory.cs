using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonders' Enclave (Commander Legends / reprints).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {3}, {T}: Draw a card. Activate only if you control a creature with
///    power 4 or greater."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Bonders'
/// Enclave enters untapped (no ETB-tapped clause).
///
/// ## Card identity + abilities come from JSON
///
/// Name / type, the <b>{T}: Add {C}</b> mana ability, and the
/// <b>{3}, {T}: Draw a card</b> activated ability are loaded from the embedded
/// JSON definition (<c>bonders-enclave.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The activated draw uses the standard
/// <c>draw_card</c> path so any future <see cref="Majik.Core.Effects.DrawCardIntent"/>
/// replacements (Dredge, etc.) participate. Same JSON-driven posture as
/// <see cref="CastleVantressFactory"/> (mana + activated ability from JSON).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype
///   (from JSON).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1), from JSON.
/// - <b>{3}, {T}: Draw a card</b> — <see cref="ActivatedAbility"/> whose cost
///   stack is a ManaCostCost({3}) + a tap-self additional cost, resolving the
///   standard single-card <c>draw_card</c> effect (CR 120), from JSON.
///
/// ## Deferred (v1 gaps)
/// - The "Activate only if you control a creature with power 4 or greater"
///   gate (CR 602.5) is exposed via the public predicate
///   <see cref="ControlsCreatureWithPower4OrGreater"/> for activator / bot
///   policy probing, but is not yet wired into the
///   <see cref="ActivatedAbility"/>'s CanActivate gate — the engine's
///   <see cref="ActivatedAbility"/> does not yet expose a generic
///   activation-legality closure (same posture as
///   <see cref="SeaGateWreckageFactory.HasNoCardsInHand"/>'s empty-hand gate
///   and Magmatic Channeler's delirium-style gate). When the
///   activation-legality surface ships the predicate will be wired into
///   <see cref="ActivatedAbility"/> directly.
/// </summary>
[CardName("Bonders' Enclave")]
public static class BondersEnclaveFactory
{
    public const string CardName = "Bonders' Enclave";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bonders-enclave");

    /// <summary>
    /// Construct Bonders' Enclave. Identity, the {T}: Add {C} mana ability, and
    /// the {3},{T}: Draw a card activated ability all come from JSON. There is
    /// no ETB-tapped clause, so no <see cref="ReplacementBus"/> overload is
    /// needed.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        return land;
    }

    /// <summary>
    /// CR 602.5 — Bonders' Enclave's "Activate only if you control a creature
    /// with power 4 or greater" gate. Scans the controller's battlefield for
    /// at least one <see cref="Creature"/> whose live <see cref="Creature.Power"/>
    /// (after continuous effects, CR 613) is 4 or greater. Reads
    /// <see cref="Card.Controller"/> live so control-change effects re-point the
    /// scan. Returns false when the controller is not yet assigned.
    /// </summary>
    public static bool ControlsCreatureWithPower4OrGreater(Land enclave)
    {
        ArgumentNullException.ThrowIfNull(enclave);
        var controller = enclave.Controller ?? enclave.Owner;
        if (controller is null) return false;
        return controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.Power >= 4);
    }
}

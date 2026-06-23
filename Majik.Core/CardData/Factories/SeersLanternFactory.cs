using System;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seer's Lantern (Khans of Tarkir / Dragons of
/// Tarkir, {3}).
///
/// Artifact. Oracle text (Scryfall-confirmed 2026-06):
///   "{T}: Add {C}.
///    {2}, {T}: Scry 1. (Look at the top card of your library. You may put
///    that card on the bottom.)"
///
/// Scryfall type line: Artifact (no subtype). Mana cost {3}.
///
/// ## Card identity + abilities come from JSON
///
/// Name / type / {3} mana cost and both abilities are loaded from the embedded
/// JSON definition (<c>seers-lantern.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>:
///
/// - <b>{T}: Add {C}</b> — a vanilla colourless <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (JSON <c>{ "kind": "mana", "produces": "C" }</c>, the same {C} mode as
///   Crystal Grotto). CR 605.1 — mana abilities don't use the stack; the {C}
///   pip is colourless (CR 107.4c), counted as a colourless generic in v1's
///   <see cref="Majik.Core.ValueObjects.ManaCost"/>.
/// - <b>{2}, {T}: Scry 1</b> — an <see cref="Majik.Core.Abilities.ActivatedAbility"/>
///   (JSON <c>{ "kind": "activated", costs:[{2} mana, tap_self], effects:[scry_self 1] }</c>).
///   Unlike the scry-land cycle (where scry 1 is an ETB <i>triggered</i>
///   ability) Seer's Lantern's scry is a repeatable activated ability paid for
///   with {2} + {T}. The scry resolves through the standard <c>scry_self</c>
///   path (CR 701.20 — scry keyword action): with a registered
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> the controller picks
///   the bottom/top partition; the pre-agent default puts the single peeked
///   card on the bottom. This activated ability DOES use the stack (CR 602.2 —
///   it is not a mana ability).
/// </summary>
[CardName("Seer's Lantern")]
public static class SeersLanternFactory
{
    public const string CardName = "Seer's Lantern";
    public const string PrintedManaCost = "{3}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("seers-lantern");

    /// <summary>
    /// Construct Seer's Lantern owned and controlled by <paramref name="owner"/>.
    /// Identity, the {T}: Add {C} mana ability, and the {2}, {T}: Scry 1
    /// activated ability all come from JSON.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}

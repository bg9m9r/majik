using System;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Command Tower (Commander 2011 + many reprints).
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add one mana of any color in your commander's color identity."
///
/// <para>
/// The Land shell (identity / owner / controller) is declared declaratively
/// in <c>Majik.Core/CardData/Cards/command-tower.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="CityOfBrassFactory"/> and <see cref="ManaConfluenceFactory"/>.
/// The any-colour mana abilities are attached on top in C# because the
/// data-only <see cref="ManaAbilityDefinition"/> schema only carries a
/// <c>Produces</c> string — it cannot express a five-colour any-colour
/// fan-out. The JSON therefore declares no mana abilities; this factory
/// adds them.
/// </para>
///
/// ## Modelling the "commander's color identity" clause
/// Command Tower's producible colours are gated by the controller's
/// commander's colour identity (CR 903.4). Majik is a one-versus-one
/// constructed engine: it has no Commander format, no command zone, and no
/// commander, so there is no commander colour identity to read. With no
/// commander defined, the faithful resolution of "any color in your
/// commander's color identity" is the full set of the five colours — the
/// same any-colour fan-out as <see cref="CityOfBrassFactory"/> (minus its
/// pain rider) and <see cref="ManaConfluenceFactory"/> (minus its life
/// cost). This matches how the suggested analogue lands resolve a
/// board-state-/format-dependent colour set the engine cannot narrow:
/// offer the whole colour set.
///
/// Modelled as five <see cref="ManaAbility"/> instances, one per WUBRG
/// (CR 605.1a — each colour is a separate mana ability). There is NO
/// <c>{C}</c> mode: "any <b>color</b>" never matches colorless mana
/// (CR 105.1 — there are five colours; colorless is not a colour). Each
/// ability is gated only on the land being untapped, since {T} is the sole
/// activation cost (no life, no pain — distinguishing Command Tower from
/// City of Brass / Mana Confluence). The mana picker chooses whichever
/// colour is needed when paying spell costs.
/// </summary>
[CardName("Command Tower")]
public static class CommandTowerFactory
{
    public const string CardName = "Command Tower";
    public const string Slug = "command-tower";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Command Tower owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add one mana of any color in your commander's color identity.
        //   No commander exists in this engine, so the colour identity is
        //   unbounded → offer all five colours. Five ManaAbility instances
        //   (one per WUBRG) — same any-colour fan-out as City of Brass /
        //   Mana Confluence, but with {T} as the only cost: no pain (CR 120.3)
        //   and no life payment (CR 119.4). {C} is excluded — colorless is
        //   not a colour (CR 105.1). Each gated only on the land untapped.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () => !land.IsTapped));
        }

        return land;
    }
}

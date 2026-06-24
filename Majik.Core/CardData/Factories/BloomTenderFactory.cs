using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloom Tender (Eventide, {1}{G}).
///
/// Creature — Elf Druid 1/1. Oracle text (verified against Scryfall):
///   "Vivid — {T}: For each color among permanents you control, add one
///    mana of that color."
///
/// <para>"Vivid" here is an ability-word flavour label (CR 207.2c) — it has
/// no rules meaning of its own (contrast the Vivid <em>lands</em>, whose
/// "Vivid" is part of a charge-counter mana ability). The whole rules text
/// is the single {T} mana ability.</para>
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid at printed cost {1}{G}, owner/controller
///   stamped. Identity / stats / subtypes built from
///   <c>Majik.Core/CardData/Cards/bloom-tender.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: For each color among permanents you control, add one mana of
///   that color (CR 605.1 — mana ability, no stack; CR 202.2 / CR 105 —
///   colours of permanents)</b>. Wired via the <see cref="ManaAbility"/>
///   <c>Func&lt;ManaCost&gt;</c> dynamic-generator overload (the same shape
///   as <see cref="ElvishArchdruidFactory"/>'s Cradle-style {T}). At
///   activation the generator scans the controller's battlefield, unions
///   the <see cref="Permanent.GetEffectiveColors"/> of every permanent the
///   controller controls (CR 105.3 / 613 — effective colour after the
///   Layer-5 colour pass, so "becomes blue" / "is all colors" riders are
///   honoured), and returns a <see cref="ManaCost"/> with one pip of each
///   distinct WUBRG colour present. Colourless permanents contribute no
///   colour; an all-colourless board produces no mana.
///
/// ## X-count / colour-set semantics
/// - Evaluated at activation (CR 605.1 — mana abilities resolve atomically;
///   same snapshot posture as Elvish Archdruid's {T}).
/// - INCLUDES Bloom Tender itself — it is a green permanent the controller
///   controls, so a lone Bloom Tender taps for {G} (oracle reads "permanents
///   you control" with no "other" qualifier).
/// - Counts permanents on the controller's battlefield only (CR 109.5 —
///   "you control" = controller, not opponents). One mana per <em>distinct</em>
///   colour, regardless of how many permanents share it.
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning sickness gate</b>: the {T} mana ability is gated centrally
///   by <see cref="SummoningSicknessTapGate"/> inside
///   <see cref="ManaAbility.CanActivate"/> (CR 302.6 / 605.3a) — a freshly
///   cast Bloom Tender without haste can't tap for mana. Same posture as
///   Elvish Archdruid / Birds of Paradise.
/// </summary>
[CardName("Bloom Tender")]
public static class BloomTenderFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bloom-tender");

    /// <summary>
    /// Construct Bloom Tender owned and controlled by <paramref name="owner"/>,
    /// with the Vivid {T} mana ability wired.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Vivid — {T}: For each color among permanents you control, add one
        // mana of that color.
        //
        // CR 605.1 — mana ability (no stack). CR 105.3 / 613.1e — "color of
        // a permanent" is its effective colour after the Layer-5 colour
        // pass, so we read GetEffectiveColors() per permanent. One mana per
        // DISTINCT WUBRG colour present on the controller's battlefield;
        // colourless permanents contribute nothing.
        //
        // Wired via the Func<ManaCost> generator overload so the colour set
        // is re-scanned at each activation (CR 605.1 snapshot).
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () => BuildVividMana(card.Controller ?? owner),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }

    /// <summary>
    /// Build "one mana of each distinct colour among the permanents
    /// <paramref name="controller"/> controls", in WUBRG order. Reads each
    /// permanent's <see cref="Permanent.GetEffectiveColors"/> (CR 105.3 /
    /// 613) and unions them; colourless permanents add no pip. Returns
    /// <see cref="ManaCost.Zero"/> when no coloured permanent is present.
    /// </summary>
    public static ManaCost BuildVividMana(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var colors = new HashSet<ManaColor>();
        foreach (var permanent in controller.Zones.Battlefield.GetCards().OfType<Permanent>())
        {
            foreach (var color in permanent.GetEffectiveColors())
            {
                colors.Add(color);
            }
        }

        if (colors.Count == 0) return ManaCost.Zero;

        // WUBRG order — only the five real colours produce a pip (CR 105.1);
        // GetEffectiveColors already excludes Generic / Colorless.
        var pips = new System.Text.StringBuilder();
        foreach (var (color, pip) in WubrgPips)
        {
            if (colors.Contains(color)) pips.Append(pip);
        }

        return ManaCost.Parse(pips.ToString());
    }

    private static readonly (ManaColor Color, string Pip)[] WubrgPips =
    {
        (ManaColor.White, "{W}"),
        (ManaColor.Blue, "{U}"),
        (ManaColor.Black, "{B}"),
        (ManaColor.Red, "{R}"),
        (ManaColor.Green, "{G}"),
    };
}

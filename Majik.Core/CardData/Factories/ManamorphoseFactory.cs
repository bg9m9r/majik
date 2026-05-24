using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Manamorphose (Shadowmoor, {1}{R/G}).
///
/// Instant. Oracle text:
///   "Add two mana in any combination of colors. Draw a card."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost <c>{1}{R/G}</c> — CR 107.4e hybrid pip
///   parsed by <see cref="ValueObjects.ManaCost.Parse"/> into 1 generic +
///   one <see cref="HybridPip"/>(Red, Green). Total mana value = 2.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) adds two mana
///   to the caster's mana pool. The pair is selected by an optional
///   caller-supplied <see cref="ManaColor"/>[] picker — v1 default is
///   <c>{R}{G}</c> (the simplest "any combination" answer that exercises
///   the multi-color path). Then draws one card.
/// - Empty library: caster is flagged for the SBA-driven loss (CR 704.5b)
///   via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>; the two
///   mana still land in the pool first (resolution order matches printed
///   text — add mana, then draw).
///
/// ## Deferred (v1 gaps)
/// - No agent prompt for "choose two mana colors" — <see cref="IPlayerAgent"/>
///   has no <c>ChooseManaColorsAsync</c> hook today. Callers can pre-pick a
///   colour pair via the 2-arg <see cref="BuildResolveEffect"/> overload;
///   the single-arg dispatcher path uses the default {R}{G}.
/// - Net mana-effect bookkeeping for cost-reduction restrictions
///   (CR 106.11b — Manamorphose generates two mana while costing two, so
///   it is net-zero) isn't tracked because the engine has no mana-provenance
///   ledger yet (same gap as Cavern of Souls' spend-restriction).
/// </summary>
[CardName("Manamorphose")]
public static class ManamorphoseFactory
{
    public const string CardName = "Manamorphose";
    public const string PrintedManaCost = "{1}{R/G}";

    /// <summary>
    /// Build a Manamorphose instant owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can plug it
    /// into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Manamorphose's resolve effect — add two mana to
    /// <paramref name="caster"/>'s mana pool, then draw a card.
    /// </summary>
    /// <param name="caster">The resolving controller.</param>
    /// <param name="colorPicker">
    /// Optional selector returning the two colours to add. The returned
    /// array must contain exactly two <see cref="ManaColor"/> entries
    /// drawn from WUBRG or <see cref="ManaColor.Colorless"/> (any
    /// generic-typed entries are normalised to <see cref="ManaColor.Colorless"/>
    /// so the deposit lands as generic mana). When <see langword="null"/>,
    /// the v1 default of <c>{R}{G}</c> is used — the simplest pair that
    /// exercises Manamorphose's multi-color knob.
    /// </param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, Func<Player, ManaColor[]>? colorPicker = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Manamorphose: add two mana in any combination, then draw a card.", () =>
            {
                // ----------------------------------------------------------
                // CR 106.1 — "Add two mana in any combination of colors."
                // The picker chooses two ManaColor entries; any non-WUBRG
                // value is treated as generic / colorless so the deposit
                // lands somewhere legal even if the caller passed something
                // exotic. Defaults to {R}{G}.
                // ----------------------------------------------------------
                var picked = colorPicker?.Invoke(caster) ?? new[] { ManaColor.Red, ManaColor.Green };
                if (picked.Length != 2)
                {
                    throw new InvalidOperationException(
                        $"Manamorphose color picker must return exactly 2 colors (got {picked.Length}).");
                }

                // Build the deposit as a printed-cost string and parse it
                // back into a ManaCost — ManaCost has no public bag-style
                // constructor, only Parse. "RG" / "WW" / "1G" all parse to
                // the expected bucket counts. Non-WUBRG colours (Colorless /
                // Generic / unexpected) land in the generic bucket — exactly
                // what we want for a colourless deposit.
                var sb = new System.Text.StringBuilder();
                int generic = 0;
                foreach (var c in picked)
                {
                    switch (c)
                    {
                        case ManaColor.White: sb.Append('W'); break;
                        case ManaColor.Blue: sb.Append('U'); break;
                        case ManaColor.Black: sb.Append('B'); break;
                        case ManaColor.Red: sb.Append('R'); break;
                        case ManaColor.Green: sb.Append('G'); break;
                        default: generic++; break;
                    }
                }
                var depositStr = (generic > 0 ? generic.ToString() : string.Empty) + sb;
                var deposit = ValueObjects.ManaCost.Parse(depositStr);
                caster.AddManaToPool(deposit);

                // ----------------------------------------------------------
                // CR 121.1 — "Draw a card." Simple top-of-library draw;
                // empty library flags the player for the SBA-driven loss
                // (CR 704.5b) via MarkTriedToDrawFromEmptyLibrary.
                // ----------------------------------------------------------
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }),
        };
    }
}

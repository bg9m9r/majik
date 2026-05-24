using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cling to Dust (Theros Beyond Death, {B}).
///
/// Instant. Oracle text (this build's interpretation):
///   "Choose one —
///    • Exile target card from a graveyard. You gain life equal to its
///      mana value.
///    • Exile target card from a graveyard. Draw a card and you lose 1
///      life."
///
/// CR 700.2d — modal "Choose one —". Each mode targets a card in a
/// graveyard and exiles it (CR 701.21); the two modes differ only in
/// the rider (lifegain by mv vs. cantrip + 1 life loss).
///
/// ## Implementation
///
/// Mirrors <see cref="ArchmagesCharmFactory"/>'s modal shape. The bound
/// <see cref="SpellDefinition"/> exposes two <see cref="TargetRequest"/>s
/// (one per mode), each with <c>MinTargets=0</c> so the unchosen mode
/// doesn't gate the cast. Lifegain in mode 0 reads the exiled card's
/// <see cref="Card.ManaCostValue"/> total (CR 202.3b — "mana value");
/// mode 1 draws one card from the controller's library and subtracts 1
/// from their life total.
///
/// ## Escape (CR 702.138)
///
/// Wired via <see cref="EscapeAlternativeCost"/>. Cling's printed
/// Escape cost is {3}{B}, "Exile five other cards from your graveyard."
/// <see cref="BuildAlternativeCost"/> returns the bound alt-cost
/// instance; the modal resolve body is unchanged — Escape only changes
/// how the spell is cast, not its on-resolution effect.
/// </summary>
[CardName("Cling to Dust")]
public static class ClingToDustFactory
{
    public const string CardName = "Cling to Dust";
    public const string PrintedManaCost = "{B}";

    /// <summary>CR 702.138 — printed Escape mana cost: {3}{B}.</summary>
    public const string EscapeManaCost = "{3}{B}";

    /// <summary>CR 702.138a — Escape rider: exile five OTHER cards from
    /// your graveyard.</summary>
    public const int EscapeExileCount = 5;

    /// <summary>
    /// CR 702.138 — Cling to Dust's printed Escape alt-cost ({2}{B},
    /// exile five OTHER graveyard cards). Mana cost replaces the
    /// printed {B}; the modal resolve body is unchanged.
    /// </summary>
    public static EscapeAlternativeCost BuildAlternativeCost() =>
        new(ValueObjects.ManaCost.Parse(EscapeManaCost), EscapeExileCount);

    /// <summary>Mode 0 — exile target card from a graveyard + gain mv life.</summary>
    public const int ModeExileGainLife = 0;

    /// <summary>Mode 1 — exile target card from a graveyard + draw 1 + lose 1.</summary>
    public const int ModeExileDrawLose = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Exile target card from a graveyard. You gain life equal to its mana value.",
        "Exile target card from a graveyard. Draw a card and you lose 1 life.",
    };

    /// <summary>
    /// Build a Cling to Dust instant owned by <paramref name="owner"/>.
    /// Card shape only; the bound <see cref="SpellDefinition"/> is built
    /// on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the resolve-time <see cref="SpellDefinition"/> for Cling to
    /// Dust. Two modes, each takes a single "target card in a graveyard"
    /// target (<c>MinTargets=0</c> so unchosen modes don't gate the cast).
    /// </summary>
    /// <param name="caster">Spell controller. Lifegain (mode 0) and life
    /// loss + draw (mode 1) target this player.</param>
    /// <param name="resolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var targetRequests = new[]
        {
            // Mode 0 — exile + gain mv life.
            new TargetRequest("target card in a graveyard", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 1 — exile + draw 1 + lose 1.
            new TargetRequest("target card in a graveyard", 0, 1, Array.Empty<object>(), BotIntent.Draw),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,  // exile + lifegain is GY hate
                BotIntent.Draw,     // exile + cantrip is selection
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex — same
                // shape as ArchmagesCharmFactory / CrypticCommandFactory.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeExileGainLife:
                            effectsOut.Add(BuildExileGainLifeEffect(caster, p, resolver));
                            break;
                        case ModeExileDrawLose:
                            effectsOut.Add(BuildExileDrawLoseEffect(caster, p, resolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildExileGainLifeEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Cling to Dust — exile target card from a graveyard; gain mv life", () =>
        {
            if (p.Targets.Count <= ModeExileGainLife) return;
            var slot = p.Targets[ModeExileGainLife];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ICard card) return;

            // CR 608.2b — illegal-target check at resolution. The card
            // must still be in a graveyard.
            if (card.Zone != ZoneType.Graveyard) return;

            // Snapshot mana value BEFORE the move — the moved card's
            // characteristics are stable, but we keep the read on the
            // pre-exile state to mirror "its mana value" wording (CR
            // 202.3b — mana value is a characteristic of the card).
            // ICard doesn't expose ManaCostValue, so route through the
            // concrete Card (every dispatched card derives from it; if
            // somehow it doesn't, mv defaults to 0 — lifegain of 0).
            var manaValue = card is Card concreteCard
                ? concreteCard.ManaCostValue.TotalValue
                : 0;

            OracleSpellBinder.MoveToExile(card);
            caster.GainLife(manaValue);
        });

    private static IEffect BuildExileDrawLoseEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Cling to Dust — exile target card from a graveyard; draw 1, lose 1", () =>
        {
            if (p.Targets.Count <= ModeExileDrawLose) return;
            var slot = p.Targets[ModeExileDrawLose];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ICard card) return;

            // CR 608.2b — illegal-target check at resolution.
            if (card.Zone != ZoneType.Graveyard) return;

            OracleSpellBinder.MoveToExile(card);

            // Draw 1 — top of caster's library to hand. Empty library
            // flags the player for state-based loss (CR 704.5b /
            // CR 120.3); same simplification as TreasureCruiseFactory's
            // draw loop.
            var top = caster.Zones.Library.GetCards().FirstOrDefault();
            if (top != null)
            {
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
            else
            {
                caster.MarkTriedToDrawFromEmptyLibrary();
            }

            // Lose 1 life — runs regardless of whether the draw was
            // successful (the printed text doesn't gate the loss on
            // the draw resolving).
            caster.LoseLife(1);
        });
}

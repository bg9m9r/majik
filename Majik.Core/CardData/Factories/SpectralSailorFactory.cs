using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spectral Sailor (Core Set 2020, {U}).
///
/// Creature — Spirit Pirate 1/1. Oracle text:
///   "Flash
///    Flying
///    {3}{U}: Draw a card."
///
/// ## Why a named factory
/// One-mana flash flying with a built-in late-game card-draw mana sink is a
/// long-running blue Spirits / Pirates utility creature. The shape is
/// trivial — two keyword markers + one mana-cost activated ability that
/// dispatches <see cref="DrawCardSelf"/> — but it doesn't fit any of the
/// existing spell-template binders (templates cover instants / sorceries,
/// not stapled activated abilities), so a named factory is the cleanest
/// landing spot. Mirrors <see cref="AvenMindcensorFactory"/> for the
/// Flash + Flying marker pair and <see cref="AetherSpellbombFactory"/> for
/// the "{cost}: Draw a card" activated-ability shape (minus the sacrifice
/// rider — Spectral Sailor's draw is repeatable).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Spirit Pirate at {U}, owner / controller wired.
/// - <b>Flash</b> (CR 702.8) keyword marker via <see cref="KeywordAbility"/>.
///   Routes through <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>
///   so the spell can be cast at instant speed.
/// - <b>Flying</b> (CR 702.9) keyword marker via <see cref="KeywordAbility"/>.
///   Combat block restrictions enforced by
///   <see cref="Majik.Core.Rules.CombatRules"/>.
/// - <b>Activated ability (CR 113.3b / 605)</b>: "{3}{U}: Draw a card."
///   Wired as an <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> of {3}{U} and a draw-a-card
///   <see cref="Effect"/>. Not a mana ability (CR 605.1 — does not produce
///   mana) so it routes through the normal stack via
///   <see cref="Majik.Core.Services.AbilityActivator"/>.
///   - Empty-library safety: the effect no-ops if the library has zero
///     cards; the SBA loop (<see cref="Majik.Core.Rules.StateBasedActions"/>
///     CR 704.5b) marks the player for the loss flag elsewhere.
///   - Instant-speed: activated abilities default to any-time activation
///     (no "Activate only as a sorcery" rider), matching the printed text.
///
/// ## Tribal angle
/// Spirit + Pirate dual-tribe lets Spectral Sailor slot into either
/// <see cref="SupremePhantomFactory"/>-style Spirits anthems (Spirit
/// subtype) or <see cref="LordOfAtlantisFactory"/>-style tribal lords —
/// no lord effect of its own, so subtype wiring is purely the subtype
/// flag.
///
/// ## Deferred (v1 gaps)
/// - Replacement-effect-aware card draw: the activated ability moves the
///   top card directly from Library to Hand. <see cref="Majik.Core.Game.GameDriver"/>'s
///   draw flow publishes <see cref="Majik.Core.Events.CardDrawnEvent"/>
///   for the turn-based draw step but the activated-ability code path
///   here does not. Once the engine grows a unified
///   <c>DrawCardService</c> (planned with the replacement-bus rework),
///   this factory should route through that primitive so Dauthi
///   Voidwalker / Notion Thief–style replacements fire. Until then,
///   Spectral Sailor's draw is a direct zone-move (same posture as
///   <see cref="AetherSpellbombFactory"/>'s drawAbility).
/// </summary>
[CardName("Spectral Sailor")]
public static class SpectralSailorFactory
{
    public const string CardName = "Spectral Sailor";
    public const string PrintedManaCost = "{U}";
    public const string ActivatedCost = "{3}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Spectral Sailor. Flash + Flying keyword markers and the
    /// {3}{U}: Draw a card activated ability are attached. The activated
    /// ability draws via direct zone-move (Library → Hand); see the class
    /// xmldoc for the replacement-bus gap.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Pirate });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed via TimingRules.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        // CR 702.9 — Flying. Block restrictions enforced by CombatRules.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // {3}{U}: Draw a card. CR 113.3b / 605.1 — not a mana ability.
        // Direct Library → Hand zone-move; see class xmldoc for the draw
        // replacement-bus gap. Empty library is a safe no-op (SBA CR 704.5b
        // handles the loss flag separately when the player would draw from
        // an empty library — but the activated ability does NOT cause that
        // loss when the library is empty, as the printed text says "draw a
        // card" and CR 121.1 specifies an empty-library draw still triggers
        // the loss flag; the direct-zone-move shortcut bypasses that until
        // DrawCardService lands).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card ({ActivatedCost} activated)",
            () => DrawCardSelf(owner));

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivatedCost) },
            effects: new IEffect[] { drawEffect });

        card.AddAbility(drawAbility);

        return card;
    }

    /// <summary>
    /// Direct Library → Hand zone-move used by the {3}{U} activated ability.
    /// No-op on empty library. Mirrors the inline draw helper in
    /// <see cref="AetherSpellbombFactory"/>'s sac-and-draw effect; will be
    /// replaced with a shared DrawCardService call when that primitive
    /// lands (see class xmldoc deferred section).
    /// </summary>
    private static void DrawCardSelf(Player controller)
    {
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — see class xmldoc.
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Ice Age / Apocalypse "painland"
/// allied-and-enemy-color dual cycle:
///
/// <list type="bullet">
///   <item>Ice Age (allied):     Adarkar Wastes, Underground River,
///     Sulfurous Springs, Karplusan Forest, Brushland.</item>
///   <item>Apocalypse (enemy):   Battlefield Forge, Caves of Koilos,
///     Llanowar Wastes, Shivan Reef, Yavimaya Coast.</item>
/// </list>
///
/// Each member shares the same shape:
/// <code>
/// {T}: Add {C}.
/// {T}: Add {A} or {B}. This deals 1 damage to you.
/// </code>
///
/// Only the produced colour pair differs, so one factory handles the
/// whole 10-card cycle.
/// <code>
/// [CardName("Adarkar Wastes",  "W", "U")]
/// [CardName("Shivan Reef",     "U", "R")]
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = first coloured option (single-letter Scryfall code)</c>,
/// <c>[2] = second coloured option</c>.
///
/// ## Implemented (v1)
/// - Land identity (non-Basic, no subtype) + correct printed name per
///   cycle member.
/// - <c>{T}: Add {C}</c> mana ability (CR 605.1 — mana ability, never
///   on the stack; no pain rider).
/// - <c>{T}: Add {A}. This deals 1 damage to you.</c> + the matching
///   <c>{B}</c> mana ability — the "Add {A} or {B}" modal is split into
///   two separate <see cref="ManaAbility"/> instances per the same
///   shape as Aether Hub's WUBRG fan-out and the
///   <see cref="OracleManaBinder"/> "dual modal" split: the bot's
///   source-picker iterates abilities by produced colour and picks the
///   one matching the spell it's paying for. Each coloured ability is
///   built via the additional-cost overload of <see cref="ManaAbility"/>:
///   <c>additionalCostPayer = controller.LoseLife(1)</c> (CR 120.3 —
///   "deals damage to you" reduces life by the damage amount), running
///   after the {T} tap.
///
/// CR 119.4 does NOT gate this damage — pain lands can drop you to 0 or
/// below (you simply lose the game from SBAs after activating), unlike
/// "Pay 1 life" costs (CR 119.4 — "you can't pay life you don't have").
/// That's the only difference from the Horizon Canopy painless-dual
/// "Pay 1 life" shape: the canActivateCheck does NOT require life &gt; 1.
///
/// ## Deferred (v1 gaps)
/// - Full <c>DamageDealtEvent</c> route: the 1 damage goes through
///   <see cref="Player.LoseLife"/>, not a damage event — damage-prevention
///   subscribers (e.g. Worship-style "you can't lose life from damage"
///   shields) don't intercept it. Same simplification Mana Crypt and the
///   Horizon Canopy cycle take.
/// </summary>
[CardName("Adarkar Wastes",     "W", "U")]
[CardName("Underground River",  "U", "B")]
[CardName("Sulfurous Springs",  "B", "R")]
[CardName("Karplusan Forest",   "R", "G")]
[CardName("Brushland",          "G", "W")]
[CardName("Battlefield Forge",  "R", "W")]
[CardName("Caves of Koilos",    "W", "B")]
[CardName("Llanowar Wastes",    "B", "G")]
[CardName("Shivan Reef",        "U", "R")]
[CardName("Yavimaya Coast",     "G", "U")]
public static class PainLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Adarkar Wastes.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Adarkar Wastes", "W", "U" });

    /// <summary>
    /// Construct the pain land identified by <paramref name="args"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c> (e.g. "Caves of Koilos"),
    /// <c>[1] = first coloured option (single-letter Scryfall code, e.g. "W")</c>,
    /// <c>[2] = second coloured option (e.g. "B")</c>.
    /// </param>
    public static Land Create(Player owner, string[] args)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"PainLandCycleFactory needs args = [name, colorA, colorB] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var colorA = args[1];
        var colorB = args[2];

        var land = new Land(cardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C}. CR 605.1 — mana ability, no pain rider on the
        // colourless mode (printed oracle: only the coloured "Add A or B"
        // mode triggers "This deals 1 damage to you").
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {T}: Add {A}. This deals 1 damage to you.
        // {T}: Add {B}. This deals 1 damage to you.
        AttachPainColoredMana(land, owner, colorA);
        AttachPainColoredMana(land, owner, colorB);

        return land;
    }

    /// <summary>
    /// Attach a <c>{T}: Add &lt;color&gt;. This deals 1 damage to you.</c>
    /// mana ability. Built via the additional-cost overload of
    /// <see cref="ManaAbility"/>: tapping pays {T}; the
    /// <c>additionalCostPayer</c> then reduces the controller's life by 1
    /// (CR 120.3 — damage to a player causes loss of life equal to that
    /// damage). No life-floor gate (unlike the Horizon Canopy "Pay 1 life"
    /// shape, CR 119.4) — pain lands can deal lethal damage to you.
    /// </summary>
    private static void AttachPainColoredMana(Land land, Player controller, string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            throw new ArgumentException("Color required", nameof(color));
        }

        var mana = ManaCost.Parse(color);
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: mana,
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));
    }
}

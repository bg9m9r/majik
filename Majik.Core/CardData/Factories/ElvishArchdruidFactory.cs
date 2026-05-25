using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Archdruid (Magic 2011 / many reprints,
/// Creature — Elf Druid {1}{G}{G}).
///
/// Oracle text:
///   "Other Elf creatures you control get +1/+1.
///    {T}: Add {G} for each Elf you control."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Druid, mana cost {1}{G}{G}, owner/controller wired.
/// - <b>"Other Elf creatures you control get +1/+1"</b> via
///   <see cref="LordStaticEffect"/> — <c>matchingSubtype: Elf</c>,
///   <c>power: 1, toughness: 1</c>, no granted keywords, <c>includeSelf:
///   false</c>. Same shape as Goblin Chieftain's stat-only half. CR 613.1g
///   Layer 7c. Scoped to the controller's battlefield (CR 109.5 — "you").
///   Lifts on LTB via <see cref="LordStaticEffect.IsActive"/>'s
///   battlefield gate.
/// - <b>{T}: Add {G} for each Elf you control</b> wired via
///   <see cref="ManaAbility"/>'s <c>Func&lt;ManaCost&gt;</c> overload
///   (CR 605.1). At activation time the closure walks the controller's
///   battlefield, counts every permanent with <see cref="CardSubtype.Elf"/>
///   (INCLUDING Archdruid itself — the oracle says "each Elf you control",
///   no "other" qualifier), and returns <c>{G} × N</c>. The mana-amount
///   computation mirrors <see cref="TronLandHelper.ComputeManaAddition"/>'s
///   pattern: closure reads live battlefield state, returns a freshly
///   parsed <see cref="ManaCost"/>. Activation taps Archdruid via
///   <see cref="Permanent.Tap"/> (default ManaAbility behaviour); the
///   <c>canActivateCheck</c> gates on <c>!IsTapped</c>.
///
/// Edge cases:
/// - <b>Zero Elves</b>: shouldn't happen at activation (Archdruid itself
///   is an Elf and is on the battlefield), but defensively, count = 0
///   yields <c>ManaCost.Zero</c> — the activation taps the source for no
///   mana, which is legal under CR 605.1.
/// - <b>Token Elves</b> created by Imperious Perfect (or any 1/1 Elf
///   Warrior token) count — the closure reads
///   <see cref="CardSubtype.Elf"/> off any permanent on the controller's
///   battlefield, token or otherwise.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister for the lord static</b>: the registered
///   <see cref="LordStaticEffect"/> stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Archdruid
///   isn't on the battlefield so the bonus lifts correctly, but a future
///   Prune pass could drop the entry. Same shape as Goblin Chieftain /
///   Imperious Perfect.
/// </summary>
[CardName("Elvish Archdruid")]
public static class ElvishArchdruidFactory
{
    public const string CardName = "Elvish Archdruid";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Elvish Archdruid with no live ContinuousEffectsService.
    /// Suitable for card-shape / dispatcher tests — the lord static effect
    /// is NOT registered (no layers service). The {T}: Add {G}×Elf-count
    /// mana ability is always wired and reads the live battlefield at
    /// activation time.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Elvish Archdruid with optional layers service. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+1 lord
    /// static for other Elves is registered.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Elf creatures you control get +1/+1" lord static against.
    /// May be null — no live bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 613.1g Layer 7c — "Other Elf creatures you control get +1/+1."
        // includeSelf: false; keyword grant list empty (P/T only). Same
        // shape as Goblin Chieftain minus the granted-Haste rider.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        // CR 605.1 — "{T}: Add {G} for each Elf you control." Mana ability
        // (no stack). The amount is dynamic so we use the Func<ManaCost>
        // overload; closure walks the controller's battlefield at
        // activation time and counts Elves (INCLUDING Archdruid itself —
        // no "other" qualifier on this rider).
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () => ComputeManaAddition(card.Controller ?? owner),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }

    /// <summary>
    /// Compute the mana amount the Archdruid's tap ability adds at the
    /// moment of activation: <c>{G} × (number of Elves the controller
    /// controls)</c>. Token Elves count. Archdruid itself counts. CR
    /// 605.1 — the amount is read from live game state at activation.
    ///
    /// Returns <see cref="ManaCost.Zero"/> when the count is zero (a
    /// defensive case that shouldn't trigger in practice — Archdruid is
    /// itself an Elf and must be on the battlefield to activate).
    /// </summary>
    public static ManaCost ComputeManaAddition(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var battlefield = controller.Zones.Battlefield.GetCards();
        var elfCount = 0;
        foreach (var c in battlefield)
        {
            if (c == null) continue;
            if (c.HasSubtype(CardSubtype.Elf)) elfCount++;
        }

        if (elfCount == 0) return ManaCost.Zero;

        // ManaCost.Parse handles repeated single-colour pips: "GG" parses
        // as two green. Build a string of N {G} pips.
        return ManaCost.Parse(new string('G', elfCount));
    }
}

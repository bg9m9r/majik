using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elvish Archdruid (Magic 2011, {1}{G}{G}).
///
/// Creature — Elf Druid 2/2. Oracle text:
///   "Other Elf creatures you control get +1/+1.
///    {T}: Add an amount of {G} equal to the number of Elves you control."
///
/// The marquee Elf lord — anthem on Elves + Cradle-shaped tribal mana
/// engine in one card. Centrepiece of mono-G Elf tribal in every format
/// it's legal in (Modern, Legacy, Commander).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Druid at printed cost {1}{G}{G}, owner/controller
///   wired. Elf + Druid subtypes so it counts itself toward its own mana
///   ability + tribal-lord scopes.
/// - <b>Lord static (CR 613.7c / 613.1g)</b>: "Other Elf creatures you
///   control get +1/+1." Wired via
///   <see cref="LordStaticEffect"/> with <c>matchingSubtype: Elf,
///   power: 1, toughness: 1, includeSelf: false, allPlayers: false</c>
///   (controller-scoped; opponents' Elves are unaffected). Identical
///   shape to <see cref="GoblinChieftainFactory"/> / Lord of Atlantis
///   without the Haste/Islandwalk rider. The Archdruid itself is also
///   an Elf — <c>includeSelf: false</c> honours the printed "Other".
/// - <b>Tribal mana ability (CR 605.1 / 107.1b)</b>: <c>{T}: Add an
///   amount of {G} equal to the number of Elves you control.</c> Wired
///   via the <see cref="ManaAbility"/> <c>Func&lt;ManaCost&gt;</c>
///   generator overload (Tron-land / Nykthos shape). The generator
///   counts Elves on the controller's battlefield (INCLUDES the
///   Archdruid itself — oracle reads "Elves you control" with no
///   "other" qualifier, contrast Goblin Piledriver's "other attacking
///   Goblins") at activation time and returns a <see cref="ManaCost"/>
///   of N green pips. With just Archdruid alone the ability produces
///   {G}; with Archdruid + two other Elves it produces {G}{G}{G};
///   the curve goes exponential alongside Elvish Mystic / Llanowar
///   Elves / Heritage Druid token swarms.
///
/// ## X-count semantics
/// - Counted at activation (CR 605.1 — mana abilities don't use the
///   stack; the generator runs atomically). Same snapshot semantics as
///   Krenko, Mob Boss's {T} ability — read once, freeze for the
///   activation.
/// - INCLUDES the Archdruid itself (Elves "you control" with no "other"
///   qualifier).
/// - Counts Elf permanents on controller's battlefield only (CR 109.5 —
///   "you control" = controller, not opponents).
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning sickness gate</b>: the {T} mana ability is gated by
///   <see cref="Majik.Core.Rules.ActionValidator"/>'s tap-cost check
///   against creatures with summoning sickness (CR 302.1). Enforcement
///   happens upstream at activation validation time — same posture as
///   Llanowar Elves / Birds of Paradise / Heritage Druid.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   the Archdruid isn't on the battlefield so the bonus lifts correctly
///   (same posture as Master of the Pearl Trident / Goblin Chieftain).
/// </summary>
[CardName("Elvish Archdruid")]
public static class ElvishArchdruidFactory
{
    public const string CardName = "Elvish Archdruid";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Elvish Archdruid without a live continuous-effects
    /// service. Suitable for shape / dispatcher tests — the lord static
    /// effect is not registered (other Elves you control don't yet
    /// receive +1/+1 because there's no layers service to register the
    /// effect against). The {T} mana ability is still wired and produces
    /// {G} × (Elf count on controller's battlefield) on activation.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Elvish Archdruid. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to other Elves the
    /// controller controls is registered against the layers service.
    /// Opponent's Elves are NOT affected (no allPlayers). The {T} mana
    /// ability is always wired regardless of whether a layer-system
    /// service is supplied.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 static effect against. May be null — no live anthem.</param>
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

        // ----------------------------------------------------------------
        // Lord static — CR 613.7c (P/T) + CR 613.1g (controller scope).
        //   "Other Elf creatures you control get +1/+1."
        // allPlayers: false → controller-scoped (opponents' Elves aren't
        // pumped). includeSelf: false honours "Other".
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // {T}: Add an amount of {G} equal to the number of Elves you
        // control (CR 605.1 — mana ability, no stack; CR 107.1b — X
        // resolves at the moment the effect determines it).
        //
        // X-count semantics:
        //   - Counted at activation (CR 605.1 — mana abilities resolve
        //     atomically; same snapshot posture as Krenko's {T}).
        //   - INCLUDES Archdruid itself — oracle reads "Elves you
        //     control" with no "other" qualifier; Archdruid is an Elf
        //     it controls.
        //   - Counts Elf permanents on controller's battlefield only
        //     (CR 109.5 — "you control" = controller, not opponents).
        //
        // Wired via the Func<ManaCost> generator overload (Tron-land /
        // Nykthos shape) so the count is read at each activation.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerator: () =>
            {
                var controller = card.Controller ?? owner;
                int elfCount = controller.Zones.Battlefield.GetCards()
                    .Count(c => c.HasSubtype(CardSubtype.Elf));

                if (elfCount <= 0) return ManaCost.Zero;

                // Build "{G}{G}...{G}" with elfCount green pips.
                return ManaCost.Parse(string.Concat(Enumerable.Repeat("{G}", elfCount)));
            },
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}

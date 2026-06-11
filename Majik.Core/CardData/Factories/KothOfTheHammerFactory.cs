using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Koth of the Hammer (Scars of Mirrodin, {2}{R}{R}).
///
/// Legendary Planeswalker — Koth. Starting loyalty 3.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "+1: Untap target Mountain. It becomes a 4/4 red Elemental creature until
///        end of turn. It's still a land.
///    −2: Add {R} for each Mountain you control.
///    −5: You get an emblem with 'Mountains you control have "{T}: This land
///        deals 1 damage to any target."'"
///
/// The base shape (name, Legendary Planeswalker — Koth, {2}{R}{R}, loyalty 3)
/// is materialised from the embedded JSON definition
/// (<c>koth-of-the-hammer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, land-animation, mana rituals, or emblems, so they live in
/// the factory (same posture as <see cref="ChandraTorchOfDefianceFactory"/> and
/// the manland animate ability in <see cref="LairOfTheHydraFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: Untap target Mountain. It becomes a 4/4 red Elemental creature
///   until end of turn. It's still a land (CR 606 + CR 701.20 untap +
///   CR 613.1c animate + CR 613.1e colour)</b>: the target Mountain is picked
///   from <paramref name="targetMountainResolver"/> (v1 has no agent target
///   prompt for loyalty abilities — same gap Chandra / Vivien share). It is
///   untapped (no-op if already untapped — CR 701.20a). When a
///   <see cref="ContinuousEffectsService"/> is wired, three end-of-turn-
///   expirable continuous effects are registered on it:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> + <see cref="CardSubtype.Elemental"/>.
///       The printed Land type stays ("It's still a land", CR 613.1c).
///     - Layer 5 (<see cref="SetColorsEffect"/>) — sets the body's colour to
///       red (CR 613.1e). Koth's red animate body unlike the manland cycle,
///       which still has no colour layer.
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base P/T
///       4/4 (CR 613.7b). Surfaces through
///       <see cref="ContinuousEffectsService.Compute(Permanent)"/>'s creature-
///       row upgrade driven by the Layer-4 Creature grant.
///   All flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so the
///   cleanup-step expiry (CR 514.2) lifts the animation. Without the service
///   wired the untap still happens; the animate effects no-op.
/// - <b>−2: Add {R} for each Mountain you control (CR 606 + CR 106.4)</b>:
///   counts the lands the controller controls that have the Mountain subtype
///   (CR 205.3 — basic land type) and adds that many red mana to the
///   controller's pool via <see cref="Player.AddManaToPool"/>. Counts zero ⇒
///   adds nothing (CR 106.4 — no mana produced).
///
/// ## Deferred (v1 gaps)
/// - <b>−5 emblem static grant</b>: "Mountains you control have '{T}: This land
///   deals 1 damage to any target.'" mints a <em>structural</em>
///   <see cref="Emblem"/> in the controller's command zone (CR 114). The
///   static ability-granting layer (a continuous effect granting an activated
///   ability to a dynamic set of permanents) has no primitive yet — same
///   deferred surface as the anthem/grant emblems of
///   <see cref="VivienReidFactory"/> / <see cref="LilianaTheLastHopeFactory"/>
///   / <see cref="KaitoBaneOfNightmaresFactory"/>. The intent is recorded in
///   the emblem's <see cref="Emblem.SourceName"/>.
/// - <b>Target prompt</b>: the +1 "target Mountain" is picked from the supplied
///   resolver rather than via an agent <see cref="Majik.Core.Targeting.TargetRequest"/>
///   — same gap every loyalty-ability factory shares today.
/// </summary>
[CardName("Koth of the Hammer")]
public static class KothOfTheHammerFactory
{
    public const string CardName = "Koth of the Hammer";
    public const string Slug = "koth-of-the-hammer";
    public const int StartingLoyalty = 3;
    public const int Plus1Loyalty = +1;
    public const int Minus2Loyalty = -2;
    public const int UltimateLoyaltyCost = -5;

    /// <summary>The 4/4 red Elemental body the +1 animates a Mountain into.</summary>
    public const int AnimatedPower = 4;
    public const int AnimatedToughness = 4;

    /// <summary>
    /// Construct Koth with no resolvers / continuous-effects service wired —
    /// the +1 no-ops its animate (and untaps nothing, since no target is
    /// supplied), the −2 still adds {R} per Mountain the controller controls,
    /// and −5 mints a structural-only emblem. Loyalty changes still apply.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetMountainResolver: null, continuousEffects: null);

    /// <summary>
    /// Construct Koth of the Hammer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetMountainResolver">Returns candidate Mountains for the
    /// +1's "untap target Mountain" clause. v1 picks the first on the
    /// battlefield. May be null — the +1 no-ops.</param>
    /// <param name="continuousEffects">Continuous-effects service for the +1's
    /// Layer 4 / Layer 5 / Layer 7b animate registration. May be null — the
    /// untap still happens but no animation is recorded.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Land>>? targetMountainResolver,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Koth, {2}{R}{R}, loyalty 3). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var koth = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Untap target Mountain. It becomes a 4/4 red Elemental creature
        //    until end of turn. It's still a land. ----------------------------
        // CR 606 (loyalty) + CR 701.20 (untap) + CR 613.1c (animate, "still a
        // land") + CR 613.1e (colour). The Mountain is a real target chosen by
        // the activating player's agent: a TargetRequest is declared so the
        // dispatch path prompts for it and the effect reads the chosen Mountain
        // off the ResolutionContext (slot 0) — falling back to the resolver on
        // the legacy direct-activation path.
        var mountainRequest = new TargetRequest(
            Description: "Untap target Mountain",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.None,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Land>()
                .Where(l => l.HasSubtype(CardSubtype.Mountain))
                .Cast<object>()
                .ToList());

        koth.AddAbility(new LoyaltyAbility(
            koth,
            Plus1Loyalty,
            new[]
            {
                Fx.Inline("Untap target Mountain; it becomes a 4/4 red Elemental creature until end of turn (it's still a land)", rc =>
                {
                    var mountain = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Land
                        : null)
                        ?? targetMountainResolver?.Invoke()?.FirstOrDefault();
                    if (mountain == null) return default;
                    if (mountain.Zone != Majik.Core.Zones.ZoneType.Battlefield) return default;

                    // Untap target Mountain (CR 701.20a — no-op if already untapped).
                    if (mountain.IsTapped) mountain.Untap();

                    // Animate to a 4/4 red Elemental until EOT (CR 613). Printed
                    // Land type stays ("It's still a land").
                    if (continuousEffects != null)
                    {
                        // Layer 4 — add Creature type + Elemental subtype.
                        continuousEffects.Register(new ManlandCycleAnimateEffect(
                            mountain,
                            keywords: Array.Empty<string>(),
                            subtypes: new[] { CardSubtype.Elemental },
                            extraTypes: null));

                        // Layer 5 — set colour to red (CR 613.1e).
                        continuousEffects.Register(new SetColorsEffect(
                            mountain,
                            scope: p => ReferenceEquals(p, mountain),
                            colors: new[] { ManaColor.Red }));

                        // Layer 7b — set base P/T to 4/4 (CR 613.7b).
                        continuousEffects.Register(new ManlandCycleBecomesPTEffect(
                            mountain, AnimatedPower, AnimatedToughness));
                    }

                    return default;
                }),
            },
            targetRequests: new[] { mountainRequest }));

        // -- −2: Add {R} for each Mountain you control. -------------------------
        // CR 606 (loyalty) + CR 106.4 (mana into the controller's pool) +
        // CR 205.3 (Mountain basic land type). Counts every land the
        // controller controls that has the Mountain subtype.
        koth.AddAbility(new LoyaltyAbility(koth, Minus2Loyalty, () =>
        {
            var controller = koth.Controller ?? owner;
            var mountains = controller.Zones.Battlefield.GetCards()
                .Count(c => c.HasType(CardType.Land) && c.HasSubtype(CardSubtype.Mountain));
            if (mountains <= 0) return; // no Mountains — no mana produced.

            // Build "{R}" × mountains and add it to the controller's pool.
            var red = ManaCost.Parse(string.Concat(Enumerable.Repeat("{R}", mountains)));
            controller.AddManaToPool(red);
        }));

        // -- −5: You get an emblem with "Mountains you control have '{T}: This
        //    land deals 1 damage to any target.'" -----------------------------
        // CR 606 (loyalty) + CR 114 (emblem). Structural emblem — the
        // ability-granting static layer is the deferred surface (see class
        // xmldoc; matches the Vivien / Liliana / Kaito emblem posture).
        koth.AddAbility(new LoyaltyAbility(koth, UltimateLoyaltyCost, () =>
        {
            var controller = koth.Controller ?? owner;
            var emblem = new Emblem(
                controller: controller,
                sourceName:
                    $"{CardName} — \"Mountains you control have '{{T}}: This land " +
                    "deals 1 damage to any target.'\" emblem",
                abilities: Array.Empty<IAbility>());
            controller.AddEmblem(emblem);
        }));

        return koth;
    }
}

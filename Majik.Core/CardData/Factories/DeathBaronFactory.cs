using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Death Baron (Magic 2010 / many reprints,
/// Creature — Zombie Wizard {1}{B}{B} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Skeletons you control and other Zombies you control get +1/+1 and
///    have deathtouch."
///
/// The marquee Zombie/Skeleton lord — anthem + evasive-removal rider in
/// one card. Models the same tribal-lord shape as
/// <see cref="LordOfAtlantisFactory"/> / Elvish Archdruid, but pumps TWO
/// creature types instead of one.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Zombie Wizard at printed cost {1}{B}{B},
///   owner/controller wired. Zombie + Wizard subtypes.
/// - <b>Two lord statics (CR 613.7c P/T, CR 613.1f granted keyword,
///   CR 613.1g controller scope)</b>, each wired via
///   <see cref="LordStaticEffect"/> with <c>power: 1, toughness: 1,
///   grantedKeywords: ["Deathtouch"], allPlayers: false</c>
///   (controller-scoped — "you control"; opponents' creatures are
///   unaffected per CR 109.5):
///   <list type="bullet">
///     <item><b>Skeleton clause</b>: <c>matchingSubtype: Skeleton,
///       includeSelf: false</c>. Death Baron is not a Skeleton, so the
///       <c>includeSelf</c> flag is moot — the per-creature subtype gate
///       in <see cref="LordStaticEffect.AppliesTo"/> already excludes the
///       Baron from this clause.</item>
///     <item><b>Zombie clause</b>: <c>matchingSubtype: Zombie,
///       includeSelf: false</c>. The printed "OTHER Zombies" qualifier is
///       honoured by <c>includeSelf: false</c> — the Baron is a Zombie but
///       does not pump itself.</item>
///   </list>
///   Net effect: Death Baron never self-buffs (it is excluded from the
///   Skeleton clause by type and from the Zombie clause by "other").
/// - <b>Deathtouch (CR 702.2)</b> is granted as the "Deathtouch" keyword
///   string on each affected creature's
///   <see cref="CreatureCharacteristics.Keywords"/> set. The damage
///   system reads it via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/>, which
///   consults the computed (layer-applied) keyword set — so the granted
///   Deathtouch is live in combat, not just a marker (CR 702.2b — lethal
///   damage from a source with deathtouch destroys the creature).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>s
///   stay on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Death Baron isn't on the battlefield so both anthems lift correctly
///   (same posture as Lord of Atlantis / Goblin Chieftain).
/// </summary>
[CardName("Death Baron")]
public static class DeathBaronFactory
{
    public const string CardName = "Death Baron";
    public const string PrintedManaCost = "{1}{B}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Death Baron without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effects are
    /// not registered (Skeletons / other Zombies you control don't yet
    /// receive +1/+1 + Deathtouch because there's no layers service to
    /// register the effects against).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Death Baron. When
    /// <paramref name="continuousEffects"/> is supplied, two
    /// <see cref="LordStaticEffect"/>s are registered against the layers
    /// service: one granting +1/+1 and Deathtouch to Skeletons the
    /// controller controls, and one granting +1/+1 and Deathtouch to
    /// OTHER Zombies the controller controls. Opponents' creatures are NOT
    /// affected (controller-scoped, CR 109.5).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// two +1/+1 + Deathtouch static effects against. May be null — no
    /// live anthem.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted Deathtouch) +
            // CR 613.1g (controller scope). Two clauses, two effects.

            // "Skeletons you control ... get +1/+1 and have deathtouch."
            // Death Baron isn't a Skeleton; the subtype gate excludes it.
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Skeleton,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Deathtouch" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));

            // "... and other Zombies you control get +1/+1 and have
            // deathtouch." includeSelf: false honours "other" — the Baron
            // is a Zombie but does not pump itself.
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Zombie,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Deathtouch" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}

using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Conscription (Rise of the Eldrazi, {8}).
///
/// Tribal Enchantment — Aura Eldrazi. Printed oracle text per Scryfall
/// (Rise of the Eldrazi, 2010-04-23, oracle id
/// <c>cfa1f7c4-b58c-4c01-a4d5-79a6e76b4d4c</c>):
///   "Enchant creature
///    Enchanted creature gets +10/+10 and has annihilator 2 and trample."
///
/// ## Implemented (v1)
///
/// - <b>Tribal Enchantment — Aura Eldrazi {8}</b>. Owner / controller wired.
///   The legacy <see cref="CardType.Tribal"/> type (CR 205.1c — removed
///   from Modern rules in 2025 but still printed) is stamped alongside
///   <see cref="CardType.Enchantment"/> via <see cref="Card.AddCardType"/>
///   so legacy Tribal-cares effects (Bloodscale-Prime, Adaptive Automaton's
///   "creatures of the chosen type") still observe the type bit.
/// - <b>"Enchant creature" target shape (CR 303.4 / 702.5b)</b>:
///   produced by <see cref="BuildSpellDefinition"/> via
///   <see cref="AuraSpellDefinitionBuilder.ForAura"/> with a "target
///   creature" predicate.
/// - <b>Static "+10/+10 and has annihilator 2 and trample" (CR 613 /
///   702.86 / 702.19)</b> — wired via a single
///   <see cref="AttachedBoostEffect"/> carrying the P/T modification
///   (Layer 7c) and the granted keyword strings (Layer 6 / 7c blend):
///   "Annihilator 2" + "Trample". The boost reads
///   <see cref="Permanent.AttachedTo"/> dynamically so re-attach
///   transfers the bonus cleanly (CR 613.1g — characteristic-defining
///   aura statics read the bearer at each layer pass).
/// - <b>Annihilator 2 trigger (CR 702.86 / 603.2)</b> — when a
///   <see cref="TriggerManager"/> is supplied, an
///   <see cref="AnnihilatorAuraTrigger"/> registers on the aura so any
///   <see cref="CreatureAttacksEvent"/> where the attacker is the aura's
///   currently-enchanted creature fires the "defending player sacrifices
///   2 permanents" effect. The trigger condition reads
///   <c>_source.AttachedTo</c> at event time (CR 506.2 — defender at
///   attack time is the canonical "defending player"), so the same aura
///   transferring to a new bearer (e.g. via Auratouched Mage / Sigil of
///   Sleep blink shenanigans) tracks the new attacker automatically.
///   Same sacrifice-pick agent surface as
///   <see cref="Majik.Core.Keywords.AnnihilatorFactory"/>: the
///   defender's agent is consulted via
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> when supplied,
///   with a deterministic first-permanent fallback for the
///   dispatcher / shape-test path.
/// - <b>Keyword markers (discoverability)</b>: the aura also stamps
///   <see cref="KeywordAbility"/>("Annihilator", arg:2) +
///   <see cref="KeywordAbility"/>("Trample") on itself so keyword-scan
///   surfaces (CombatAbilities, bot keyword inventory) see the
///   printed keywords. The bearer reads the granted keywords through
///   the layer system (<see cref="AttachedBoostEffect"/>'s Layer 6
///   contribution to <see cref="CreatureCharacteristics.Keywords"/>),
///   not these aura-side markers.
///
/// ## Overloads
///
/// - <see cref="Create(Player)"/> — shape only. No continuous effect
///   registered, no trigger registered. Suitable for dispatcher / shape
///   tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. The boost + Annihilator trigger register against the
///   supplied services.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Bearer-side "has annihilator" trigger</b>: the printed text grants
///   "annihilator 2" to the bearer, so any other effect that reads "this
///   creature has annihilator N" off the bearer (rare — Annihilator is
///   only consulted via the per-attacker trigger in CR 702.86a) only
///   sees the keyword string through the layer system. The actual
///   sacrifice trigger is the aura's, not the bearer's — functionally
///   identical because the trigger references the bearer at fire time
///   via <see cref="Permanent.AttachedTo"/>.
/// - <b>Planeswalker-defender Annihilator</b>: same posture as
///   <see cref="Majik.Core.Keywords.AnnihilatorFactory"/> — when the
///   attack's defender is a planeswalker, the planeswalker's controller
///   is treated as the defending player for sacrifice purposes.
/// - <b>Tribal legacy interactions</b>: the Tribal type is stamped but
///   no Modern-format card actually keys on the Eldrazi subtype on a
///   non-creature card. Discoverability only.
/// </summary>
[CardName("Eldrazi Conscription")]
public static class EldraziConscriptionFactory
{
    public const string CardName = "Eldrazi Conscription";
    public const string PrintedManaCost = "{8}";
    public const int PowerBoost = 10;
    public const int ToughnessBoost = 10;
    public const int AnnihilatorValue = 2;

    /// <summary>Granted keyword strings on the enchanted creature
    /// (Layer 6 via <see cref="AttachedBoostEffect"/>): "Annihilator 2",
    /// "Trample" (CR 702.86 / 702.19).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "Annihilator 2", "Trample" };

    public const string OracleText =
        "Enchant creature\n" +
        "Enchanted creature gets +10/+10 and has annihilator 2 and trample.";

    /// <summary>
    /// Constructs Eldrazi Conscription with card identity only — no
    /// continuous effect, no trigger. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null, triggers: null, agentSelector: null);

    /// <summary>
    /// Constructs Eldrazi Conscription. When
    /// <paramref name="continuousEffects"/> is supplied the +10/+10 +
    /// granted-keywords boost is registered; gated on the aura being on
    /// the battlefield AND attached (effect's <c>IsActive</c> check).
    /// When <paramref name="triggers"/> is supplied an
    /// <see cref="AnnihilatorAuraTrigger"/> registers so the bearer's
    /// attacks fire "defending player sacrifices 2 permanents". The
    /// <paramref name="agentSelector"/> drives the defender's sacrifice
    /// picks when supplied; null falls back to deterministic first-N
    /// permanents.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura, CardSubtype.Eldrazi });
        // CR 205.1c — printed "Tribal Enchantment" line. Stamp the
        // legacy Tribal type on top of the Enchantment base.
        card.AddCardType(CardType.Tribal);
        card.SetOwner(owner);
        card.SetController(owner);

        // Discoverability markers — the actual gameplay surfaces are the
        // boost (keyword grants on the bearer) + the trigger (Annihilator
        // sacrifice).
        card.AddAbility(new KeywordAbility("Annihilator", card, owner, arg: AnnihilatorValue));
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the
            // Layer 7c P/T bump and the Layer 6 keyword grants
            // (Annihilator 2 + Trample) on the enchanted creature.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        // CR 702.86a — Annihilator 2 triggered ability. The aura is
        // the source; the trigger condition matches attacks by
        // _source.AttachedTo (the current bearer), so the trigger
        // tracks re-attach naturally. AddAbility unconditionally so
        // keyword scans + shape tests see it; only the trigger-manager
        // registration is gated on the optional service supplied by
        // the caller.
        var annihilator = new AnnihilatorAuraTrigger(card, AnnihilatorValue, agentSelector);
        card.AddAbility(annihilator.Ability);
        triggers?.RegisterTriggeredAbility(annihilator.Ability);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Eldrazi
    /// Conscription. The printed clause is the bare "Enchant creature"
    /// (CR 702.5b) — any battlefield creature is a legal candidate.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Creature));
    }
}

/// <summary>
/// CR 702.86 — Annihilator N as an aura-attached trigger. The aura is the
/// source; the per-attacker trigger fires when the aura's
/// <see cref="Permanent.AttachedTo"/> creature attacks. Same sacrifice-pick
/// agent surface as <see cref="Majik.Core.Keywords.AnnihilatorFactory"/>
/// (the static <see cref="TriggeredAbility"/> builder there hard-codes a
/// fixed creature source, which doesn't fit the dynamic-bearer aura case).
/// </summary>
public sealed class AnnihilatorAuraTrigger
{
    public TriggeredAbility Ability { get; }
    private readonly Enchantment _source;
    private readonly int _n;
    private readonly Func<Player, IPlayerAgent?>? _agentSelector;
    private Player? _capturedDefender;

    public AnnihilatorAuraTrigger(
        Enchantment auraSource,
        int n,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        _source = auraSource ?? throw new ArgumentNullException(nameof(auraSource));
        _n = n;
        _agentSelector = agentSelector;
        if (_source.Controller == null)
            throw new InvalidOperationException("Eldrazi Conscription source must have a controller");

        var condition = new EventTriggerCondition<CreatureAttacksEvent>(
            (e, _) =>
            {
                if (_source.Zone != ZoneType.Battlefield) return false;
                var bearer = _source.AttachedTo;
                if (bearer == null) return false;
                if (!ReferenceEquals(e.Attacker, bearer)) return false;

                // CR 506.2 — defending player at attack time.
                _capturedDefender = e.DefendingPlayerOrPlaneswalker switch
                {
                    Player p => p,
                    Planeswalker pw => pw.Controller,
                    _ => null,
                };
                return _capturedDefender != null;
            });

        var effect = new Effect(
            $"Annihilator {_n}: defending player sacrifices {_n} permanent{(_n == 1 ? "" : "s")}",
            () =>
            {
                var victim = _capturedDefender;
                if (victim == null) return;
                if (_n <= 0) return;

                var sacrificed = 0;
                while (sacrificed < _n)
                {
                    // Re-read each iteration — prior sacrifice may have
                    // removed multiple permanents (LTB triggers, etc.).
                    var candidates = victim.Zones.Battlefield.GetCards().ToList();
                    if (candidates.Count == 0) break;

                    ICard? pick;
                    var agent = _agentSelector?.Invoke(victim);
                    if (agent != null)
                    {
                        pick = agent.ChooseFromBattlefieldAsync(
                                victim,
                                candidates,
                                Cards.BotIntent.Removal)
                            .GetAwaiter().GetResult();
                        if (pick == null
                            || pick.Zone != ZoneType.Battlefield
                            || !ReferenceEquals(pick.Controller, victim))
                        {
                            pick = candidates[0];
                        }
                    }
                    else
                    {
                        pick = candidates[0];
                    }

                    // CR 701.16 / 702.12b — sacrifice bypasses
                    // Indestructible + regeneration.
                    Fx.Sacrifice(pick);
                    sacrificed++;
                }
            });

        Ability = new TriggeredAbility(
            source: _source,
            controller: _source.Controller,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });
    }
}

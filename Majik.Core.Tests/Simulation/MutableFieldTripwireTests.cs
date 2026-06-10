using System.Reflection;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Simulation;

/// <summary>
/// Reflection-based tripwire: every instance field declared on Card,
/// Permanent, Creature, and Player must appear in the per-type allow-list
/// below. Adding a field to one of these types FAILS this test until the
/// field is classified here AND handled in GameStateCloner.
///
/// Allow-list entries use NORMALIZED names: backing fields of the form
/// &lt;PropName&gt;k__BackingField are mapped to "PropName" before comparison.
/// Plain backing fields (e.g. _zone) use their raw names.
///
/// Classification markers used in comments:
///   COPIED        – copied by the type's copy-ctor / CloneEmpty
///   SHARED-IMMUT  – definition data; shared by reference (never mutated after ctor)
///   SKIPPED-DEFER – reference-bearing / complex; consciously deferred (see note)
/// </summary>
public sealed class MutableFieldTripwireTests
{
    // -----------------------------------------------------------------------
    // Allow-lists
    // -----------------------------------------------------------------------

    private static readonly Dictionary<Type, HashSet<string>> Known = new()
    {
        [typeof(Card)] = new HashSet<string>
        {
            // ── raw backing fields ──────────────────────────────────────────
            "_abilities",                           // COPIED (AddRange — shared ability refs, definition)
            "_backFaceLoyaltyAbilities",            // SKIPPED-DEFER: derived from _mdfcState (itself SKIPPED-DEFER); back-face loyalty abilities are sub-refs of _abilities (AddRange-copied), re-synced on transform
            "_cardTypes",                           // COPIED (AddRange — definition)
            "_controller",                          // COPIED (set null; re-linked via RelinkReferences)
            "_mdfcState",                           // SKIPPED-DEFER: complex, has lambda callbacks — see Task 8 concern
            "_offBattlefieldTypes",                 // SHARED-IMMUT (off-battlefield CDA def — shared by ref in copy-ctor)
            "_offBattlefieldSubtypes",              // SHARED-IMMUT (off-battlefield CDA def — shared by ref in copy-ctor)
            "_restrictedCastZones",                 // COPIED (AddRange — definition)
            "_subtypes",                            // COPIED (AddRange — definition)
            "_supertypes",                          // COPIED (AddRange — definition)
            "_zone",                                // COPIED (_zone = src._zone)

            // ── auto-property backing fields (normalized to PropName) ────────
            "AdventureSpec",                        // COPIED (definition ref — AdventureSpec = src.AdventureSpec)
            "ColorIndicator",                       // COPIED (immutable list ref)
            "ExiledWith",                           // COPIED (ExiledWith = src.ExiledWith — imprint back-link Guid?)
            "HasGiftPromised",                      // COPIED (cast sentinel)
            "InstanceId",                           // COPIED (InstanceId = src.InstanceId — stable identity)
            "Intensity",                            // COPIED (Intensity = src.Intensity)
            "IsDevoid",                             // COPIED (IsDevoid = src.IsDevoid)
            "IsVanillaShell",                       // COPIED (IsVanillaShell = src.IsVanillaShell)
            "ManaCost",                             // SHARED-IMMUT (definition string)
            "ManaCostValue",                        // SHARED-IMMUT (immutable value object)
            "MayPlayFromGraveyard",                 // COPIED (MayPlayFromGraveyard = src.MayPlayFromGraveyard)
            "Name",                                 // SHARED-IMMUT (definition string)
            "OffBattlefieldPower",                  // COPIED (OffBattlefieldPower = src.OffBattlefieldPower — off-battlefield CDA def)
            "OffBattlefieldToughness",              // COPIED (OffBattlefieldToughness = src.OffBattlefieldToughness — off-battlefield CDA def)
            "Owner",                                // COPIED (null initially; re-linked via RelinkReferences)
            "PendingCastColorCounts",               // COPIED (immutable dict ref)
            "PendingCastColors",                    // COPIED (immutable list ref)
            "PendingCastTargets",                   // COPIED (PendingCastTargets = src.PendingCastTargets)
            "PendingCastX",                         // COPIED (PendingCastX = src.PendingCastX)
            "PendingDelveExiledCount",              // COPIED (PendingDelveExiledCount = src.PendingDelveExiledCount)
            "ReturnToHandOnResolution",             // COPIED (ReturnToHandOnResolution = src.ReturnToHandOnResolution)
            "RuntimeEscapeCost",                    // COPIED (immutable value-object ref)
            "RuntimeEscapeExileCount",              // COPIED (RuntimeEscapeExileCount = src.RuntimeEscapeExileCount)
            "RuntimeExileCastAllowedCaster",        // COPIED + relinked via RelinkReferences (GrantRuntimeExileCast → cloned player)
            "RuntimeExileCastCost",                 // COPIED (immutable value-object ref)
            "RuntimeExileCastSpendAsAnyColor",      // COPIED (scalar bool)
            "RuntimeFlashbackCost",                 // COPIED (immutable value-object ref)
            "RuntimeGraveyardCastCost",             // COPIED (immutable value-object ref)
            "RuntimeGraveyardNonOwnerCastAllowedCaster", // COPIED + relinked via RelinkReferences (GrantRuntimeGraveyardNonOwnerCast → cloned player)
            "RuntimeGraveyardNonOwnerCastAnyTypeMana",   // COPIED (scalar bool)
            "RuntimeGraveyardNonOwnerCastCost",          // COPIED (immutable value-object ref)
            "TimesKicked",                          // COPIED (TimesKicked = src.TimesKicked)
            "TokenColorsOverride",                  // COPIED (immutable list ref)
            "TotalManaSpentThisCast",               // COPIED (TotalManaSpentThisCast = src.TotalManaSpentThisCast)
            "WasBargained",                         // COPIED (cast sentinel)
            "WasCast",                              // COPIED (cast sentinel)
            "WasCastForEscape",                     // COPIED (cast sentinel)
            "WasCastForSurge",                      // COPIED (cast sentinel)
            "WasCastFromHand",                      // COPIED (cast sentinel)
            "WasCastFromLibrary",                   // COPIED (cast sentinel)
            "WasCastFromSuspend",                   // COPIED (cast sentinel)
            "WasKicked",                            // COPIED (cast sentinel)
            "WasOffspringPaid",                     // COPIED (cast sentinel)
            "WasPlacedFromLibrary",                 // COPIED (cast sentinel)
        },

        [typeof(Permanent)] = new HashSet<string>
        {
            // ── raw backing fields ──────────────────────────────────────────
            "_attachments",             // COPIED (rebuilt in RelinkReferences pass)
            "_enteredBattlefieldTimestamp", // COPIED (_enteredBattlefieldTimestamp = src._enteredBattlefieldTimestamp)
            "_faceDownIntrinsicAbilities",  // SKIPPED-DEFER: HashSet<IAbility> — ability refs; face-down ward rarely active; deferred
            "_hasSummoningSickness",    // COPIED (_hasSummoningSickness = src._hasSummoningSickness)
            "_imprintedCards",          // SKIPPED-DEFER: List<ICard> ref-bearing — Task 5/6 deferred
            "_isTapped",                // COPIED (_isTapped = src._isTapped)
            "_regenerationShields",     // COPIED (_regenerationShields = src._regenerationShields)
            "_transientLoyalty",        // COPIED (_transientLoyalty = src._transientLoyalty)

            // ── auto-property backing fields (normalized) ────────────────────
            "ActiveEffects",            // SKIPPED-DEFER: service ref — intentionally null in sim clone (documented in copy-ctor)
            "AdditionalLandPlaysGranted", // COPIED (AdditionalLandPlaysGranted = src.AdditionalLandPlaysGranted)
            "AttachedTo",               // COPIED (re-linked in RelinkReferences pass)
            "BattleState",              // SKIPPED-DEFER: complex attached tracker — Task 6 deferred (comment in copy-ctor)
            "ClassState",               // SKIPPED-DEFER: complex attached tracker — Task 6 deferred
            "Counters",                 // COPIED (deep-copied per-type via src.Counters.All loop)
            "IsFaceDown",               // COPIED (IsFaceDown = src.IsFaceDown)
            "IsToken",                  // COPIED (IsToken = src.IsToken)
            "LoyaltyAbilityActivatedThisTurn", // COPIED (LoyaltyAbilityActivatedThisTurn = src.LoyaltyAbilityActivatedThisTurn)
            "SagaState",                // SKIPPED-DEFER: complex attached tracker — Task 6 deferred
            "WasDealtDamageThisTurn",   // COPIED (WasDealtDamageThisTurn = src.WasDealtDamageThisTurn)
        },

        [typeof(Creature)] = new HashSet<string>
        {
            // ── raw backing fields ──────────────────────────────────────────
            "_basePower",               // COPIED (_basePower = src._basePower)
            "_baseToughness",           // COPIED (_baseToughness = src._baseToughness)
            "_damage",                  // COPIED (_damage = src._damage)

            // ── auto-property backing fields (normalized) ────────────────────
            "BlitzWasPaid",             // COPIED (BlitzWasPaid = src.BlitzWasPaid)
            "CanAttackAsThoughItDidntHaveDefenderThisTurn", // COPIED (= src.CanAttackAsThoughItDidntHaveDefenderThisTurn)
            "EvokeWasPaid",             // COPIED (EvokeWasPaid = src.EvokeWasPaid)
            "IsCommander",              // COPIED (IsCommander = src.IsCommander)
            "MarkedForDestructionByDeathtouch", // COPIED (MarkedForDestructionByDeathtouch = src.MarkedForDestructionByDeathtouch)
        },

        [typeof(Player)] = new HashSet<string>
        {
            // ── raw backing fields ──────────────────────────────────────────
            "_citysBlessingBus",        // SKIPPED-DEFER: IEventBus ref — not needed in sim clone (sim doesn't use event bus)
            "_emblems",                 // COPIED (_emblems.AddRange(src._emblems) — immutable emblem refs)
            "_hasCitysBlessing",        // COPIED (clone._hasCitysBlessing = _hasCitysBlessing)
            "_hasLost",                 // COPIED (clone._hasLost = _hasLost)
            "_lifeTotal",               // COPIED (via startingLife ctor arg → _lifeTotal)
            "_manaPool",                // COPIED (clone._manaPool = _manaPool)
            "_manaProvenance",          // COPIED (clone._manaProvenance.AddRange(_manaProvenance))
            "_otherCounters",           // COPIED (foreach loop copies all entries)

            // ── auto-property backing fields (normalized) ────────────────────
            "Commander",                // SKIPPED-DEFER: CommanderState ref — commander format deferred for sim
            "CompanionUsedThisGame",    // COPIED (clone.CompanionUsedThisGame = CompanionUsedThisGame)
            "EnergyCounters",           // COPIED (clone.EnergyCounters = EnergyCounters)
            "Id",                       // COPIED (clone.Id = Id — stable identity)
            "LifeLostThisTurn",         // COPIED (clone.LifeLostThisTurn = LifeLostThisTurn)
            "Name",                     // SHARED-IMMUT (passed as ctor arg)
            "PoisonCounters",           // COPIED (clone.PoisonCounters = PoisonCounters)
            "Replacements",             // SKIPPED-DEFER: ReplacementBus ref — not needed in sim (no live effects)
            "Ring",                     // SKIPPED-DEFER: RingState ref-bearing — deferred per CloneEmpty comment
            "TriedToDrawFromEmptyLibrary", // COPIED (clone.TriedToDrawFromEmptyLibrary = TriedToDrawFromEmptyLibrary)
            "WasDealtDamageThisTurn",   // COPIED (clone.WasDealtDamageThisTurn = WasDealtDamageThisTurn)
            "Zones",                    // COPIED (empty ZoneManager created by ctor; zone cards added in later passes)
        },
    };

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Normalize a field name: strip auto-property backing-field wrapper so
    /// &lt;PropName&gt;k__BackingField becomes "PropName". Plain fields (_foo,
    /// publicField) pass through unchanged.
    /// </summary>
    private static string Normalize(string rawFieldName)
    {
        // Compiler-generated backing fields look like: <PropertyName>k__BackingField
        if (rawFieldName.StartsWith('<') && rawFieldName.Contains(">k__BackingField"))
        {
            var end = rawFieldName.IndexOf('>');
            return rawFieldName.Substring(1, end - 1);
        }
        return rawFieldName;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> Types() =>
        new[] { typeof(Card), typeof(Permanent), typeof(Creature), typeof(Player) }
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Types))]
    public void NoUnacknowledgedInstanceFields(Type t)
    {
        // DeclaredOnly: only fields declared on THIS type, not inherited ones —
        // each type's allow-list covers its own fields, base-type fields are
        // covered by the base type's own entry.
        var actual = t
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(f => !f.IsLiteral)          // exclude const fields
            .Select(f => Normalize(f.Name))    // normalize backing-field names
            .ToHashSet();

        var unknown = actual.Except(Known[t]).ToList();
        unknown.Should().BeEmpty(
            $"new instance field(s) on {t.Name} must be classified in MutableFieldTripwireTests.Known " +
            $"AND handled in GameStateCloner. Unknown fields: {string.Join(", ", unknown)}");
    }
}

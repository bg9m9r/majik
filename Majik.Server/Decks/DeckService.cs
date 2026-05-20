using MongoDB.Driver;

namespace Majik.Server.Decks;

/// <summary>Orchestrator for deck CRUD. Enforces per-user cap +
/// per-user name uniqueness + comprehensive deck validation. Endpoints
/// are thin shells over this.</summary>
public sealed class DeckService
{
    private const int MaxDecksPerOwner = 25;
    private const int MaxNameLength = 60;

    private readonly DeckRepository _repo;
    private readonly DeckValidationService _validator;

    public DeckService(DeckRepository repo, DeckValidationService validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public async Task<IReadOnlyList<DeckDto>> ListAsync(string ownerSub, CancellationToken ct)
    {
        var rows = await _repo.ListByOwnerAsync(ownerSub, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DeckResult> GetAsync(string ownerSub, Guid id, CancellationToken ct)
    {
        var d = await _repo.GetByIdForOwnerAsync(id, ownerSub, ct);
        if (d == null) return DeckResult.Fail(new DeckError("deck-not-found"));
        return DeckResult.Ok(ToDto(d));
    }

    public async Task<DeckResult> CreateAsync(string ownerSub, CreateDeckRequest req, CancellationToken ct)
    {
        var nameErr = ValidateName(req.Name);
        if (nameErr != null) return DeckResult.Fail(new DeckError("invalid-deck", new[] { nameErr }));

        var count = await _repo.CountByOwnerAsync(ownerSub, ct);
        if (count >= MaxDecksPerOwner)
        {
            return DeckResult.Fail(new DeckError("deck-cap-reached", Detail: $"max {MaxDecksPerOwner}"));
        }

        if (await _repo.NameTakenForOwnerAsync(ownerSub, req.Name.Trim(), excludeId: null, ct))
        {
            return DeckResult.Fail(new DeckError("name-taken"));
        }

        var now = DateTime.UtcNow;
        var deck = new Deck
        {
            Id = Guid.NewGuid(),
            OwnerSub = ownerSub,
            Name = req.Name.Trim(),
            Mainboard = req.Mainboard.Select(e => new DeckCardEntry { Name = e.Name, Count = e.Count }).ToList(),
            Sideboard = req.Sideboard.Select(e => new DeckCardEntry { Name = e.Name, Count = e.Count }).ToList(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var validation = _validator.Validate(deck);
        if (!validation.IsValid)
        {
            return DeckResult.Fail(new DeckError("invalid-deck", validation.Errors));
        }

        try
        {
            await _repo.InsertAsync(deck, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return DeckResult.Fail(new DeckError("name-taken"));
        }

        return DeckResult.Ok(ToDto(deck));
    }

    public async Task<DeckResult> UpdateAsync(string ownerSub, Guid id, UpdateDeckRequest req, CancellationToken ct)
    {
        var nameErr = ValidateName(req.Name);
        if (nameErr != null) return DeckResult.Fail(new DeckError("invalid-deck", new[] { nameErr }));

        var existing = await _repo.GetByIdForOwnerAsync(id, ownerSub, ct);
        if (existing == null) return DeckResult.Fail(new DeckError("deck-not-found"));

        if (await _repo.NameTakenForOwnerAsync(ownerSub, req.Name.Trim(), excludeId: id, ct))
        {
            return DeckResult.Fail(new DeckError("name-taken"));
        }

        var now = DateTime.UtcNow;
        var trial = new Deck
        {
            Id = existing.Id,
            OwnerSub = existing.OwnerSub,
            Name = req.Name.Trim(),
            Mainboard = req.Mainboard.Select(e => new DeckCardEntry { Name = e.Name, Count = e.Count }).ToList(),
            Sideboard = req.Sideboard.Select(e => new DeckCardEntry { Name = e.Name, Count = e.Count }).ToList(),
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
        };

        var validation = _validator.Validate(trial);
        if (!validation.IsValid)
        {
            return DeckResult.Fail(new DeckError("invalid-deck", validation.Errors));
        }

        var moved = await _repo.UpdateForOwnerAsync(id, ownerSub, trial.Name, trial.Mainboard, trial.Sideboard, now, ct);
        if (!moved) return DeckResult.Fail(new DeckError("deck-not-found"));

        return DeckResult.Ok(ToDto(trial));
    }

    public async Task<DeckResult> DeleteAsync(string ownerSub, Guid id, CancellationToken ct)
    {
        var deleted = await _repo.DeleteForOwnerAsync(id, ownerSub, ct);
        if (deleted == 0) return DeckResult.Fail(new DeckError("deck-not-found"));
        return DeckResult.Ok(null);
    }

    private static string? ValidateName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "name: empty";
        if (trimmed.Length > MaxNameLength) return $"name: max {MaxNameLength} chars";
        return null;
    }

    private static DeckDto ToDto(Deck d) => new(
        Id: d.Id,
        OwnerSub: d.OwnerSub,
        Name: d.Name,
        Mainboard: d.Mainboard.Select(e => new DeckCardEntryDto(e.Name, e.Count)).ToList(),
        Sideboard: d.Sideboard.Select(e => new DeckCardEntryDto(e.Name, e.Count)).ToList(),
        CreatedAt: d.CreatedAt,
        UpdatedAt: d.UpdatedAt);
}

/// <summary>Result wrapper matching MatchService.Result&lt;T&gt; shape from
/// sub-project #5. <see cref="Value"/> is the DeckDto on success; null
/// for DeleteAsync.</summary>
public sealed record DeckResult(bool IsSuccess, DeckDto? Value, DeckError? Error)
{
    public static DeckResult Ok(DeckDto? value) => new(true, value, null);
    public static DeckResult Fail(DeckError err) => new(false, null, err);
}

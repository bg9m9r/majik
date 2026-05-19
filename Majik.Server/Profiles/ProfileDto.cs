namespace Majik.Server.Profiles;

/// <summary>Wire format for <c>/me</c>. Sends <c>HandleDisplay</c> as <c>handle</c>;
/// callers never see the lowercased index value.</summary>
public sealed record ProfileDto(
    string Sub,
    string Handle,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Standard error envelope for /me endpoints.</summary>
public sealed record ProfileError(string Error, string? Detail = null);

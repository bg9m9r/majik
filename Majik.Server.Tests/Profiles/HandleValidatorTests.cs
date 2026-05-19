using FluentAssertions;
using Majik.Server.Profiles;
using Xunit;

namespace Majik.Server.Tests.Profiles;

public class HandleValidatorTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("Alice")]
    [InlineData("a1b")]
    [InlineData("AAA")]
    [InlineData("a_b-c")]
    [InlineData("twentycharacter_nam")] // 20 chars
    public void Validate_AcceptsValid(string handle)
    {
        var result = HandleValidator.Validate(handle);
        result.Outcome.Should().Be(HandleValidationOutcome.Ok);
    }

    [Theory]
    [InlineData("ab")]                     // too short
    [InlineData("a_very_long_handle_indeed")] // 25 chars, too long
    [InlineData("hi!")]                    // illegal char
    [InlineData("with space")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsInvalidFormat(string handle)
    {
        var result = HandleValidator.Validate(handle);
        result.Outcome.Should().Be(HandleValidationOutcome.InvalidFormat);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Majik")]
    [InlineData("bot")]
    [InlineData("system")]
    public void Validate_RejectsReserved(string handle)
    {
        var result = HandleValidator.Validate(handle);
        result.Outcome.Should().Be(HandleValidationOutcome.Reserved);
    }

    [Fact]
    public void Validate_TrimsWhitespace()
    {
        var result = HandleValidator.Validate("  alice  ");
        result.Outcome.Should().Be(HandleValidationOutcome.Ok);
        result.Normalized.Should().Be("alice");
        result.Display.Should().Be("alice");
    }

    [Fact]
    public void Validate_NormalizedIsLowercase()
    {
        var result = HandleValidator.Validate("AliceB");
        result.Normalized.Should().Be("aliceb");
        result.Display.Should().Be("AliceB");
    }
}

using FluentAssertions;
using Majik.Server.Composition;
using Xunit;

namespace Majik.Server.Tests.Composition;

public class ReportingOptionsTests
{
    [Fact]
    public void IsTrusted_true_only_for_listed_sub_when_enabled()
    {
        var opts = new ReportingOptions { Enabled = true, TrustedTesterSubs = { "alice" } };
        opts.IsTrusted("alice").Should().BeTrue();
        opts.IsTrusted("bob").Should().BeFalse();
    }

    [Fact]
    public void IsTrusted_false_when_disabled()
    {
        var opts = new ReportingOptions { Enabled = false, TrustedTesterSubs = { "alice" } };
        opts.IsTrusted("alice").Should().BeFalse();
    }
}

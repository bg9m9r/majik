using FluentAssertions;
using Majik.Server.Matches;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class NotificationsPublisherTests
{
    [Fact]
    public async Task NotifyReportDelivered_sends_to_user_sub()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User("alice")).Returns(clientProxy.Object);
        var ctx = new Mock<IHubContext<NotificationsHub>>();
        ctx.SetupGet(c => c.Clients).Returns(clients.Object);

        var pub = new NotificationsPublisher(ctx.Object);
        await pub.NotifyReportDeliveredAsync("alice", 50, "wedge", default);

        // The SendAsync extension method funnels through IClientProxy.SendCoreAsync.
        clientProxy.Verify(p => p.SendCoreAsync("report-delivered",
            It.Is<object[]>(a => a.Length == 1), It.IsAny<CancellationToken>()), Times.Once);
    }
}

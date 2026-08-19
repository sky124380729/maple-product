using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Maple.InputBroker.Tests.Broker;

public sealed class BrokerPipeSecurityTests
{
    [Fact]
    public void Allows_only_the_current_windows_user()
    {
        if (!OperatingSystem.IsWindows()) return;

        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User!;
        PipeSecurity security = BrokerPipeSecurity.CreateForCurrentUser();
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            targetType: typeof(SecurityIdentifier));

        PipeAccessRule rule = Assert.Single(rules.Cast<PipeAccessRule>());
        Assert.Equal(currentUser, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(PipeAccessRights.FullControl, rule.PipeAccessRights);
        Assert.True(security.AreAccessRulesProtected);
    }
}

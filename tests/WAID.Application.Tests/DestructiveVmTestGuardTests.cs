using WAID.Application.Abstractions;
using WAID.Application.Services;
namespace WAID.Application.Tests;
public sealed class DestructiveVmTestGuardTests
{
    [Fact]public async Task Normal_mode_can_never_authorize_destructive_validation()=>await Assert.ThrowsAsync<InvalidOperationException>(()=>new DestructiveVmTestGuard(new Admin(true)).AuthorizeAsync(new(false,true,true,true,"RUN DESTRUCTIVE VM TEST"),CancellationToken.None));
    [Fact]public async Task Every_acknowledgement_and_admin_are_required()=>await Assert.ThrowsAsync<InvalidOperationException>(()=>new DestructiveVmTestGuard(new Admin(true)).AuthorizeAsync(new(true,true,false,true,"RUN DESTRUCTIVE VM TEST"),CancellationToken.None));
    [Fact]public async Task Exact_confirmation_authorizes_disposable_admin_vm()=>await new DestructiveVmTestGuard(new Admin(true)).AuthorizeAsync(new(true,true,true,true,"RUN DESTRUCTIVE VM TEST"),CancellationToken.None);
    private sealed class Admin(bool value):IAdministratorService{public bool IsAdministrator()=>value;}
}

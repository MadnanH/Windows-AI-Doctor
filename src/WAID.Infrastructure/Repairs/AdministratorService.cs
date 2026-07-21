using System.Security.Principal;
using WAID.Application.Abstractions;

namespace WAID.Infrastructure.Repairs;

public sealed class AdministratorService : IAdministratorService
{
    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

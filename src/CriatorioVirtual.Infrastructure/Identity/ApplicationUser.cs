using Microsoft.AspNetCore.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid? SelectedBreedingFarmId { get; set; }
}

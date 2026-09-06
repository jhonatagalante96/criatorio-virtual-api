using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class CreateBreedingFarmCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CreateBreedingFarmCommand, CreateBreedingFarmResult>
{
    public async Task<CreateBreedingFarmResult> Handle(
        CreateBreedingFarmCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return CreateBreedingFarmResult.AccountNotFound();
        }

        var officialRegistrationNumber = Normalize(command.OfficialRegistrationNumber);
        if (officialRegistrationNumber is not null &&
            await dbContext.BreedingFarms.AnyAsync(
                farm => farm.OfficialRegistrationNumber == officialRegistrationNumber,
                cancellationToken))
        {
            return CreateBreedingFarmResult.DuplicateOfficialRegistration();
        }

        var contactEmail = Normalize(command.ContactEmail) ?? user.Email.Trim();
        var responsibleName = Normalize(command.ResponsibleName) ?? contactEmail;
        var address = command.Address is null
            ? new BreedingFarmAddress(null, null, null, null, null, null, null)
            : new BreedingFarmAddress(
                command.Address.Street,
                command.Address.Number,
                command.Address.Complement,
                command.Address.Neighborhood,
                command.Address.City,
                command.Address.State,
                command.Address.PostalCode);
        var now = DateTimeOffset.UtcNow;
        var farm = new BreedingFarm(
            Guid.NewGuid(),
            now,
            command.Name!,
            responsibleName,
            contactEmail,
            command.ContactPhone,
            officialRegistrationNumber,
            address);
        var owner = new BreedingFarmUser(farm.Id, command.UserId, BreedingFarmRole.Owner, now);

        dbContext.BreedingFarms.Add(farm);
        dbContext.BreedingFarmUsers.Add(owner);

        return CreateBreedingFarmResult.Created(farm.Id, command.UserId);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

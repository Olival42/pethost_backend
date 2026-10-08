using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Seed;

/// <summary>
/// Cria a conta de administrador se ela ainda não existir.
/// </summary>
/// <remarks>
/// Idempotente: rodar em toda subida é seguro, porque o par (e-mail, admin) é
/// verificado antes. Nunca atualiza a senha de um admin que já existe — trocar
/// senha por variável de ambiente seria um jeito silencioso de sequestrar a conta.
/// </remarks>
internal sealed partial class AuthDbSeeder(
    AuthDbContext dbContext,
    IPasswordHasher passwordHasher,
    IOptions<AdminSeedOptions> options,
    TimeProvider timeProvider,
    ILogger<AuthDbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            LogSeedDisabled(logger);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
        {
            LogSeedIncomplete(logger);
            return;
        }

        var email = Email.Create(settings.Email);
        if (email.IsFailure)
        {
            LogSeedInvalidEmail(logger);
            return;
        }

        var maskedEmail = Mask(email.Value!.Value);

        var alreadyExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                u => u.Email == email.Value && u.Role == UserRole.Admin,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            LogAdminAlreadyExists(logger, maskedEmail);
            return;
        }

        var passwordHash = PasswordHash.FromHash(passwordHasher.Hash(settings.Password));
        if (passwordHash.IsFailure)
        {
            LogSeedHashFailed(logger);
            return;
        }

        // Sem regra de senha forte aqui: a senha do admin vem do ambiente, não do usuário.
        var fullName = FullName.Create(settings.FullName);
        if (fullName.IsFailure)
        {
            LogSeedRejected(logger, fullName.FirstError?.Code ?? "unknown");
            return;
        }

        var admin = User.CreateAdmin(
            fullName.Value!,
            email.Value!,
            passwordHash.Value!,
            timeProvider.GetUtcNow());

        if (admin.IsFailure)
        {
            LogSeedRejected(logger, admin.FirstError?.Code ?? "unknown");
            return;
        }

        dbContext.Users.Add(admin.Value!);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogAdminCreated(logger, maskedEmail);
    }

    /// <summary>E-mail em log vai mascarado (§15): <c>a***@exemplo.com</c>.</summary>
    private static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at <= 0
            ? "***"
            : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Admin seed is disabled. Skipping.")]
    private static partial void LogSeedDisabled(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Admin seed is enabled but email or password is missing. Skipping.")]
    private static partial void LogSeedIncomplete(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Admin seed email is not a valid address. Skipping.")]
    private static partial void LogSeedInvalidEmail(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Admin account {Email} already exists. Nothing to seed.")]
    private static partial void LogAdminAlreadyExists(ILogger logger, string email);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error,
        Message = "Could not build a valid password hash for the admin seed.")]
    private static partial void LogSeedHashFailed(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error,
        Message = "Admin seed rejected by the domain with code {ErrorCode}.")]
    private static partial void LogSeedRejected(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Admin account {Email} created by the seeder.")]
    private static partial void LogAdminCreated(ILogger logger, string email);
}

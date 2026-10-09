using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.GetLinkedAccounts;

public sealed class GetLinkedAccountsQueryHandler(IUserRepository userRepository)
    : IQueryHandler<GetLinkedAccountsQuery, IReadOnlyList<LinkedAccountResponse>>
{
    public async Task<Result<IReadOnlyList<LinkedAccountResponse>>> HandleAsync(
        GetLinkedAccountsQuery query,
        CancellationToken cancellationToken)
    {
        var id = new UserId(query.UserId);
        var current = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is null)
            return Result<IReadOnlyList<LinkedAccountResponse>>.Failure(AuthErrors.UserNotFound(id));

        var accounts = await userRepository
            .ListByEmailAsync(current.Email, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LinkedAccountResponse> response =
        [
            .. accounts
                .Where(u => u.Role is UserRole.Owner or UserRole.Host)
                .OrderBy(u => u.Role)
                .Select(u => new LinkedAccountResponse(UserRoleValues.ToWire(u.Role), u.IsActive, u.Id == current.Id)),
        ];

        return Result<IReadOnlyList<LinkedAccountResponse>>.Success(response);
    }
}

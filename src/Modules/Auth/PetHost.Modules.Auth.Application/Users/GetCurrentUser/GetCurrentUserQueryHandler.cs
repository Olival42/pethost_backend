using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(IUserRepository userRepository)
    : IQueryHandler<GetCurrentUserQuery, AuthenticatedUserResponse>
{
    public async Task<Result<AuthenticatedUserResponse>> HandleAsync(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        var id = new UserId(query.UserId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return user is null
            ? Result<AuthenticatedUserResponse>.Failure(AuthErrors.UserNotFound(id))
            : Result<AuthenticatedUserResponse>.Success(SessionResponseFactory.ToUserResponse(user));
    }
}

using FluentValidation;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;

namespace PetHost.Modules.Bookings.Application.Bookings.CreateBooking;

/// <summary>
/// Formato do pedido: cantinho, pets (1 a 10, sem repetição) e o período, conferido pela regra do
/// próprio domínio (<see cref="StayPeriod"/>). Todos os erros voltam juntos num só 400.
/// </summary>
public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage(BookingsErrors.ListingIdRequired.Message);

        RuleFor(x => x.PetIds)
            .Must(ids => ids is { Count: > 0 }).WithMessage(BookingsErrors.PetIdsRequired.Message)
            .DependentRules(() =>
            {
                RuleFor(x => x.PetIds)
                    .Cascade(CascadeMode.Stop)
                    .Must(ids => ids!.Count <= Booking.MaxPets).WithMessage(BookingsErrors.PetIdsTooMany.Message)
                    .Must(ids => !ids!.Contains(Guid.Empty) && ids!.Distinct().Count() == ids!.Count).WithMessage(BookingsErrors.PetIdsInvalid.Message);
            });

        RuleFor(x => x.CheckIn)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(BookingsErrors.CheckInRequired.Message)
            .Must(text => StayPeriod.TryParseDate(text, out _)).WithMessage(BookingsErrors.CheckInInvalid.Message);

        RuleFor(x => x.CheckOut)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(BookingsErrors.CheckOutRequired.Message)
            .Must(text => StayPeriod.TryParseDate(text, out _)).WithMessage(BookingsErrors.CheckOutInvalid.Message);

        RuleFor(x => x).Custom((command, context) =>
        {
            if (!StayPeriod.TryParseDate(command.CheckIn, out var checkIn))
                return;

            // Saída ilegível: a chegada ainda é conferida sozinha (contra uma saída de uma noite),
            // para os erros dos dois campos voltarem juntos.
            var hasCheckOut = StayPeriod.TryParseDate(command.CheckOut, out var checkOut);

            var period = StayPeriod.Create(checkIn, hasCheckOut ? checkOut : checkIn.AddDays(1), BookingClock.Today(timeProvider.GetUtcNow()));
            if (period.IsSuccess)
                return;

            foreach (var error in period.Errors!.Where(error => hasCheckOut || error.Field == BookingsErrors.CheckInInThePast.Field))
                context.AddFailure(error.Field ?? string.Empty, error.Message);
        });
    }
}

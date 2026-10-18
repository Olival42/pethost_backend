using FluentValidation;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;

namespace PetHost.Modules.Bookings.Application.Bookings.CreateBooking;

/// <summary>
/// Formato do pedido: cantinho, pets (1 a 10, sem repetição), o período, conferido pela regra do
/// próprio domínio (<see cref="StayPeriod"/>), e a confirmação de ração e remédios (escopo, regra 8).
/// Todos os erros voltam juntos num só 400.
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

        RuleFor(x => x.CareTermsAccepted)
            .Equal(true).WithMessage(BookingsErrors.CareTermsNotAccepted.Message);

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

            var now = timeProvider.GetUtcNow();
            var period = StayPeriod.Create(checkIn, hasCheckOut ? checkOut : checkIn.AddDays(1), BookingClock.Today(now));
            var errors = period.IsSuccess
                ? []
                : period.Errors!.Where(error => hasCheckOut || error.Field == BookingsErrors.CheckInInThePast.Field).ToList();

            // Chegada válida mas em cima da hora: o anfitrião não teria como aceitar a tempo.
            if (errors.TrueForAll(error => error.Field != BookingsErrors.CheckInTooSoon.Field) && Booking.IsTooSoonToRequest(checkIn, now))
                errors.Add(BookingsErrors.CheckInTooSoon);

            foreach (var error in errors)
                context.AddFailure(error.Field ?? string.Empty, error.Message);
        });
    }
}

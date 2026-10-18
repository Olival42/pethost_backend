using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>Lembretes de prazo: pedido sem resposta (anfitrião) e 1ª parcela sem pagamento (tutor).</summary>
public sealed class DeadlineReminderTests
{
    private static readonly DateTimeOffset Now = BookingBuilder.DefaultNow;

    // ----- pedido sem resposta -----

    [Fact]
    public void TakeResponseReminder_Should_RemindOnce_When_TheDeadlineIsAFewHoursAway()
    {
        // Arrange: pedido feito agora vence em 24h; 3h antes do fim entra na janela de 4h.
        var booking = new BookingBuilder().Build();
        var at = booking.ResponseDeadline.AddHours(-3);

        // Act
        var first = booking.TakeResponseReminder(at);
        var again = booking.TakeResponseReminder(at.AddMinutes(30));

        // Assert
        first.Should().BeTrue();
        again.Should().BeFalse("o lembrete vai uma vez só");
        booking.ResponseRemindedAt.Should().Be(at);
    }

    [Fact]
    public void TakeResponseReminder_Should_NotRemind_When_ItIsTooEarlyOrTheDeadlinePassed()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var early = booking.TakeResponseReminder(booking.ResponseDeadline - Booking.ResponseReminderLead - TimeSpan.FromMinutes(1));
        var late = booking.TakeResponseReminder(booking.ResponseDeadline.AddMinutes(1));

        // Assert
        early.Should().BeFalse();
        late.Should().BeFalse("prazo vencido: a tarefa de expiração fecha o pedido");
        booking.ResponseRemindedAt.Should().BeNull();
    }

    [Fact]
    public void TakeResponseReminder_Should_NotRemind_When_TheHostAlreadyAnswered()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var reminded = booking.TakeResponseReminder(Now.AddHours(22));

        // Assert
        reminded.Should().BeFalse();
    }

    // ----- 1ª parcela sem pagamento -----

    [Fact]
    public void TakePaymentReminder_Should_RemindOnce_When_ThePaymentDeadlineIsAFewHoursAway()
    {
        // Arrange: aceita 1h depois do pedido; o prazo de pagamento vence 24h depois do aceite.
        var booking = new BookingBuilder().BuildAccepted();
        var at = booking.PaymentDeadline!.Value.AddHours(-5);

        // Act
        var first = booking.TakePaymentReminder(at);
        var again = booking.TakePaymentReminder(at.AddHours(1));

        // Assert
        first.Should().BeTrue();
        again.Should().BeFalse("o lembrete vai uma vez só");
        booking.PaymentRemindedAt.Should().Be(at);
    }

    [Fact]
    public void TakePaymentReminder_Should_NotRemind_When_ItIsTooEarlyOrTheDeadlinePassed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        var deadline = booking.PaymentDeadline!.Value;

        // Act
        var early = booking.TakePaymentReminder(deadline - Booking.PaymentReminderLead - TimeSpan.FromMinutes(1));
        var late = booking.TakePaymentReminder(deadline.AddMinutes(1));

        // Assert
        early.Should().BeFalse();
        late.Should().BeFalse("prazo vencido: a reserva expira");
        booking.PaymentRemindedAt.Should().BeNull();
    }

    [Fact]
    public void TakePaymentReminder_Should_NotRemind_When_TheFirstInstallmentIsPaidOrTheRequestIsNotAccepted()
    {
        // Arrange
        var confirmed = new BookingBuilder().BuildConfirmed();
        var requested = new BookingBuilder().Build();

        // Act
        var paid = confirmed.TakePaymentReminder(Now.AddHours(20));
        var notAccepted = requested.TakePaymentReminder(Now.AddHours(20));

        // Assert
        paid.Should().BeFalse();
        notAccepted.Should().BeFalse();
    }

    // ----- confirmação de ração e remédios -----

    [Fact]
    public void Request_Should_RecordTheCareTermsConfirmation_When_TheRequestIsCreated()
    {
        // Act
        var booking = new BookingBuilder().Build();

        // Assert
        booking.CareTermsAcceptedAt.Should().Be(Now);
    }
}

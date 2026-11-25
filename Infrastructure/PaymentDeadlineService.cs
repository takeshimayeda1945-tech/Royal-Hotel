using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>
/// An online booking keeps its room only while the guest chooses how to pay: when no payment method
/// was chosen within <see cref="BookingOptions.PaymentChoiceMinutes"/> minutes of booking, the booking
/// is cancelled (reason <see cref="CancelReasons.PaymentTimeout"/>) and the room can be booked again.
/// Runs when the site starts and then once a minute. Pages that show such a booking (payment page,
/// my bookings, front desk) run the same check first, so they never show an overdue booking as valid.
/// Bookings with a chosen method are never touched, also "cash at the hotel" ones.
/// </summary>
public sealed class PaymentDeadlineService : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PaymentDeadlineService> _logger;

    public PaymentDeadlineService(IServiceScopeFactory scopes, ILogger<PaymentDeadlineService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Cancels every overdue booking; returns how many were cancelled.</summary>
    public static async Task<int> CancelOverdueAsync(ApplicationDbContext db, DateTime now, CancellationToken cancellationToken = default)
    {
        // Usually there is nothing to do: a plain read, no locks.
        var overdueIds = await db.HotelBookings.PaymentChoiceOverdue(now).Select(b => b.Id).ToListAsync(cancellationToken);
        if (overdueIds.Count == 0)
        {
            return 0;
        }

        // One booking at a time (there are only a few): the UPDATE checks the rule again, so a booking
        // whose guest chose a payment method a moment ago is left alone (the payment page locks the row
        // while it saves the choice). The audit line is written in the same transaction, and only by
        // the run that really cancelled the booking (a page and the timer may run at the same moment).
        var cancelled = 0;
        foreach (var id in overdueIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var changed = await db.HotelBookings
                .Where(b => b.Id == id)
                .PaymentChoiceOverdue(now)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.CancelReason, CancelReasons.PaymentTimeout), cancellationToken);
            if (changed == 0)
            {
                continue; // the transaction is rolled back when it is disposed
            }

            var booking = await db.HotelBookings.AsNoTracking().FirstAsync(b => b.Id == id, cancellationToken);
            db.AuditLogs.Add(AuditLog.BySystem(AuditActions.AutoCancel, AuditEntities.Booking, id,
                $"{AuditText.Booking(booking)} · ไม่ได้เลือกวิธีชำระเงินภายใน {BookingOptions.PaymentChoiceMinutes} นาทีหลังจอง " +
                $"(จองเมื่อ {DisplayText.Timestamp(booking.CreatedAt)})"));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            cancelled++;
        }

        return cancelled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the site finish starting first

        using var timer = new PeriodicTimer(CheckEvery);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The site is shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cancelled = await CancelOverdueAsync(db, DateTime.Now, stoppingToken);
            if (cancelled > 0)
            {
                _logger.LogInformation("Cancelled {Count} booking(s) without a payment method {Minutes} minutes after booking.",
                    cancelled, BookingOptions.PaymentChoiceMinutes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // E.g. the database is briefly unreachable: try again on the next tick.
            _logger.LogWarning(ex, "Could not cancel overdue unpaid bookings; trying again in {Interval}.", CheckEvery);
        }
    }
}

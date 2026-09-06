using EventScheduling.Frontend.Models;
using Microsoft.JSInterop;

namespace EventScheduling.Frontend.Services;

public enum BookingActionKind
{
    Return,
    End,
    Cancel
}

public sealed record BookingCardAction(BookingActionKind Kind, string Label, string CssClass);

public static class BookingActions
{
    public static BookingCardAction? For(BookingResponse booking)
    {
        if (booking.ResourceType == ResourceType.Equipment)
        {
            if (booking.Status is BookingStatus.Pending
                || (booking.Status is BookingStatus.Confirmed && DateTime.Now < booking.StartUtc))
            {
                return new BookingCardAction(BookingActionKind.Cancel, "Cancel hire", "button-danger");
            }

            if (booking.EquipmentId is not null && booking.Status is BookingStatus.Confirmed or BookingStatus.Overdue)
            {
                return new BookingCardAction(BookingActionKind.Return, "Return", "button-primary");
            }

            return null;
        }

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return null;
        }

        var now = DateTime.Now;
        if (now >= booking.StartUtc && now < booking.EndUtc)
        {
            return new BookingCardAction(BookingActionKind.End, "End", "button-primary");
        }

        if (now < booking.StartUtc)
        {
            return new BookingCardAction(BookingActionKind.Cancel, "Cancel", "button-danger");
        }

        return null;
    }

    public static Task<BookingResult> RunAsync(
        EventSchedulingApiClient api, IJSRuntime js, BookingResponse booking, BookingCardAction action) =>
        action.Kind switch
        {
            BookingActionKind.Return => ReturnWithNoteAsync(api, js, booking),
            BookingActionKind.End => api.EndBookingAsync(booking.Id),
            _ => api.CancelBookingAsync(booking)
        };

    private static async Task<BookingResult> ReturnWithNoteAsync(
        EventSchedulingApiClient api, IJSRuntime js, BookingResponse booking)
    {
        string? note = null;
        try
        {
            note = await js.InvokeAsync<string?>("prompt", "Note on the equipment's condition (optional):");
        }
        catch (JSException)
        {
        }

        return await api.ReturnBookingAsync(booking.Id, note);
    }
}

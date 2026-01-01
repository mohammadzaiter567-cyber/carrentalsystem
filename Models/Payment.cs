using System.ComponentModel.DataAnnotations;

namespace projectadvanced.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking Booking { get; set; }

        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Required]
        public string PaymentMethod { get; set; } = "Card";

        public string Status { get; set; } = "Paid";
        public string? StripeSessionId { get; set; }

        public string? CardLast4 { get; set; }
        public string? TransactionId { get; set; }
    }
}

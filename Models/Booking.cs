using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace projectadvanced.Models
{
    public class Booking
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }
        public IdentityUser User { get; set; }

        [Required]
        public int CarId { get; set; }
        public Car Car { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        public decimal TotalPrice { get; set; }

        public string Status { get; set; } = "Pending";
    }
}

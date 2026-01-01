using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace projectadvanced.Models
{
    public class CustomerProfile
    {
        public int Id { get; set; }

        // UserId is set server-side from authenticated user, no need for [Required] validation
        public string UserId { get; set; }
        public IdentityUser User { get; set; }

        [Required]
        [StringLength(100)]
        public string FullName { get; set; }

        [Required]
        [Phone]
        public string Phone { get; set; }

        public string Address { get; set; }

        [StringLength(20)]
        public string LicenseNumber { get; set; }
    }
}

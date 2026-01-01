using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc;

namespace projectadvanced.Models
{
    public class Car
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Brand { get; set; }

        [Required]
        [StringLength(50)]
        public string Model { get; set; }

        [Range(1950, 2100)]
        public int Year { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Plate Number")]
        [Remote(action: "VerifyPlateNumber", controller: "Car", AdditionalFields = "Id", ErrorMessage = "This plate number already exists.")]
        public string PlateNumber { get; set; }

        [Range(0, 9999)]
        public decimal DailyPrice { get; set; }

        public bool IsAvailable { get; set; } = true;

        public string? ImagePath { get; set; }

        [NotMapped]
        public IFormFile? ImageFile { get; set; }

        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
    }
}

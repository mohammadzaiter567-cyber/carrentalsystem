using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using projectadvanced.Models;

namespace projectadvanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CarController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CarController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index()
        {
            var cars = await _context.Cars
                .Include(c => c.Category)
                .ToListAsync();

            return View(cars);
        }

        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
       
        public async Task<IActionResult> Create(Car car)
        {
            // Check if plate already exists
            bool plateExists = await _context.Cars
                .AnyAsync(c => c.PlateNumber == car.PlateNumber);

            if (plateExists)
            {
                ModelState.AddModelError("PlateNumber", "This plate number already exists.");
                ViewBag.Categories = await _context.Categories.ToListAsync();
                return View(car);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.Categories.ToListAsync();
                return View(car);
            }

            if (car.ImageFile != null)
            {
                car.ImagePath = await SaveImage(car.ImageFile);
            }

            _context.Add(car);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }


        public async Task<IActionResult> Edit(int id)
        {
            var car = await _context.Cars.FindAsync(id);
            if (car == null)
                return NotFound();

            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(car);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Car car)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.Categories.ToListAsync();
                return View(car);
            }

            // UNIQUE VALIDATION WHEN EDITING (exclude same ID)
            bool plateExists = await _context.Cars
                .AnyAsync(c => c.PlateNumber == car.PlateNumber && c.Id != car.Id);

            if (plateExists)
            {
                ModelState.AddModelError("PlateNumber", "Plate number already exists.");
                ViewBag.Categories = await _context.Categories.ToListAsync();
                return View(car);
            }

            var existingCar = await _context.Cars.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == car.Id);

            if (car.ImageFile != null)
            {
                if (!string.IsNullOrEmpty(existingCar?.ImagePath))
                {
                    DeleteImage(existingCar.ImagePath);
                }
                car.ImagePath = await SaveImage(car.ImageFile);
            }
            else
            {
                car.ImagePath = existingCar?.ImagePath;
            }

            _context.Update(car);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int id)
        {
            var car = await _context.Cars.FindAsync(id);
            if (car == null)
                return NotFound();

            if (!string.IsNullOrEmpty(car.ImagePath))
            {
                DeleteImage(car.ImagePath);
            }

            _context.Cars.Remove(car);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [AcceptVerbs("Get", "Post")]
        public async Task<IActionResult> VerifyPlateNumber(string plateNumber, int id = 0)
        {
            bool plateExists = await _context.Cars
                .AnyAsync(c => c.PlateNumber == plateNumber && c.Id != id);

            if (plateExists)
            {
                return Json($"This plate number already exists.");
            }

            return Json(true);
        }

        private async Task<string> SaveImage(IFormFile imageFile)
        {
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "cars");

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + imageFile.FileName;
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(fileStream);
            }

            return "/images/cars/" + uniqueFileName;
        }

        private void DeleteImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return;

            string fullPath = Path.Combine(_webHostEnvironment.WebRootPath, imagePath.TrimStart('/'));

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
    }
}

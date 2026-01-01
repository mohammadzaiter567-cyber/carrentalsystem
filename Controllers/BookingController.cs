using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using projectadvanced.Models;

namespace projectadvanced.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public BookingController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // USER: View their own bookings
        public async Task<IActionResult> MyBookings()
        {
            var userId = _userManager.GetUserId(User);

            var bookings = await _context.Bookings
                .Include(b => b.Car)
                .Where(b => b.UserId == userId)
                .ToListAsync();

            return View(bookings);
        }

        // USER: Browse available cars for rent
        [AllowAnonymous]
        public async Task<IActionResult> RentCars()
        {
            // Get all cars
            var cars = await _context.Cars
                .Include(c => c.Category)
                .Where(c => c.IsAvailable)
                .ToListAsync();

            // Get active bookings to show availability info
            var activeBookings = await _context.Bookings
                .Where(b => b.Status == "Confirmed" && b.EndDate >= DateTime.Now.Date)
                .ToListAsync();

            ViewBag.ActiveBookings = activeBookings;

            return View(cars);
        }

        // Check if car is available for given dates
        private async Task<bool> IsCarAvailableForDates(int carId, DateTime startDate, DateTime endDate)
        {
            var hasOverlappingBooking = await _context.Bookings
                .AnyAsync(b => b.CarId == carId 
                    && b.Status == "Confirmed"
                    && startDate < b.EndDate 
                    && endDate > b.StartDate);

            return !hasOverlappingBooking;
        }

        // USER: Create booking page
        public async Task<IActionResult> Create(int carId)
        {
            var car = await _context.Cars.Include(c => c.Category).FirstOrDefaultAsync(c => c.Id == carId);
            if (car == null)
                return NotFound();

            if (!car.IsAvailable)
            {
                TempData["Error"] = "This car is not available for rent.";
                return RedirectToAction(nameof(RentCars));
            }

            // Get the latest booking end date for this car
            var latestBooking = await _context.Bookings
                .Where(b => b.CarId == carId && b.Status == "Confirmed" && b.EndDate >= DateTime.Now.Date)
                .OrderByDescending(b => b.EndDate)
                .FirstOrDefaultAsync();

            ViewBag.Car = car;
            ViewBag.MinStartDate = latestBooking != null 
                ? latestBooking.EndDate.AddDays(1) 
                : DateTime.Now.Date;

            return View(new Booking { CarId = carId });
        }

        // USER: Submit booking - redirect to payment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Booking booking)
        {
            var car = await _context.Cars.Include(c => c.Category).FirstOrDefaultAsync(c => c.Id == booking.CarId);
            if (car == null)
            {
                ModelState.AddModelError("", "The selected car does not exist.");
                ViewBag.Car = car;
                return View(booking);
            }

            if (!car.IsAvailable)
            {
                ModelState.AddModelError("", "The selected car is not available for rent.");
                ViewBag.Car = car;
                return View(booking);
            }

            // Check for overlapping bookings
            if (!await IsCarAvailableForDates(booking.CarId, booking.StartDate, booking.EndDate))
            {
                ModelState.AddModelError("", "This car is already booked for the selected dates. Please choose different dates.");
                ViewBag.Car = car;
                return View(booking);
            }

            if (booking.EndDate <= booking.StartDate)
            {
                ModelState.AddModelError("EndDate", "End date must be after start date.");
                ViewBag.Car = car;
                return View(booking);
            }

            if (booking.StartDate < DateTime.Now.Date)
            {
                ModelState.AddModelError("StartDate", "Start date cannot be in the past.");
                ViewBag.Car = car;
                return View(booking);
            }

            var days = (booking.EndDate - booking.StartDate).Days;
            if (days <= 0)
            {
                ModelState.AddModelError("", "Invalid date range.");
                ViewBag.Car = car;
                return View(booking);
            }

            // Calculate price using car's daily price
            decimal totalPrice = (decimal)days * car.DailyPrice;

            // Store booking info in TempData for payment page
            TempData["CarId"] = booking.CarId;
            TempData["StartDate"] = booking.StartDate.ToString("yyyy-MM-dd");
            TempData["EndDate"] = booking.EndDate.ToString("yyyy-MM-dd");
            TempData["TotalPrice"] = totalPrice.ToString();
            TempData["Days"] = days;

            return RedirectToAction(nameof(Payment));
        }

        // Payment page - Shows summary before Stripe payment
        public async Task<IActionResult> Payment()
        {
            if (TempData["CarId"] == null)
                return RedirectToAction(nameof(RentCars));

            var carId = (int)TempData["CarId"];
            var startDate = DateTime.Parse(TempData["StartDate"].ToString());
            var endDate = DateTime.Parse(TempData["EndDate"].ToString());

            var car = await _context.Cars.Include(c => c.Category).FirstOrDefaultAsync(c => c.Id == carId);
            
            if (car == null || !car.IsAvailable)
                return RedirectToAction(nameof(RentCars));

            // Double check availability before payment
            if (!await IsCarAvailableForDates(carId, startDate, endDate))
            {
                TempData["Error"] = "This car has been booked by someone else. Please choose another car.";
                return RedirectToAction(nameof(RentCars));
            }

            ViewBag.Car = car;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.TotalPrice = decimal.Parse(TempData["TotalPrice"].ToString());
            ViewBag.Days = (int)TempData["Days"];

            // Keep TempData for POST
            TempData.Keep();

            return View();
        }

        // Create booking and redirect to Stripe
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProceedToPayment()
        {
            try
            {
                if (TempData["CarId"] == null)
                {
                    TempData["Error"] = "Session expired. Please start over.";
                    return RedirectToAction(nameof(RentCars));
                }

                var carId = (int)TempData["CarId"];
                var startDate = DateTime.Parse(TempData["StartDate"].ToString());
                var endDate = DateTime.Parse(TempData["EndDate"].ToString());
                var totalPrice = decimal.Parse(TempData["TotalPrice"].ToString());

                var car = await _context.Cars.FindAsync(carId);
                if (car == null || !car.IsAvailable)
                {
                    TempData["Error"] = "Car is no longer available.";
                    return RedirectToAction(nameof(RentCars));
                }

                // Final check for overlapping bookings
                if (!await IsCarAvailableForDates(carId, startDate, endDate))
                {
                    TempData["Error"] = "This car has been booked by someone else. Please choose another car.";
                    return RedirectToAction(nameof(RentCars));
                }

                // Create booking
                var userId = _userManager.GetUserId(User);
                var booking = new Booking
                {
                    UserId = userId,
                    CarId = carId,
                    StartDate = startDate,
                    EndDate = endDate,
                    TotalPrice = totalPrice,
                    Status = "Pending" // Will be confirmed after payment
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                // Redirect to Stripe payment page with card form
                return RedirectToAction("Create", "Payment", new { bookingId = booking.Id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred: " + ex.Message;
                return RedirectToAction(nameof(RentCars));
            }
        }


        // Booking confirmation page
        public async Task<IActionResult> Confirmation(int id)
        {
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings
                .Include(b => b.Car)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

            if (booking == null)
                return NotFound();

            var payment = await _context.Payments.FirstOrDefaultAsync(p => p.BookingId == id);
            ViewBag.Payment = payment;

            return View(booking);
        }

        // ADMIN: View all bookings
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Car)
                .Include(b => b.User)
                .ToListAsync();

            return View(bookings);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Approve(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound();

            booking.Status = "Approved";
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Reject(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound();

            booking.Status = "Rejected";
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}

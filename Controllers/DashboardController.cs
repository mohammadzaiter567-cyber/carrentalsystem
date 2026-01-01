using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using projectadvanced.Models;

namespace projectadvanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View();
        }

        // API endpoint for dashboard statistics
        [HttpGet]
        public async Task<IActionResult> GetStatistics()
        {
            var totalCars = await _context.Cars.CountAsync();
            var availableCars = await _context.Cars.CountAsync(c => c.IsAvailable);
            var totalBookings = await _context.Bookings.CountAsync();
            var confirmedBookings = await _context.Bookings.CountAsync(b => b.Status == "Confirmed");
            var pendingBookings = await _context.Bookings.CountAsync(b => b.Status == "Pending");
            var totalCustomers = await _context.Users.CountAsync();
            var totalRevenue = await _context.Payments
                .Where(p => p.Status == "Paid")
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
            var totalCategories = await _context.Categories.CountAsync();

            var stats = new
            {
                TotalCars = totalCars,
                AvailableCars = availableCars,
                TotalBookings = totalBookings,
                ConfirmedBookings = confirmedBookings,
                PendingBookings = pendingBookings,
                TotalCustomers = totalCustomers,
                TotalRevenue = totalRevenue,
                TotalCategories = totalCategories
            };

            return Json(stats);
        }

        // API endpoint for revenue over time (last 6 months)
        [HttpGet]
        public async Task<IActionResult> GetRevenueData()
        {
            var sixMonthsAgo = DateTime.Now.AddMonths(-6);
            var payments = await _context.Payments
                .Where(p => p.Status == "Paid" && p.PaymentDate >= sixMonthsAgo)
                .Select(p => new { p.PaymentDate, p.Amount })
                .ToListAsync();

            var grouped = payments
                .GroupBy(p => new { Year = p.PaymentDate.Year, Month = p.PaymentDate.Month })
                .Select(g => new
                {
                    Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    Revenue = g.Sum(p => p.Amount)
                })
                .OrderBy(x => x.Label)
                .ToList();

            return Json(grouped);
        }

        // API endpoint for booking status distribution
        [HttpGet]
        public async Task<IActionResult> GetBookingStatusData()
        {
            var statusData = await _context.Bookings
                .GroupBy(b => b.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            return Json(statusData);
        }

        // API endpoint for bookings over time (last 6 months)
        [HttpGet]
        public async Task<IActionResult> GetBookingsOverTime()
        {
            var sixMonthsAgo = DateTime.Now.AddMonths(-6);
            var bookings = await _context.Bookings
                .Where(b => b.StartDate >= sixMonthsAgo)
                .Select(b => new { b.StartDate })
                .ToListAsync();

            var grouped = bookings
                .GroupBy(b => new { Year = b.StartDate.Year, Month = b.StartDate.Month })
                .Select(g => new
                {
                    Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    Count = g.Count()
                })
                .OrderBy(x => x.Label)
                .ToList();

            return Json(grouped);
        }

        // API endpoint for category popularity
        [HttpGet]
        public async Task<IActionResult> GetCategoryPopularity()
        {
            var categoryData = await _context.Bookings
                .Include(b => b.Car)
                .ThenInclude(c => c.Category)
                .Where(b => b.Car.Category != null)
                .GroupBy(b => b.Car.Category.Name)
                .Select(g => new
                {
                    Category = g.Key,
                    Bookings = g.Count(),
                    Revenue = g.Sum(b => b.TotalPrice)
                })
                .OrderByDescending(x => x.Bookings)
                .ToListAsync();

            return Json(categoryData);
        }

        // API endpoint for top performing cars
        [HttpGet]
        public async Task<IActionResult> GetTopCars()
        {
            var topCars = await _context.Bookings
                .Include(b => b.Car)
                .GroupBy(b => new { b.CarId, b.Car.Brand, b.Car.Model })
                .Select(g => new
                {
                    CarName = $"{g.Key.Brand} {g.Key.Model}",
                    Bookings = g.Count(),
                    Revenue = g.Sum(b => b.TotalPrice)
                })
                .OrderByDescending(x => x.Revenue)
                .Take(5)
                .ToListAsync();

            return Json(topCars);
        }

        // API endpoint for recent activity
        [HttpGet]
        public async Task<IActionResult> GetRecentActivity()
        {
            var recentBookings = await _context.Bookings
                .Include(b => b.Car)
                .Include(b => b.User)
                .OrderByDescending(b => b.StartDate)
                .Take(10)
                .Select(b => new
                {
                    Id = b.Id,
                    CarName = $"{b.Car.Brand} {b.Car.Model}",
                    CustomerEmail = b.User.Email,
                    StartDate = b.StartDate.ToString("MMM dd, yyyy"),
                    EndDate = b.EndDate.ToString("MMM dd, yyyy"),
                    TotalPrice = b.TotalPrice,
                    Status = b.Status
                })
                .ToListAsync();

            return Json(recentBookings);
        }

        // API endpoint for monthly comparison
        [HttpGet]
        public async Task<IActionResult> GetMonthlyComparison()
        {
            var currentMonth = DateTime.Now;
            var lastMonth = currentMonth.AddMonths(-1);

            var currentMonthData = await _context.Payments
                .Where(p => p.Status == "Paid" && 
                    p.PaymentDate.Year == currentMonth.Year && 
                    p.PaymentDate.Month == currentMonth.Month)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var lastMonthData = await _context.Payments
                .Where(p => p.Status == "Paid" && 
                    p.PaymentDate.Year == lastMonth.Year && 
                    p.PaymentDate.Month == lastMonth.Month)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var currentMonthBookings = await _context.Bookings
                .CountAsync(b => b.StartDate.Year == currentMonth.Year && 
                    b.StartDate.Month == currentMonth.Month);

            var lastMonthBookings = await _context.Bookings
                .CountAsync(b => b.StartDate.Year == lastMonth.Year && 
                    b.StartDate.Month == lastMonth.Month);

            var comparison = new
            {
                Revenue = new
                {
                    Current = currentMonthData,
                    Previous = lastMonthData,
                    Change = lastMonthData > 0 ? ((currentMonthData - lastMonthData) / lastMonthData * 100) : 0
                },
                Bookings = new
                {
                    Current = currentMonthBookings,
                    Previous = lastMonthBookings,
                    Change = lastMonthBookings > 0 ? ((currentMonthBookings - lastMonthBookings) / (double)lastMonthBookings * 100) : 0
                }
            };

            return Json(comparison);
        }
    }
}


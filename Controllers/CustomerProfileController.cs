using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using projectadvanced.Models;

namespace projectadvanced.Controllers
{
    [Authorize]
    public class CustomerProfileController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public CustomerProfileController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // Admin sees all customers
            if (User.IsInRole("Admin"))
            {
                var allUsers = await _userManager.Users.ToListAsync();
                var adminUsers = new List<IdentityUser>();
                
                foreach (var u in allUsers)
                {
                    if (await _userManager.IsInRoleAsync(u, "Admin"))
                        adminUsers.Add(u);
                }
                
                // Exclude admins from customer list
                var customers = allUsers.Except(adminUsers).ToList();
                return View("CustomerList", customers);
            }

            // Regular user sees their profile
            var userId = _userManager.GetUserId(User);
            var user = await _userManager.GetUserAsync(User);

            var profile = await _context.CustomerProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
                profile = new CustomerProfile { UserId = userId };

            ViewBag.UserEmail = user?.Email;
            ViewBag.UserName = user?.UserName;

            return View(profile);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index([Bind("Id,FullName,Phone,Address,LicenseNumber")] CustomerProfile profile)
        {
            // Always set UserId from the authenticated user
            var userId = _userManager.GetUserId(User);
            profile.UserId = userId;
            
            // Clear validation errors for UserId and User since we set them server-side
            ModelState.Remove(nameof(CustomerProfile.UserId));
            ModelState.Remove(nameof(CustomerProfile.User));
            ModelState.Remove("UserId");
            ModelState.Remove("User");

            if (!ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                ViewBag.UserEmail = user?.Email;
                ViewBag.UserName = user?.UserName;
                // Ensure UserId is set for the view
                profile.UserId = userId;
                return View(profile);
            }

            var existingProfile = await _context.CustomerProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (existingProfile == null)
            {
                _context.Add(profile);
            }
            else
            {
                existingProfile.FullName = profile.FullName;
                existingProfile.Phone = profile.Phone;
                existingProfile.Address = profile.Address;
                existingProfile.LicenseNumber = profile.LicenseNumber;
                _context.Update(existingProfile);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Profile saved successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}

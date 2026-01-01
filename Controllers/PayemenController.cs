using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using projectadvanced.Models;
using Stripe.Checkout;
using Stripe;
using System.Collections.Generic;

namespace projectadvanced.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public PaymentController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ---------------------------------------------
        // CREATE PAYMENT VIEW (GET) - Show payment page
        // ---------------------------------------------
        [HttpGet]
        public async Task<IActionResult> Create(int bookingId)
        {
            var booking = await _context.Bookings
                .Include(b => b.Car)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
                return NotFound();

            var payment = new Payment
            {
                BookingId = bookingId,
                Amount = booking.TotalPrice,
                PaymentMethod = "Stripe"
            };

            return View(payment);
        }

        // ---------------------------------------------
        // CREATE PAYMENT INTENT (for Stripe Elements)
        // ---------------------------------------------
        [HttpPost]
        [IgnoreAntiforgeryToken] // API endpoint doesn't need CSRF
        [AllowAnonymous] // Allow access for payment processing
        public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request)
        {
            try
            {
                // Create payment record first
                var payment = new Payment
                {
                    BookingId = request.BookingId,
                    Amount = request.Amount,
                    PaymentMethod = "Stripe",
                    Status = "Pending"
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                // Create Stripe Payment Intent
                var options = new PaymentIntentCreateOptions
                {
                    Amount = (long)(request.Amount * 100),
                    Currency = "usd",
                    PaymentMethodTypes = new List<string> { "card" },
                    Metadata = new Dictionary<string, string>
                    {
                        { "paymentId", payment.Id.ToString() },
                        { "bookingId", request.BookingId.ToString() }
                    }
                };

                var service = new PaymentIntentService();
                var paymentIntent = service.Create(options);

                // Update payment with Payment Intent ID (this is the Stripe transaction ID)
                payment.TransactionId = paymentIntent.Id;
                payment.StripeSessionId = paymentIntent.Id; // Save as StripeSessionId for consistency
                await _context.SaveChangesAsync();

                return Json(new { clientSecret = paymentIntent.ClientSecret, paymentId = payment.Id });
            }
            catch (Stripe.StripeException ex)
            {
                return Json(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Json(new { error = "An error occurred: " + ex.Message });
            }
        }

        public class CreatePaymentIntentRequest
        {
            public int BookingId { get; set; }
            public decimal Amount { get; set; }
        }

        // ---------------------------------------------
        // POST: CREATE PAYMENT + STRIPE CHECKOUT SESSION
        // ---------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int BookingId, decimal Amount, string PaymentMethod)
        {
            // Create payment object from form data
            var payment = new Payment
            {
                BookingId = BookingId,
                Amount = Amount,
                PaymentMethod = PaymentMethod ?? "Stripe"
            };
            
            // Validate
            if (BookingId <= 0)
            {
                ModelState.AddModelError("BookingId", "Booking ID is required");
                return View(payment);
            }
            
            if (Amount <= 0)
            {
                ModelState.AddModelError("Amount", "Amount must be greater than zero");
                return View(payment);
            }

            // Only create Stripe session if payment method is Stripe
            if (payment.PaymentMethod != "Stripe")
            {
                // For non-Stripe payments, save directly
                payment.Status = "Paid";
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Success), new { paymentId = payment.Id });
            }

            // 1️⃣ Save payment FIRST to get an ID
            payment.Status = "Pending";
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            // Build domain URL
            var domain = $"{Request.Scheme}://{Request.Host}";

            try
            {
                // Verify Stripe key is configured
                var stripeKey = _config["Stripe:SecretKey"]?.Trim();
                if (string.IsNullOrEmpty(stripeKey))
                {
                    ModelState.AddModelError("", "Stripe Secret Key is not configured. Please check appsettings.json");
                    return View(payment);
                }

                // 2️⃣ Create Stripe Checkout Session (now we have payment.Id)
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    Mode = "payment",
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            Quantity = 1,
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                Currency = "usd",
                                UnitAmount = (long)(payment.Amount * 100),
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = "Car Booking Payment"
                                }
                            }
                        }
                    },
                    SuccessUrl = domain + "/Payment/Success?session_id={CHECKOUT_SESSION_ID}",
                    CancelUrl = domain + "/Payment/Cancel?paymentId=" + payment.Id,
                    Metadata = new Dictionary<string, string>
                    {
                        { "paymentId", payment.Id.ToString() },
                        { "bookingId", BookingId.ToString() }
                    }
                };

                var service = new SessionService();
                Session session = service.Create(options);

                // 3️⃣ Update payment with Stripe Session ID
                payment.StripeSessionId = session.Id;
                await _context.SaveChangesAsync();

                // 4️⃣ Redirect user to Stripe's hosted payment page
                return Redirect(session.Url);
            }
            catch (Stripe.StripeException ex)
            {
                // Handle Stripe errors
                if (ex.Message.Contains("Invalid API Key") || ex.Message.Contains("authentication"))
                {
                    ModelState.AddModelError("", $"Invalid Stripe API Key. Please check your keys in appsettings.json. Error: {ex.Message}");
                }
                else
                {
                    ModelState.AddModelError("", $"Stripe error: {ex.Message}");
                }
                _context.Payments.Remove(payment);
                await _context.SaveChangesAsync();
                return View(payment);
            }
            catch (Exception ex)
            {
                // Handle other errors
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                _context.Payments.Remove(payment);
                await _context.SaveChangesAsync();
                return View(payment);
            }
        }

        // ---------------------------------------------
        // PAYMENT SUCCESS - Simple flow
        // ---------------------------------------------
        public async Task<IActionResult> Success(string session_id)
        {
            // Require session_id from Stripe
            if (string.IsNullOrEmpty(session_id))
            {
                TempData["Error"] = "Invalid payment session. Please complete payment through Stripe.";
                return RedirectToAction("Index", "Booking");
            }

            Payment payment = null;

            // Find payment by Stripe session ID
            payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.StripeSessionId == session_id);

            if (payment == null)
            {
                TempData["Error"] = "Payment record not found. Please contact support.";
                return RedirectToAction("Index", "Booking");
            }

            // Verify payment with Stripe - REQUIRED
            try
            {
                var service = new SessionService();
                var session = service.Get(session_id);

                // Only mark as paid if Stripe confirms payment
                if (session.PaymentStatus == "paid")
                {
                    payment.Status = "Paid";
                    payment.TransactionId = session.PaymentIntentId;
                    payment.StripeSessionId = session_id;
                    
                    // Get card last 4 digits from payment intent
                    if (!string.IsNullOrEmpty(session.PaymentIntentId))
                    {
                        try
                        {
                            var paymentIntentService = new PaymentIntentService();
                            var paymentIntent = paymentIntentService.Get(session.PaymentIntentId, new PaymentIntentGetOptions
                            {
                                Expand = new List<string> { "payment_method" }
                            });
                            
                            // Get payment method details
                            if (paymentIntent.PaymentMethod != null)
                            {
                                string paymentMethodId = paymentIntent.PaymentMethod.Id ?? paymentIntent.PaymentMethodId;
                                if (!string.IsNullOrEmpty(paymentMethodId))
                                {
                                    var paymentMethodService = new PaymentMethodService();
                                    var paymentMethod = paymentMethodService.Get(paymentMethodId);
                                    
                                    if (paymentMethod.Card != null && !string.IsNullOrEmpty(paymentMethod.Card.Last4))
                                    {
                                        payment.CardLast4 = paymentMethod.Card.Last4;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // If we can't get card details, continue without them
                        }
                    }
                    
                    await _context.SaveChangesAsync();

                    // Confirm booking
                    if (payment.BookingId > 0)
                    {
                        var booking = await _context.Bookings.FindAsync(payment.BookingId);
                        if (booking != null && booking.Status == "Pending")
                        {
                            booking.Status = "Confirmed";
                            await _context.SaveChangesAsync();
                        }
                    }

                    // Show success message
                    TempData["Success"] = "Payment successful! Your booking is confirmed.";
                    
                    // Pass payment to view
                    ViewBag.Payment = payment;
                    return View(payment);
                }
                else
                {
                    // Payment not completed in Stripe
                    TempData["Error"] = $"Payment not completed. Status: {session.PaymentStatus}";
                    return RedirectToAction("Index", "Booking");
                }
            }
            catch (Stripe.StripeException ex)
            {
                // Stripe verification failed - don't mark as paid
                TempData["Error"] = $"Payment verification failed: {ex.Message}. Please contact support.";
                return RedirectToAction("Index", "Booking");
            }
            catch (Exception ex)
            {
                // Other errors - don't mark as paid
                TempData["Error"] = $"An error occurred: {ex.Message}. Please contact support.";
                return RedirectToAction("Index", "Booking");
            }
        }

        // ---------------------------------------------
        // PAYMENT CANCEL
        // ---------------------------------------------
        public async Task<IActionResult> Cancel(int? paymentId)
        {
            if (paymentId.HasValue)
            {
                var payment = await _context.Payments.FindAsync(paymentId.Value);
                if (payment != null && payment.Status == "Pending")
                {
                    payment.Status = "Cancelled";
                    await _context.SaveChangesAsync();
                }
            }
            return View();
        }

    }
}

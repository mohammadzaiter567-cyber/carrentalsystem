using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using projectadvanced.Data;
using Stripe;

namespace projectadvanced
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ---------------------------------------
            // DATABASE (SQL Server)
            // ---------------------------------------
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    connectionString,
                    sqlOptions => sqlOptions.EnableRetryOnFailure()
                )
            );
            // ---------------------------------------
            // Identity + Roles
            // ---------------------------------------
            builder.Services.AddDefaultIdentity<IdentityUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

            builder.Services.AddControllersWithViews();

            // ---------------------------------------
            // STRIPE CONFIGURATION
            // ---------------------------------------
            var stripeSecretKey = builder.Configuration["Stripe:SecretKey"]?.Trim();
            if (string.IsNullOrEmpty(stripeSecretKey))
            {
                stripeSecretKey = builder.Configuration.GetSection("Stripe")["SecretKey"]?.Trim();
            }
            if (!string.IsNullOrEmpty(stripeSecretKey))
            {
                StripeConfiguration.ApiKey = stripeSecretKey;
            }
            else
            {
                throw new InvalidOperationException("Stripe Secret Key is not configured in appsettings.json");
            }

            var app = builder.Build();

            // ---------------------------------------
            // Seed Roles + Default Admin
            // ---------------------------------------
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

                string[] roles = { "Admin", "User" };

                foreach (string role in roles)
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        await roleManager.CreateAsync(new IdentityRole(role));
                    }
                }

                // Create default admin
                string adminEmail = "admin@system.com";
                string adminPassword = "Admin123!";

                var adminUser = await userManager.FindByEmailAsync(adminEmail);
                if (adminUser == null)
                {
                    adminUser = new IdentityUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        EmailConfirmed = true
                    };

                    await userManager.CreateAsync(adminUser, adminPassword);
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // ---------------------------------------
            // Middleware pipeline
            // ---------------------------------------
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            
            // Block Stripe wallet-config requests via middleware
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Value?.Contains("wallet-config") == true ||
                    context.Request.Path.Value?.Contains("merchant-ui-api") == true)
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{}");
                    return;
                }
                await next();
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.MapRazorPages();

            app.Run();
        }
    }
}

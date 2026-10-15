using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
internal static class AdminCli
{
    public static async Task<int> Run(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            if (args is ["migrate"])
            {
                await db.Database.MigrateAsync();
                Console.WriteLine("Database migration complete.");
                return 0;
            }

            if (args is not ["admin", "create", _, _ ] && args is not ["admin", "reset-password", _ ])
            {
                Console.Error.WriteLine("Usage: migrate | admin create <userName> <Admin|Operator|ReadOnly> | admin reset-password <userName>");
                return 1;
            }

            if (args[1] == "create" && args[3] is not "Admin" and not "Operator" and not "ReadOnly")
            {
                Console.Error.WriteLine("Invalid role.");
                return 1;
            }

            var password = Environment.GetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD");
            Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", null);
            if (string.IsNullOrEmpty(password))
                password = ReadPassword();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
            AdminUser user;
            IdentityResult result;
            if (args[1] == "create")
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                if (!await roles.RoleExistsAsync(args[3]))
                    throw new CliException("ROLE_NOT_FOUND");
                user = new()
                {
                    UserName = args[2],
                    MustChangePassword = args[3] != "Admin"
                };
                result = await users.CreateAsync(user, password);
                Check(result);
                Check(await users.AddToRoleAsync(user, args[3]));
            }
            else
            {
                user = await users.FindByNameAsync(args[2]) ?? throw new CliException("ACCOUNT_NOT_FOUND");
                user.MustChangePassword = true;
                user.LoginChallengeId = null;
                AccountSecurityService.ClearPending(user);
                var token = await users.GeneratePasswordResetTokenAsync(user);
                result = await users.ResetPasswordAsync(user, token, password);
                Check(result);
                Check(await users.SetLockoutEndDateAsync(user, null));
                Check(await users.ResetAccessFailedCountAsync(user));
            }

            db.AuditLogs.Add(new() { ActorId = "cli", Action = args[1] == "create" ? "admin.create" : "admin.reset-password", TargetId = Guid.Parse(user.Id), Changes = args[1] == "create" ? System.Text.Json.JsonSerializer.Serialize(new { role = args[3] }) : "{}", CreatedAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(), TraceId = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Console.WriteLine("Account operation complete.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Operation failed: " + (error is CliException safe ? safe.Message : error.GetType().Name));
            return 1;
        }
    }

    private sealed class CliException(string message) : Exception(message);
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new CliException(string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
            throw new CliException("Set KEYVALIDATION_ADMIN_PASSWORD for non-interactive use.");
        Console.Write("Password: ");
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }

        Console.WriteLine();
        return password.ToString();
    }
}

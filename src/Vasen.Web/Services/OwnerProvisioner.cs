using Microsoft.AspNetCore.Identity;
using Vasen.Web.Models;
using Vasen.Web.Data;
namespace Vasen.Web.Services;
public static class OwnerProvisioner
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<Member>>();
        Console.Write("Namn: "); var name = Console.ReadLine()?.Trim();
        Console.Write("E-post: "); var email = Console.ReadLine()?.Trim();
        Console.Write("Tillfälligt lösenord (minst 10 tecken, stor/liten bokstav och siffra): ");
        var password = "";
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password = password[..^1]; }
            else if (!char.IsControl(key.KeyChar)) password += key.KeyChar;
        }
        Console.WriteLine();
        await using var tx = await services.GetRequiredService<VasenDb>().Database.BeginTransactionAsync();
        await services.GetRequiredService<MemberService>().ValidateContactAsync(name ?? "", email ?? "");
        var result = await users.CreateAsync(new Member { DisplayName = name!, Email = email, UserName = email, IsOwner = true, MustChangePassword = true }, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
        await tx.CommitAsync();
        Console.WriteLine("Ägaren är skapad och väljer eget lösenord vid första inloggningen. Kör kommandot igen för nästa person.");
    }
}

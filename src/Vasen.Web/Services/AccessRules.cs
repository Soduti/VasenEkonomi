using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vasen.Web.Data;
using Vasen.Web.Models;

namespace Vasen.Web.Services;

public static class AccessRules
{
    public static bool IsActive(Member? member) => member is { IsActive: true, IsAnonymized: false, MustChangePassword: false };
    public static bool IsOwner(Member? member) => IsActive(member) && member!.IsOwner;
    public static bool CanRegister(Member? member) => IsActive(member) && (member!.IsOwner || member.AccessLevel == MemberAccess.ReceiptRegistrar);
    public static bool CanEdit(Member? member, Receipt receipt) => IsOwner(member) ||
        (CanRegister(member) && receipt.CreatedById == member!.Id && receipt.WithdrawalId == null && receipt.DeletedUtc == null);

    public static async Task<Member> ActorAsync(VasenDb db, string actor) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor) is { } member && IsActive(member)
            ? member : throw new UnauthorizedAccessException();

    public static async Task RequireOwnerAsync(VasenDb db, string actor)
    {
        if (!IsOwner(await ActorAsync(db, actor))) throw new UnauthorizedAccessException();
    }
}

public class RequestAccess(VasenDb db)
{
    public Member? Member { get; private set; }
    public bool IsOwner => AccessRules.IsOwner(Member);
    public bool CanRegisterReceipts => AccessRules.CanRegister(Member);
    public bool CanEdit(Receipt receipt) => AccessRules.CanEdit(Member, receipt);
    public async Task LoadAsync(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        Member = id == null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
namespace Vasen.Web.Models;

public enum MemberAccess { Reader = 0, ReceiptRegistrar = 1 }

public class Member : IdentityUser
{
    [MaxLength(100)] public string DisplayName { get; set; } = "";
    public bool IsOwner { get; set; }
    public MemberAccess AccessLevel { get; set; }
    public string AccessName => IsOwner ? "Ägare" : AccessLevel == MemberAccess.ReceiptRegistrar ? "Kvittoregistrerare" : "Läsare";
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public bool IsAnonymized { get; set; }
}
public class Receipt
{
    public string? CreatedById { get; set; }
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string MemberId { get; set; } = "";
    public Member Member { get; set; } = null!;
    public long AmountOre { get; set; }
    [MaxLength(300)] public string Description { get; set; } = "";
    [MaxLength(80)] public string? Category { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedUtc { get; set; }
    public int? WithdrawalId { get; set; }
    public Withdrawal? Withdrawal { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
    public List<ReceiptFile> Files { get; set; } = [];
}
public class ReceiptFile
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public string StorageName { get; set; } = "";
    [MaxLength(255)] public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
}
public class Withdrawal
{
    public int Id { get; set; }
    public string MemberId { get; set; } = "";
    public Member Member { get; set; } = null!;
    public DateOnly Date { get; set; }
    public long AmountOre { get; set; }
    public long OriginalReceiptTotalOre { get; set; }
    public long OwnContributionOre { get; set; }
    [MaxLength(300)] public string? Comment { get; set; }
    public bool IsUndone { get; set; }
    [ConcurrencyCheck] public Guid Version { get; set; } = Guid.NewGuid();
    public List<Receipt> Receipts { get; set; } = [];
}
public class AuditEntry
{
    public int Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string ActorId { get; set; } = "";
    public string Entity { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

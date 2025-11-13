namespace DomainServices.Models;

public class Groups
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public long CRNo { get; set; }
    public long VATNo { get; set; }
    public int? WalletNo { get; set; }
    public string City { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? CompanyLogoName { get; set; }
    public byte[]? CompanyLogo { get; set; }
    public int? GroupId { get; set; }
    public bool? MainBranch { get; set; }
    public int? ParentId { get; set; }
    public bool Status { get; set; }
    public string? TransactionsTable { get; set; }
}

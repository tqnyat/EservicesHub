using Microsoft.AspNetCore.Identity;

namespace DomainServices.Data
{
    public class Users : IdentityUser 
    {
        public long IdNo { get; set; }
        public string FirstName { get; set; }
        public string? MidName { get; set; }
        public string? ThirdName { get; set; }
        public string LastName { get; set; }
        public string? Nationality { get; set; }
        public string? Gender { get; set; }

        public DateTime LastUpd { get; set; } = DateTime.Now;
        public string LastUpdBy { get; set; } = "1";
        public DateTime Created { get; set; } = DateTime.Now;
        public string CreatedBy { get; set; } = "1";
        public int ModificationCount { get; set; } = 0;
        public int GroupId { get; set; } = 1;
        public int RoleId { get; set; } = 0;
        public string? Address { get; set; }

        public string? Comment { get; set; }

        public byte[]? ProfileImage { get; set; }

        public int UserGroupId { get; set; } = 1;
        public bool IsSalesperson { get; set; } = false;
        public bool LockoutEnabled { get; set; } = false;

        public string? ApplicationTheme { get; set; }

        public string? JobTitle { get; set; }

        public string? Title { get; set; }

        public int Status { get; set; } = 1;
        public string? WorkPlace { get; set; }

        public string? OTP { get; set; }
        public string? EmailOTP { get; set; }
        public string? GUID { get; set; }
        public int? DivisionId { get; set; }
        public int? SentCounts { get; set; }
        public DateTime? LastInvitationDate { get; set; }
        public string? GeneratedPassword { get; set; }
        public int? OTPSentCount { get; set; }
        public string? OTPSendSource { get; set; }
        public DateTime? OTPSentTime { get; set; }
        public DateTime? ResetPasswordDate { get; set; } = DateTime.Now;
        public string? Language { get; set; } = "en";

    }
}

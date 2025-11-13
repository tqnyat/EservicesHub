using DomainServices.Data.UserIdentity;
using DomainServices.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DomainServices.Data.Repository
{
    public class DomainDBContext : DbContext
    {
        public DomainDBContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<LocalResource> LocalResources { get; set; }
        public DbSet<Groups> Groups { get; set; }
        public DbSet<SendNotifications> SendNotifications { get; set; }
        public DbSet<SignedInUsers> SignedInUsers { get; set; }

        public class DomainRepo
        {
            private readonly DomainDBContext _context;

            public DomainRepo(DomainDBContext context)
            {
                _context = context;
            }

            public async Task<Groups?> GetGroupByIdAsync(int groupId)
            {
                return await _context.Groups.FindAsync(groupId);
            }

            public int CreateSendNotification(SendNotifications notifications)
            {
                var notification = _context.SendNotifications.Add(notifications);
                _context.SaveChanges();
                //var Notifications = _context.SendNotifications.OrderByDescending(notifications => notifications.id).First();
                return notifications.id;
            }

            public List<SignedInUsers>? GetSignedInUsers(string userId, string? sessionKey = null)
            {
                //var SignedInUsers = _context.SignedInUsers.Where(s => s.CreatedBy == userId).ToList();
                string sql = "";
                if (sessionKey != null)
                {
                    sql = $"SELECT * FROM [SignedInUsers] WHERE CreatedBy = '{userId}' AND Active = 1 AND SessionKey = '{sessionKey}'";
                }
                else
                {
                    sql = $"SELECT * FROM [SignedInUsers] WHERE CreatedBy = '{userId}' AND Active = 1";
                }
                var SignedInUsers = _context.SignedInUsers.FromSqlRaw(sql).ToListAsync().Result;
                return SignedInUsers;
            }
            public int UpdateSignInUser(SignedInUsers signedInUsers)
            {
                _context.SignedInUsers.Update(signedInUsers);
                _context.SaveChanges();
                return signedInUsers.Id;
            }
            public bool UpdatePingSessionTimeAsync(SignedInUsers signedInUsers)
            {
                if (signedInUsers == null)
                    return false;

                _context.SignedInUsers.Attach(signedInUsers);
                _context.Entry(signedInUsers).Property(u => u.LastUpd).IsModified = true;
                var result = _context.SaveChangesAsync().Result;
                return result > 0;
            }
            public int CreateSignInUser(SignedInUsers signedInUsers)
            {
                var signedInUser = _context.SignedInUsers.Add(signedInUsers);
                _context.SaveChanges();
                //var signnedInUsers = _context.SignedInUsers.OrderByDescending(s => s.Id).FirstOrDefault();
                return signedInUsers.Id;
            }
        }
    }
}

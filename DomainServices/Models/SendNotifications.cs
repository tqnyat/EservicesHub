namespace DomainServices.Models;

public class SendNotifications
{
    public int id { get; set; }
    public string NotificationTitle { get; set; }
    public string? NotificationTitleAr { get; set; } = "العنوان العربي";
    public string NotificationText { get; set; }
    public string? NotificationTextAr { get; set; } = "النص العربي"; 
    public bool ReadStatus { get; set; }
    public bool IsImportant { get; set; } 
    public string? AssignToCompany { get; set; }
    public string? AssignToUser { get; set; }
    public string SendTo { get; set; }
    public int? GroupId { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? ScheduleTime { get; set; }
    public string CreatedBy { get; set; }
    public string LastUpdBy { get; set; }
    public DateTime LastUpd  { get; set; } = DateTime.Now;
    public string Status { get; set; }
    public bool Schedule { get; set; }
    public bool Web { get; set; }
    public bool Email { get; set; }
    public bool Mobile { get; set; }
    public bool SMS { get; set; }



}

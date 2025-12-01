using System;

namespace JobBoard.Models
{
    public class Job
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime PostedDate { get; set; } = DateTime.Now;
        public string Type { get; set; } = "Full-time"; // Full-time, Part-time, Contract
        public string SalaryRange { get; set; } = string.Empty;
    }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace JobBoard.Models
{
    public class Applicant
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string FirstName { get; set; }
        
        [Required]
        [StringLength(100)]
        public string LastName { get; set; }
        
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        
        [Phone]
        public string PhoneNumber { get; set; }
        
        public DateTime DateOfBirth { get; set; }
        
        public string Address { get; set; }
        
        public string City { get; set; }
        
        public string State { get; set; }
        
        public string ZipCode { get; set; }
        
        public string Country { get; set; }
        
        public string LinkedInProfile { get; set; }
        
        public string PortfolioUrl { get; set; }
        
        public string Summary { get; set; }
        
        public string ResumePath { get; set; }
        
        public DateTime AppliedDate { get; set; } = DateTime.Now;
    }
}
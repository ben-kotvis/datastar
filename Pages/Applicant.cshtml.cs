using JobBoard.Helpers;
using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StarFederation.Datastar.DependencyInjection;

namespace JobBoard.Pages
{
    public class ApplicantModel : PageModel
    {
        private readonly ApplicantService _applicantService;
        private readonly IDatastarService _dataStarService;

        public ApplicantModel(ApplicantService applicantService, IDatastarService dataStarService)
        {
            _applicantService = applicantService;
            _dataStarService = dataStarService;
        }

        [BindProperty]
        public string FirstName { get; set; } = string.Empty;
        [BindProperty]
        public string LastName { get; set; } = string.Empty;
        [BindProperty]
        public string Email { get; set; } = string.Empty;
        [BindProperty]
        public string PhoneNumber { get; set; } = string.Empty;
        [BindProperty]
        public string DateOfBirth { get; set; } = string.Empty;
        [BindProperty]
        public string Address { get; set; } = string.Empty;
        [BindProperty]
        public string City { get; set; } = string.Empty;
        [BindProperty]
        public string State { get; set; } = string.Empty;
        [BindProperty]
        public string ZipCode { get; set; } = string.Empty;
        [BindProperty]
        public string Country { get; set; } = string.Empty;
        [BindProperty]
        public string LinkedInProfile { get; set; } = string.Empty;
        [BindProperty]
        public string PortfolioUrl { get; set; } = string.Empty;
        [BindProperty]
        public string Summary { get; set; } = string.Empty;
        [BindProperty]
        public string ResumePath { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostSubmitAsync()
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(Email))
            {
                // In a real app, return validation errors. 
                // For now, just do nothing or return error fragment.
                return new EmptyResult();
            }

            var applicant = new Applicant
            {
                FirstName = FirstName,
                LastName = LastName,
                Email = Email,
                PhoneNumber = PhoneNumber,
                DateOfBirth = DateTime.Parse(DateOfBirth),
                Address = Address,
                City = City,
                State = State,
                ZipCode = ZipCode,
                Country = Country,
                LinkedInProfile = LinkedInProfile,
                PortfolioUrl = PortfolioUrl,
                Summary = Summary,
                ResumePath = ResumePath
            };

            _applicantService.Add(applicant);

            // Return success fragment
            var successHtml = @"
                <div id='applicant-form-container' class='alert alert-success'>
                    <h4 class='alert-heading'>Application Submitted!</h4>
                    <p>Your application has been successfully submitted.</p>
                    <hr>
                    <a href='/Index' class='btn btn-primary'>View Jobs</a>
                    <a href='/Applicant' class='btn btn-outline-secondary'>Apply for Another</a>
                </div>";
            await _dataStarService.PatchElementsAsync(successHtml);
            return new EmptyResult();
        }
    }
}
using JobBoard.Helpers;
using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StarFederation.Datastar.DependencyInjection;

namespace JobBoard.Pages
{
    public class PostModel : PageModel
    {
        private readonly JobService _jobService;

        private readonly IDatastarService _dataStarService;
        public PostModel(JobService jobService, IDatastarService datastarService)
        {
            _jobService = jobService;
            _dataStarService = datastarService;
        }

        [BindProperty]
        public string Title { get; set; } = string.Empty;
        [BindProperty]
        public string Company { get; set; } = string.Empty;
        [BindProperty]
        public string Location { get; set; } = string.Empty;
        [BindProperty]
        public string Description { get; set; } = string.Empty;
        [BindProperty]
        public string Type { get; set; } = "Full-time";
        [BindProperty]
        public string SalaryRange { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostSubmitAsync()
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Company))
            {
                 // In a real app, return validation errors. 
                 // For now, just do nothing or return error fragment.
                return new EmptyResult();
            }

            var job = new Job
            {
                Title = Title,
                Company = Company,
                Location = Location,
                Description = Description,
                Type = Type,
                SalaryRange = SalaryRange
            };

            _jobService.Add(job);

            // Return success fragment
            var successHtml = @"
                <div id='post-form-container' class='alert alert-success'>
                    <h4 class='alert-heading'>Job Posted!</h4>
                    <p>Your job listing has been successfully created.</p>
                    <hr>
                    <a href='/Index' class='btn btn-primary'>View Jobs</a>
                    <a href='/Post' class='btn btn-outline-secondary'>Post Another</a>
                </div>";
            await _dataStarService.PatchElementsAsync(successHtml);
            return new EmptyResult();
        }
    }
}

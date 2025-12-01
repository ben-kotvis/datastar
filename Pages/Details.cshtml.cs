using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobBoard.Pages
{
    public class DetailsModel : PageModel
    {
        private readonly JobService _jobService;

        public DetailsModel(JobService jobService)
        {
            _jobService = jobService;
        }

        public Job Job { get; set; }

        public IActionResult OnGet(int id)
        {
            Job = _jobService.GetById(id);
            if (Job == null)
            {
                return NotFound();
            }
            return Page();
        }
    }
}

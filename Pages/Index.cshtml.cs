using JobBoard.Helpers;
using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StarFederation.Datastar.DependencyInjection;

namespace JobBoard.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly JobService _jobService;
        private readonly RazorViewToStringRenderer _renderer;
        private readonly IDatastarService _dataStarService;

        public IndexModel(ILogger<IndexModel> logger, JobService jobService, RazorViewToStringRenderer renderer, IDatastarService dataStarService)
        {
            _logger = logger;
            _jobService = jobService;
            _renderer = renderer;
            _dataStarService = dataStarService;
        }

        public IEnumerable<Job> Jobs { get; set; } = new List<Job>();

        [BindProperty(SupportsGet = true)]
        public string Query { get; set; } = string.Empty;

        public void OnGet()
        {
            Jobs = _jobService.GetAll();
        }

        public async Task<IActionResult> OnGetSearchAsync()
        {
            _logger.LogInformation("Search query: {Query}", Query);
            foreach (var key in Request.Query.Keys)
            {
                _logger.LogInformation("Query Key: {Key}, Value: {Value}", key, Request.Query[key]);
            }
            // Check for Datastar store in headers or params
            // Usually it might be in 'datastar' param
            if (Request.Query.ContainsKey("datastar"))
            {
                Query = "DevOps";
                _logger.LogInformation("Datastar param found: {Value}", Request.Query["datastar"]);
            }
            var jobs = _jobService.GetAll();
            if (!string.IsNullOrWhiteSpace(Query))
            {
                jobs = jobs.Where(j => 
                    j.Title.Contains(Query, StringComparison.OrdinalIgnoreCase) || 
                    j.Company.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                    j.Location.Contains(Query, StringComparison.OrdinalIgnoreCase));
            }

            var html = await _renderer.RenderViewToStringAsync("_JobList", jobs, PageContext);
            
            await _dataStarService.PatchElementsAsync(html);
            // SendMergeFragments(html, selector: "#job-list");
            return new EmptyResult();
        }
    }
}

using JobBoard.Models;
using System.Collections.Concurrent;

namespace JobBoard.Services
{
    public class JobService
    {
        private readonly ConcurrentDictionary<int, Job> _jobs = new();
        private int _nextId = 1;

        public JobService()
        {
            // Seed some data
            Add(new Job { Title = "Senior C# Developer", Company = "TechCorp", Location = "Remote", Description = "We are looking for an experienced C# developer to join our team.", Type = "Full-time", SalaryRange = "$120k - $150k" });
            Add(new Job { Title = "Frontend Engineer", Company = "WebSolutions", Location = "New York, NY", Description = "Join us to build the next generation of web applications.", Type = "Full-time", SalaryRange = "$100k - $130k" });
            Add(new Job { Title = "DevOps Specialist", Company = "CloudSystems", Location = "Austin, TX", Description = "Help us scale our infrastructure.", Type = "Contract", SalaryRange = "$80/hr" });
        }

        public IEnumerable<Job> GetAll()
        {
            return _jobs.Values.OrderByDescending(j => j.PostedDate);
        }

        public Job? GetById(int id)
        {
            _jobs.TryGetValue(id, out var job);
            return job;
        }

        public void Add(Job job)
        {
            job.Id = _nextId++;
            job.PostedDate = DateTime.Now;
            _jobs.TryAdd(job.Id, job);
        }
    }
}

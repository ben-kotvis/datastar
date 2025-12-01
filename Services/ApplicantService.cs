using JobBoard.Models;
using System.Collections.Concurrent;

namespace JobBoard.Services
{
    public class ApplicantService
    {
        private readonly ConcurrentDictionary<int, Applicant> _applicants = new();
        private int _nextId = 1;

        public IEnumerable<Applicant> GetAll()
        {
            return _applicants.Values.OrderByDescending(a => a.AppliedDate);
        }

        public Applicant? GetById(int id)
        {
            _applicants.TryGetValue(id, out var applicant);
            return applicant;
        }

        public void Add(Applicant applicant)
        {
            applicant.Id = _nextId++;
            applicant.AppliedDate = DateTime.Now;
            _applicants.TryAdd(applicant.Id, applicant);
        }
    }
}
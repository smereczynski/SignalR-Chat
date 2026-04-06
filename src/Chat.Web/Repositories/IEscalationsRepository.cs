using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    public interface IEscalationsRepository
    {
        Task<Escalation> CreateAsync(Escalation escalation);
        Task<Escalation> GetByIdAsync(string id, string roomName);
        Task<IEnumerable<Escalation>> GetByRoomAsync(string roomName, int take = 50);
        Task<IEnumerable<Escalation>> GetDueScheduledAsync(DateTime dueBeforeUtc, int take = 100);
        Task<Escalation> GetOpenByMessageIdAsync(int messageId);
        Task UpsertAsync(Escalation escalation);
    }
}

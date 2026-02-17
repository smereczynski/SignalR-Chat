using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for dispatch center storage and user assignment management.
    /// </summary>
    public interface IDispatchCentersRepository
    {
        Task<IEnumerable<DispatchCenter>> GetAllAsync();
        Task<DispatchCenter> GetByIdAsync(string id);
        Task<DispatchCenter> GetByNameAsync(string name);
        Task UpsertAsync(DispatchCenter dispatchCenter);
        Task DeleteAsync(string id);
        Task AssignUserAsync(string dispatchCenterId, string userName);
        Task UnassignUserAsync(string dispatchCenterId, string userName);
    }
}

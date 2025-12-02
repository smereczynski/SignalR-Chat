using System.Collections.Generic;
using System.Threading.Tasks;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for user profile lookup and persistence used by auth + presence features.
    /// </summary>
    public interface IUsersRepository
    {
        Task<ApplicationUser> GetByUserNameAsync(string userName);
        Task<ApplicationUser> GetByUpnAsync(string upn);
        Task<IEnumerable<ApplicationUser>> GetAllAsync();
        Task UpsertAsync(ApplicationUser user);
    }
}

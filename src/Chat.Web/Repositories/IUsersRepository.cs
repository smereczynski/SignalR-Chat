using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    /// <summary>
    /// Abstraction for user profile lookup and persistence used by auth + presence features.
    /// </summary>
    public interface IUsersRepository
    {
        ApplicationUser GetByUserName(string userName);
        ApplicationUser GetByUpn(string upn);
        IEnumerable<ApplicationUser> GetAll();
        void Upsert(ApplicationUser user);
    }
}

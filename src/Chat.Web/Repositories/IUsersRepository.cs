using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    public interface IUsersRepository
    {
        ApplicationUser GetByUserName(string userName);
        IEnumerable<ApplicationUser> GetAll();
        void Upsert(ApplicationUser user);
    }
}

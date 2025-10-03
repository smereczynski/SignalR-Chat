using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    public interface IRoomsRepository
    {
        IEnumerable<Room> GetAll();
        Room GetById(int id);
        Room GetByName(string name);
        Room Create(Room room);
        void Update(Room room);
        void Delete(int id);
    }
}

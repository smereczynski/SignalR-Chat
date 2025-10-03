using System.Collections.Generic;
using Chat.Web.Models;

namespace Chat.Web.Repositories
{
    public interface IMessagesRepository
    {
        Message GetById(int id);
        IEnumerable<Message> GetRecentByRoom(string roomName, int take = 20);
        Message Create(Message message);
        void Delete(int id, string byUserName);
    }
}

namespace Chat.Web.ViewModels
{
    /// <summary>
    /// Minimal user presence model exposed to clients (omits sensitive data).
    /// </summary>
    public class UserViewModel
    {
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Avatar { get; set; }
        public string CurrentRoom { get; set; }
    }
}

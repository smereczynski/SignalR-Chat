namespace Chat.Web.Options
{
    public class CosmosOptions
    {
        public string ConnectionString { get; set; }
        public string Database { get; set; } = "chat";
        public string MessagesContainer { get; set; } = "messages";
        public string UsersContainer { get; set; } = "users";
        public string RoomsContainer { get; set; } = "rooms";
        // When null, TTL is disabled (container's DefaultTimeToLive not set). When -1, TTL enabled but items never expire by default.
        public int? MessagesTtlSeconds { get; set; } = 604800; // 7 days default
        public bool AutoCreate { get; set; } = true; // Automatically create database/containers if missing
    }
}

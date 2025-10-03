namespace Chat.Web.Options
{
    public class RedisOptions
    {
        public string ConnectionString { get; set; }
        public int Database { get; set; } = 0;
        public int OtpTtlSeconds { get; set; } = 300; // 5 minutes
    }
}

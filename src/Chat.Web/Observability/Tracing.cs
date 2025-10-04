using System.Diagnostics;

namespace Chat.Web.Observability
{
    public static class Tracing
    {
        public const string ServiceName = "Chat.Web";
        public static readonly ActivitySource ActivitySource = new ActivitySource(ServiceName);
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Chat.Tests
{
    public class UrlIsLocalUrlTests
    {
        private static IUrlHelper CreateUrlHelper()
        {
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            return new UrlHelper(actionContext);
        }

        [Theory]
        [InlineData("/chat", true)]
        [InlineData("/profile", true)]
        [InlineData("/chat?x=1", true)]
        [InlineData("/chat#frag", true)]
        [InlineData("//evil", false)]
        [InlineData("/\\evil", false)]
        [InlineData("https://evil", false)]
        [InlineData("http://example.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsLocalUrl_BasicCases(string? path, bool expected)
        {
            var url = CreateUrlHelper();
            var actual = url.IsLocalUrl(path);
            Assert.Equal(expected, actual);
        }
    }
}

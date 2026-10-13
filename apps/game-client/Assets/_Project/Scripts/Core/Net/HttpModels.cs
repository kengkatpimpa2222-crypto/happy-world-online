using System.Collections.Generic;
using System.Threading.Tasks;

namespace HappyWorld.Core.Net
{
    /// <summary>Plain HTTP request description so the API layer does not depend on Unity or HttpClient.</summary>
    public sealed class HttpRequestSpec
    {
        public string Method = "GET";
        public string Url = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        /// <summary>JSON body, or null.</summary>
        public string Body;
    }

    public sealed class HttpResult
    {
        public int Status;
        public string Body = "";
        /// <summary>Set when the request never reached the server (no network, DNS, TLS...).</summary>
        public string TransportError;
    }

    /// <summary>Implemented by UnityWebRequestTransport (game) and DotNetHttpTransport (tests, tools).</summary>
    public interface IHttpTransport
    {
        Task<HttpResult> SendAsync(HttpRequestSpec request);
    }
}

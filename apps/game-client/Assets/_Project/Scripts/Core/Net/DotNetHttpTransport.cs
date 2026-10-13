using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HappyWorld.Core.Net
{
    /// <summary>HttpClient-based transport. Used by the .NET test suite and editor tools.</summary>
    public sealed class DotNetHttpTransport : IHttpTransport
    {
        private readonly HttpClient _client;

        public DotNetHttpTransport(HttpClient client = null)
        {
            _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<HttpResult> SendAsync(HttpRequestSpec spec)
        {
            using (var req = new HttpRequestMessage(new HttpMethod(spec.Method), spec.Url))
            {
                foreach (var h in spec.Headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                if (spec.Body != null) req.Content = new StringContent(spec.Body, Encoding.UTF8, "application/json");
                try
                {
                    using (var res = await _client.SendAsync(req).ConfigureAwait(false))
                    {
                        var body = res.Content == null ? "" : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return new HttpResult { Status = (int)res.StatusCode, Body = body };
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
                {
                    return new HttpResult { Status = 0, TransportError = e.Message };
                }
            }
        }
    }
}

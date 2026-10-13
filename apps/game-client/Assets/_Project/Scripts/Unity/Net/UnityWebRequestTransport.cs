using System.Text;
using System.Threading.Tasks;
using HappyWorld.Core.Net;
using UnityEngine.Networking;

namespace HappyWorld.Unity.Net
{
    /// <summary>
    /// IHttpTransport on UnityWebRequest (works on Android/iOS/PC, respects platform proxy and TLS).
    /// UnityWebRequest must be created on the main thread, so every call is marshalled there.
    /// </summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public int TimeoutSeconds = 15;

        public Task<HttpResult> SendAsync(HttpRequestSpec spec)
        {
            var tcs = new TaskCompletionSource<HttpResult>();
            MainThread.Post(() =>
            {
                var req = new UnityWebRequest(spec.Url, spec.Method) { timeout = TimeoutSeconds };
                req.downloadHandler = new DownloadHandlerBuffer();
                if (spec.Body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(spec.Body));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                foreach (var h in spec.Headers) req.SetRequestHeader(h.Key, h.Value);
                var op = req.SendWebRequest();
                op.completed += _ =>
                {
                    var r = new HttpResult();
                    if (req.result == UnityWebRequest.Result.ConnectionError)
                    {
                        r.Status = 0;
                        r.TransportError = req.error;
                    }
                    else
                    {
                        r.Status = (int)req.responseCode;
                        r.Body = req.downloadHandler != null ? req.downloadHandler.text : null;
                    }
                    req.Dispose();
                    tcs.TrySetResult(r);
                };
            });
            return tcs.Task;
        }
    }
}

// LAN discovery that also tells searchers how many players the host has, so a host
// that ended up alone (e.g. a headset woken from sleep that took over while the real
// session carried on) can tell it should join the busy host instead of staying apart.
// The count rides in the URI's query string ("?players=N"), which the KCP transport
// ignores when connecting, so the discovery message format is unchanged.
using System;
using System.Net;
using Mirror;
using Mirror.Discovery;

public class SoundStageDiscovery : NetworkDiscovery {
  protected override ServerResponse ProcessRequest(ServerRequest request, IPEndPoint endpoint) {
    ServerResponse r = base.ProcessRequest(request, endpoint);
    try {
      UriBuilder b = new UriBuilder(r.uri) { Query = "players=" + NetworkServer.connections.Count };
      r.uri = b.Uri;
    } catch (Exception) { }
    return r;
  }

  // players on the host that sent this response (its own included), or -1 if it didn't say
  public static int Players(ServerResponse r) {
    if (r.uri == null) return -1;
    string q = r.uri.Query;
    int i = q.IndexOf("players=", StringComparison.Ordinal);
    if (i < 0) return -1;
    int n = 0, k = i + 8;
    bool any = false;
    while (k < q.Length && char.IsDigit(q[k])) { n = n * 10 + (q[k] - '0'); k++; any = true; }
    return any ? n : -1;
  }
}

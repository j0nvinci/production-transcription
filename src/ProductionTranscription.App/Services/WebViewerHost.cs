using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using ProductionTranscription.App.Models;

namespace ProductionTranscription.App.Services;

/// <summary>Optional HTTP viewer with configurable password (blank disables authentication).</summary>
public sealed class WebViewerHost : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public static string[] BindAddresses() =>
        new[] { "127.0.0.1", "0.0.0.0" }
        .Concat(System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString()))
        .Distinct().ToArray();

    public async Task StartAsync(AppSettings config, Func<object> snapshot, Func<bool, Task> captureControl, Func<string, Guid?, Task> messageControl)
    {
        await _lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync();
            if (!config.WebEnabled) return;
            if (config.WebPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(config.WebPort));
            if (!IPAddress.TryParse(config.WebBindAddress, out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Choose a valid IPv4 listener address.");

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
            builder.WebHost.UseKestrel(options => options.Listen(address, config.WebPort));
            var app = builder.Build();
            string secret = config.WebPassword ?? "";
            bool writable = config.WebReadWrite;
            static bool Authorized(HttpRequest req, string token)
            {
                if (token.Length == 0) return true;
                string supplied = req.Headers.Authorization.ToString();
                if (!supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
                byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                byte[] received = SHA256.HashData(Encoding.UTF8.GetBytes(supplied[7..]));
                return CryptographicOperations.FixedTimeEquals(expected, received);
            }
            app.MapGet("/", () => Results.Content(Page, "text/html; charset=utf-8"));
            app.MapGet("/api/snapshot", (HttpRequest req) =>
                Authorized(req, secret) ? Results.Json(new { writable, state = snapshot() }) : Results.Unauthorized());
            app.MapPost("/api/capture/{action}", async (string action, HttpRequest req) =>
            {
                if (!Authorized(req, secret)) return Results.Unauthorized();
                if (!writable) return Results.StatusCode(403);
                // Reject cross-origin browser writes even when password protection is disabled.
                if (req.Headers.Origin.Count > 0 && !string.Equals(req.Headers.Origin.ToString(),
                    $"{req.Scheme}://{req.Host}", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(403);
                if (req.Headers["X-Requested-With"] != "ProductionTranscription") return Results.StatusCode(403);
                if (action != "start" && action != "stop") return Results.NotFound();
                // Bearer authorization is sent as a custom header; browsers cannot issue it cross-origin without preflight.
                await captureControl(action == "start");
                return Results.Ok(new { accepted = true });
            });
            app.MapPost("/api/messages/{action}", async (string action, HttpRequest req) =>
            {
                if (!Authorized(req, secret)) return Results.Unauthorized();
                if (!writable) return Results.StatusCode(403);
                if (req.Headers.Origin.Count > 0 && !string.Equals(req.Headers.Origin.ToString(),
                    $"{req.Scheme}://{req.Host}", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(403);
                if (req.Headers["X-Requested-With"] != "ProductionTranscription") return Results.StatusCode(403);
                if (action == "clear") { await messageControl("clear", null); return Results.Ok(); }
                if (action != "delete" && action != "pin") return Results.NotFound();
                if (!Guid.TryParse(req.Query["id"], out var id)) return Results.BadRequest();
                await messageControl(action, id);
                return Results.Ok();
            });
            await app.StartAsync();
            _app = app;
        }
        finally { _lifecycle.Release(); }
    }
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycle.Release(); }
    }
    private async Task StopCoreAsync()
    {
        if (_app is null) return;
        var app = _app;
        _app = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await app.StopAsync(timeout.Token); } catch (OperationCanceledException) { }
        await app.DisposeAsync();
    }
    public async ValueTask DisposeAsync() { await StopAsync(); _lifecycle.Dispose(); }

    private const string Page = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Production Transcription — Remote</title><link rel="icon" type="image/svg+xml" href="data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2064%2064%22%3E%3Crect%20x%3D%222%22%20y%3D%222%22%20width%3D%2260%22%20height%3D%2260%22%20rx%3D%2214%22%20fill%3D%22%230b1521%22%20stroke%3D%22%2336e4df%22%20stroke-width%3D%223%22%2F%3E%3Crect%20x%3D%2225%22%20y%3D%2211%22%20width%3D%2214%22%20height%3D%2230%22%20rx%3D%227%22%20fill%3D%22%23eaf3f7%22%2F%3E%3Cpath%20d%3D%22M18%2029a14%2014%200%200%200%2028%200M32%2043v9M23%2052h18%22%20fill%3D%22none%22%20stroke%3D%22%2336e4df%22%20stroke-width%3D%223%22%20stroke-linecap%3D%22round%22%2F%3E%3Cpath%20d%3D%22M10%2026v12m5-18v24m34-24v24m5-18v12%22%20stroke%3D%22%2312bbd8%22%20stroke-width%3D%223%22%20stroke-linecap%3D%22round%22%2F%3E%3C%2Fsvg%3E"><style>
:root{color-scheme:dark;font:15px system-ui;background:#0b1019;color:#f2f5fa}
body{margin:0}header{background:#141d2a;padding:16px 22px;border-bottom:1px solid #293a50;display:flex;gap:14px;align-items:center;flex-wrap:wrap}
h1{font-size:20px;margin:0}small{color:#9aacc1}main{display:flex;height:calc(100vh - 90px)}
nav{width:225px;flex-shrink:0;background:#151b29;padding:16px;overflow:auto}
nav button{display:block;width:100%;text-align:left;margin-bottom:7px}
section{flex:1;overflow:auto;padding:18px;min-width:0}
button,input{background:#26384b;color:white;border:1px solid #41526a;border-radius:7px;padding:9px;cursor:pointer}
button.selected{border-color:#59e0c0;background:#244c50}
.message{border-left:4px solid #59e0c0;background:#151d2a;margin-bottom:9px;padding:12px;border-radius:5px;white-space:pre-wrap;overflow-wrap:anywhere}
.meta{font-size:12px;color:#a0b0c0;margin-bottom:6px}
#controls{display:none}#error{color:#ff8888}#login{padding:16px;background:#141d2a}
@media(max-width:650px){main{display:block;height:auto}nav{width:auto;display:flex;overflow-x:auto;gap:8px;padding:8px}nav button{width:auto;white-space:nowrap}section{padding:12px}}
</style></head><body>
<header><h1>Production Transcription</h1><small id="status">Connect to view transcript</small>
<div id="controls"><button id="start">Start capture</button> <button id="stop">Stop capture</button></div></header>
<div id="login"><label>Web password <input type="password" id="token" autocomplete="off"></label>
<button id="connect">Connect</button> <span id="error"></span></div>
<main><nav id="channels"></nav><section><h2 id="heading">All communications</h2><div id="feed"></div></section></main>
<script>
let token='',selected=null,busy=false,latest=null;
const $=id=>document.getElementById(id);
$('connect').onclick=()=>{token=$('token').value.trim();$('token').value='';poll()};
$('start').onclick=()=>control('start');$('stop').onclick=()=>control('stop');
async function control(action){try{let r=await fetch('/api/capture/'+action,{method:'POST',headers:{Authorization:'Bearer '+token,'X-Requested-With':'ProductionTranscription'}});if(!r.ok)throw Error('HTTP '+r.status);await poll()}catch(e){$('error').textContent=e.message}}
async function poll(){if(busy)return;busy=true;try{
let r=await fetch('/api/snapshot',{headers:{Authorization:'Bearer '+token},cache:'no-store'});
if(!r.ok)throw Error(r.status===401?'Incorrect web password':'HTTP '+r.status);
let result=await r.json();latest=result.state;$('login').style.display='none';$('error').textContent='';
$('controls').style.display=result.writable?'block':'none';
$('status').textContent=latest.capturing?'Capture active':'Capture stopped';
render();
}catch(e){$('error').textContent=e.message;$('login').style.display='block'}finally{busy=false}}
function render(){if(!latest)return;const nav=$('channels'),feed=$('feed');nav.replaceChildren();feed.replaceChildren();
const channels=[null,...latest.channels];if(selected!==null&&!channels.includes(selected))selected=null;
for(const name of channels){let b=document.createElement('button');b.textContent=name??'All communications';b.className=name===selected?'selected':'';b.onclick=()=>{selected=name;render()};nav.append(b)}
$('heading').textContent=selected??'All communications';
for(const m of latest.messages){if(selected!==null&&m.source!==selected)continue;
let div=document.createElement('div');div.className='message';div.style.borderLeftColor=m.color||'#59e0c0';
let meta=document.createElement('div');meta.className='meta';meta.textContent=m.source+' • '+m.time+(m.partial?' • Listening…':'');
let body=document.createElement('div');body.textContent=m.words;div.append(meta,body);feed.append(div)}
}
poll();
setInterval(poll,750);
</script></body></html>
""";
}

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
body{margin:0}header{background:#141d2a;padding:12px 16px;border-bottom:1px solid #293a50;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
h1{font-size:18px;margin:0 14px 0 0}small{color:#9aacc1}main{display:flex;height:calc(100vh - 105px)}
nav{width:185px;flex-shrink:0;background:#151b29;padding:12px;overflow:auto}
nav button{display:block;width:100%;text-align:left;margin-bottom:6px}
section{flex:1;overflow:auto;padding:14px;min-width:0}
button,input,select{background:#26384b;color:white;border:1px solid #41526a;border-radius:7px;padding:7px;cursor:pointer}
input{cursor:text}button.selected{border-color:#59e0c0;background:#244c50}
.message{border-left:4px solid #59e0c0;background:#151d2a;margin-bottom:9px;padding:12px;border-radius:5px;white-space:pre-wrap;overflow-wrap:anywhere}
.message.pinned{outline:1px solid #e7b87d}.meta{font-size:12px;color:#a0b0c0;margin-bottom:6px}
.actions{display:flex;gap:5px;margin-top:6px}.actions button{font-size:11px;padding:3px 7px}
#controls{display:none}#error{color:#ff8888}#login{padding:12px;background:#141d2a}
#toolbar{display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:12px}
#feed{font-size:16px}.compact .message{padding:6px;margin-bottom:4px}.spacious .message{padding:20px;margin-bottom:16px}
mark{background:#f7ca54;color:#101824;border-radius:3px}.hidden{display:none!important}
body.kiosk header,body.kiosk nav,body.kiosk #toolbar,body.kiosk #login{display:none!important}
body.kiosk main{height:100vh}body.kiosk section{padding:8px}
@media(max-width:650px){main{display:block;height:auto}nav{width:auto;display:flex;overflow-x:auto;gap:8px;padding:8px}nav button{width:auto;white-space:nowrap}section{padding:12px}}
</style></head><body>
<header><h1>Production Transcription</h1><small id="status">Connecting…</small>
<div id="controls"><button id="start">Start</button><button id="stop">Stop</button></div></header>
<div id="login"><label>Password (blank if disabled) <input type="password" id="token" autocomplete="off"></label>
<button id="connect">Connect</button> <span id="error"></span></div>
<main><nav id="channels"></nav><section>
<div id="toolbar">
<button id="smaller">A−</button><button id="larger">A+</button>
<button id="order">Newest last</button><button id="live">Jump to Live</button>
<input id="search" placeholder="Search transcript" aria-label="Search transcript" style="width:140px">
<select id="density"><option value="normal">Normal</option><option value="compact">Compact</option><option value="spacious">Spacious</option></select>
<select id="clock"><option value="hms24">24h seconds</option><option value="hm24">24h</option><option value="hms12">12h seconds</option><option value="hm12">12h</option><option value="none">Hide time</option></select>
<button id="pins">Pinned only</button><button id="kiosk">Kiosk</button>
<button id="clear" class="hidden">Clear all</button>
</div>
<h2 id="heading">All communications</h2><small id="health"></small><div id="feed"></div></section></main>
<script>
let token='',selected=null,busy=false,latest=null,writable=false,onlyPins=false,following=true,initialized=false;
const $=id=>document.getElementById(id);
const load=(k,fallback)=>{try{let v=localStorage.getItem('pt-'+k);return v===null?fallback:JSON.parse(v)}catch{return fallback}};
const save=(k,v)=>{try{localStorage.setItem('pt-'+k,JSON.stringify(v))}catch{}};
let prefs={font:load('font',null),newest:load('newest',null),density:load('density','normal'),clock:load('clock','hms24'),kiosk:false};
function applyPrefs(){
$('feed').style.fontSize=prefs.font+'px';$('order').textContent=prefs.newest?'Newest first':'Newest last';
$('density').value=prefs.density;$('clock').value=prefs.clock;
$('feed').className=prefs.density;document.body.classList.toggle('kiosk',prefs.kiosk);
}
$('connect').onclick=()=>{token=$('token').value;$('token').value='';poll()};
$('start').onclick=()=>control('start');$('stop').onclick=()=>control('stop');
$('smaller').onclick=()=>{prefs.font=Math.max(10,prefs.font-1);save('font',prefs.font);applyPrefs()};
$('larger').onclick=()=>{prefs.font=Math.min(36,prefs.font+1);save('font',prefs.font);applyPrefs()};
$('order').onclick=()=>{prefs.newest=!prefs.newest;save('newest',prefs.newest);applyPrefs();render()};
$('density').onchange=()=>{prefs.density=$('density').value;save('density',prefs.density);applyPrefs()};
$('clock').onchange=()=>{prefs.clock=$('clock').value;save('clock',prefs.clock);render()};
$('search').oninput=render;
$('pins').onclick=()=>{onlyPins=!onlyPins;$('pins').classList.toggle('selected',onlyPins);render()};
$('kiosk').onclick=()=>{prefs.kiosk=!prefs.kiosk;applyPrefs()};
$('live').onclick=jumpLive;
$('clear').onclick=()=>{if(confirm('Clear all transcript messages for everyone?'))messageAction('clear')};
$('feed').addEventListener('scroll',()=>{});
document.querySelector('section').addEventListener('scroll',()=>{const el=document.querySelector('section');following=prefs.newest?el.scrollTop<50:el.scrollTop+el.clientHeight>=el.scrollHeight-60});
document.addEventListener('keydown',e=>{
if(e.key==='Escape'&&prefs.kiosk){prefs.kiosk=false;applyPrefs()}
if(e.ctrlKey&&e.key.toLowerCase()==='f'){e.preventDefault();$('search').focus()}
if(e.ctrlKey&&(e.key==='+'||e.key==='=')){$('larger').click();e.preventDefault()}
if(e.ctrlKey&&e.key==='-'){$('smaller').click();e.preventDefault()}
});
async function request(url,method='GET'){return fetch(url,{method,headers:{Authorization:'Bearer '+token,'X-Requested-With':'ProductionTranscription'},cache:'no-store'})}
async function control(action){try{let r=await request('/api/capture/'+action,'POST');if(!r.ok)throw Error('HTTP '+r.status);await poll()}catch(e){$('error').textContent=e.message}}
async function messageAction(action,id){try{let r=await request('/api/messages/'+action+(id?'?id='+encodeURIComponent(id):''),'POST');if(!r.ok)throw Error('HTTP '+r.status);await poll()}catch(e){$('error').textContent=e.message}}
async function poll(){if(busy)return;busy=true;try{
let r=await request('/api/snapshot');if(!r.ok)throw Error(r.status===401?'Incorrect web password':'HTTP '+r.status);
let result=await r.json();latest=result.state;writable=result.writable;
if(!initialized){prefs.font=prefs.font??Math.max(10,Math.min(36,latest.settings.fontSize));prefs.newest=prefs.newest??latest.settings.newestFirst;initialized=true;applyPrefs()}
$('login').classList.add('hidden');$('error').textContent='';$('controls').style.display=writable?'block':'none';
$('clear').classList.toggle('hidden',!writable);
$('kiosk').classList.toggle('hidden',!latest.settings.kiosk);
$('pins').classList.toggle('hidden',!latest.settings.pinning);
$('status').textContent=latest.capturing?'Capture active':'Capture stopped';
$('health').textContent=latest.settings.health?'Viewer connected • '+latest.messages.length+' messages':'';
render();
}catch(e){$('error').textContent=e.message;$('login').classList.remove('hidden')}finally{busy=false}}
function jumpLive(){const el=document.querySelector('section');el.scrollTop=prefs.newest?0:el.scrollHeight;following=true}
function formatTime(t){
if(!latest.settings.timestamps||prefs.clock==='none')return '';
let parts=t.split(':');if(parts.length<3)return t;
let hh=+parts[0],suffix='';
if(prefs.clock.endsWith('12')){suffix=hh>=12?' PM':' AM';hh=hh%12||12}
return String(hh).padStart(2,'0')+':'+parts[1]+(prefs.clock.startsWith('hms')?':'+parts[2]:'')+suffix
}
function highlighted(container,text){
let terms=latest.settings.highlight?latest.settings.keywords.filter(x=>x&&x.trim()).sort((a,b)=>b.length-a.length):[];
if(!terms.length){container.textContent=text;return}
let lower=text.toLowerCase(),at=0;
while(at<text.length){let best=-1,term='';
for(const t of terms){let pos=lower.indexOf(t.toLowerCase(),at);if(pos>=0&&(best<0||pos<best||(pos===best&&t.length>term.length))){best=pos;term=t}}
if(best<0){container.append(document.createTextNode(text.slice(at)));break}
if(best>at)container.append(document.createTextNode(text.slice(at,best)));
let mark=document.createElement('mark');mark.textContent=text.slice(best,best+term.length);container.append(mark);at=best+term.length}
}
function render(){if(!latest)return;const nav=$('channels'),feed=$('feed'),el=document.querySelector('section');
const nearLive=following;nav.replaceChildren();feed.replaceChildren();
const channels=[null,...latest.channels];if(selected!==null&&!channels.includes(selected))selected=null;
for(const name of channels){let b=document.createElement('button');b.textContent=name??'All communications';b.className=name===selected?'selected':'';b.onclick=()=>{selected=name;render()};nav.append(b)}
$('heading').textContent=selected??'All communications';
let query=$('search').value.toLowerCase();let messages=prefs.newest?[...latest.messages].reverse():latest.messages;
for(const m of messages){if(selected!==null&&m.source!==selected)continue;if(onlyPins&&!m.pinned)continue;
if(query&&!(m.words+' '+m.source).toLowerCase().includes(query))continue;
let div=document.createElement('div');div.className='message'+(m.pinned?' pinned':'');div.style.borderLeftColor=m.color||'#59e0c0';
let meta=document.createElement('div');meta.className='meta';meta.textContent=m.source+(formatTime(m.time)?' • '+formatTime(m.time):'')+(m.partial?' • Listening…':'')+(m.pinned?' • PINNED':'');
let body=document.createElement('div');highlighted(body,m.words);div.append(meta,body);
if(writable){let actions=document.createElement('div');actions.className='actions';
if(latest.settings.pinning){let pin=document.createElement('button');pin.textContent=m.pinned?'Unpin':'Pin';pin.onclick=()=>messageAction('pin',m.id);actions.append(pin)}
let del=document.createElement('button');del.textContent='Delete';del.onclick=()=>messageAction('delete',m.id);actions.append(del);div.append(actions)}
feed.append(div)}
if(nearLive)requestAnimationFrame(jumpLive)
}
poll();setInterval(poll,750);
</script></body></html>
""";
}

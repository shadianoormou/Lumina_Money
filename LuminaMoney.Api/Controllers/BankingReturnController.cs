using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuminaMoney.Api.Controllers;

[ApiController, AllowAnonymous]
public sealed class BankingReturnController : ControllerBase
{
    [HttpGet("banking/truelayer/return")]
    public ContentResult Return() => Content("""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Return to Lumina Money</title><style>body{margin:0;background:#eaf4f2;color:#10242c;font:16px/1.5 system-ui;display:grid;min-height:100vh;place-items:center}.card{background:#fff;border:1px solid #dce7e8;border-radius:28px;padding:34px;max-width:430px;margin:20px;text-align:center;box-shadow:0 18px 50px #315a681c}h1{font-size:30px}p{color:#60777f}a{display:block;background:#10b981;color:#fff;text-decoration:none;font-weight:700;border-radius:17px;padding:16px;margin-top:24px}</style></head><body><div class="card"><div style="font-size:42px;color:#087b5b">✓</div><h1>Bank journey complete</h1><p>Return to Lumina Money. The app will securely check the connection status with TrueLayer—this page does not receive your bank credentials.</p><a id="return" href="luminamoney://bank/complete">Return to Lumina Money</a></div><script>setTimeout(()=>location.href=document.getElementById('return').href,350)</script></body></html>
        """, "text/html; charset=utf-8");
}

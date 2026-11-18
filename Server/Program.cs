using System.Threading.RateLimiting;
using ForeverSocial.Companion;
using ForeverSocial.Server;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=1024*1024);
builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.Encoder=Protocol.Json.Encoder;o.SerializerOptions.MaxDepth=12;});
builder.Services.AddSingleton(new CommunityStore(Environment.GetEnvironmentVariable("FS_DATA") ?? Path.Combine(AppContext.BaseDirectory,"data")));
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode=429;
    // Shared enrollment budget: do not trust arbitrary forwarded-IP headers.
    o.AddPolicy("enrollment",_=>RateLimitPartition.GetFixedWindowLimiter("enrollment",_=>new(){PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    o.AddPolicy("requests",context=>{
        var header=context.Request.Headers.Authorization.ToString();
        var account=header.StartsWith("Bearer ",StringComparison.Ordinal)?context.RequestServices.GetRequiredService<CommunityStore>().Authenticate(header[7..]):null;
        return RateLimitPartition.GetFixedWindowLimiter(account?.Id ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",_=>new(){PermitLimit=120,Window=TimeSpan.FromMinutes(1),QueueLimit=0});
    });
});
var app=builder.Build();app.UseRateLimiter();
app.Use(async(context,next)=>{
    context.Response.Headers.CacheControl="no-store";
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    try{await next(context);}
    catch(AccountAlreadyClaimedException){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error="Character already claimed; use its existing credential"});}catch(Exception e)when(e is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error="Invalid or conflicting community data"});}
    catch(UnauthorizedAccessException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Record does not belong to this account"});}
});
Account? User(HttpContext context,CommunityStore store){var header=context.Request.Headers.Authorization.ToString();return header.StartsWith("Bearer ",StringComparison.Ordinal)?store.Authenticate(header[7..]):null;}
app.MapGet("/health",()=>Results.Ok(new{service="Forever Social",version="1.2.0",protocol=1,enrollment="open"}));
app.MapPost("/v1/enroll",(Enrollment request,CommunityStore store)=>{
    // This is a consent claim, not proof of WoW identity. Existing claims are never reassigned.
    if(request.Format!=1 || !request.Enabled)return Results.BadRequest(new{error="Enable the companion export in the addon and update the companion app"});
    return Results.Ok(store.Enroll(request.Client,request.Actor,request.Community ?? ""));
}).RequireRateLimiting("enrollment");
app.MapPost("/v1/records",(HttpContext context,Upload request,CommunityStore store)=>{
    var user=User(context,store);return user==null?Results.Unauthorized():request.Records==null?Results.BadRequest():Results.Ok(new{accepted=store.Upload(user,request.Records,DateTimeOffset.UtcNow.ToUnixTimeSeconds())});
}).RequireRateLimiting("requests");
app.MapGet("/v1/snapshot",(HttpContext context,CommunityStore store)=>{
    var user=User(context,store);return user==null?Results.Unauthorized():Results.Ok(store.Snapshot(user,DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
}).RequireRateLimiting("requests");
app.MapDelete("/v1/account",(HttpContext context,CommunityStore store)=>{
    var user=User(context,store);if(user==null)return Results.Unauthorized();store.Delete(user);return Results.NoContent();
}).RequireRateLimiting("requests");
app.MapGet("/v1/account",(HttpContext context,CommunityStore store)=>{
    var user=User(context,store);return user==null?Results.Unauthorized():Results.Ok(new AccountInfo(user.Id,user.Client,user.Actor,user.Community));
}).RequireRateLimiting("requests");
app.Run();

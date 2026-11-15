using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ForeverSocial.Companion;
public sealed class SyncClient : IDisposable
{
    private readonly HttpClient http;
    public SyncClient(Uri endpoint,HttpMessageHandler? handler=null)
    {
        http=handler==null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}):new HttpClient(handler);
        http.BaseAddress=BridgeFiles.Endpoint(endpoint.AbsoluteUri);http.Timeout=TimeSpan.FromSeconds(30);http.DefaultRequestHeaders.UserAgent.ParseAdd("ForeverSocialCompanion/1.2.1");
    }
    public async Task<Credential> Enroll(Export export,string invite,CancellationToken cancel=default)
    {
        using var response=await http.PostAsJsonAsync("v1/enroll",new Enrollment(invite,export.Client,export.Actor,export.Community),Protocol.Json,cancel).ConfigureAwait(false);response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Credential>(Protocol.Json,cancel).ConfigureAwait(false) ?? throw new InvalidDataException("Invalid enrollment");
    }
    public async Task<Snapshot> Sync(Export export,string token,CancellationToken cancel=default,bool uploadRecords=true)
    {
        if(!export.Enabled)throw new InvalidDataException("Enable the companion inside the addon first");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);var accepted=0;
        foreach(var batch in (uploadRecords ? export.Records : []).Chunk(96)){
            using var response=await http.PostAsJsonAsync("v1/records",new Upload(batch.ToList()),Protocol.Json,cancel).ConfigureAwait(false);response.EnsureSuccessStatusCode();
            var answer=await response.Content.ReadFromJsonAsync<Dictionary<string,int>>(Protocol.Json,cancel).ConfigureAwait(false);accepted+=answer?.GetValueOrDefault("accepted") ?? 0;
        }
        using var snapshot=await http.GetAsync("v1/snapshot",HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false);snapshot.EnsureSuccessStatusCode();
        if(snapshot.Content.Headers.ContentLength>Protocol.MaxFileBytes)throw new InvalidDataException("Response too large");
        await using var stream=await snapshot.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);using var buffer=new MemoryStream();var chunk=new byte[8192];int length;
        while((length=await stream.ReadAsync(chunk,cancel).ConfigureAwait(false))>0){if(buffer.Length+length>Protocol.MaxFileBytes)throw new InvalidDataException("Response too large");buffer.Write(chunk,0,length);}
        var result=System.Text.Json.JsonSerializer.Deserialize<Snapshot>(buffer.ToArray(),Protocol.Json) ?? throw new InvalidDataException("Invalid snapshot");
        if(result.Format!=1 || result.Records is null || result.Records.Count>1500)throw new InvalidDataException("Unsupported snapshot");return result with{Accepted=accepted};
    }
    public void Dispose()=>http.Dispose();
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WalletSync.Api.Tests;

public class StreamEndpointTests
{
    [Fact]
    public async Task Stream_pushes_seq_after_a_commit()
    {
        await using var f = new WebApplicationFactory<Program>();
        var seed = RandomNumberGenerator.GetBytes(32);

        // Set up a second client with the SAME identity first, so the commit is ready
        // to fire immediately after the stream connection is established.
        var writer = f.CreateClient();
        var wtoken = await TestAuth.AuthenticateAsync(writer, seed);
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wtoken);

        var reader = f.CreateClient();
        var token = await TestAuth.AuthenticateAsync(reader, seed);
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // NOTE: With TestHost (WebApplicationFactory), Response.StartAsync() does NOT flush
        // headers to the client until the first body write/flush. Therefore:
        //   1. We must NOT pass the bounding CTS token to SendAsync — it would abort the server
        //      pipe via AbortRequest, causing "Flush was canceled on underlying PipeWriter".
        //   2. The stream will only return headers once the first SSE event is written.
        //   So: fire the commit concurrently with the SendAsync, then read lines with a bounded CTS.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Start the commit task concurrently — it will trigger an SSE event which
        // causes the server to write + flush, which resolves the TestHost _responseTcs
        // and unblocks SendAsync. Using CancellationToken.None here is intentional:
        // passing cts.Token to SendAsync would register AbortRequest on the server pipe,
        // causing "Flush was canceled" when cts fires.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/v1/bucket/stream");
        var sendTask = reader.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        // Give the stream a moment to register the subscription before the commit fires.
        await Task.Delay(100, cts.Token);

        var commit = await writer.PostAsJsonAsync("/v1/bucket/commit",
            new CommitRequest(new[] { new WriteOpDto("k", 0, "cse-v1", Convert.ToBase64String(new byte[] { 1 }), false) }), cts.Token);
        commit.EnsureSuccessStatusCode();

        // Now the commit has fired. The SSE endpoint will write "id: 1\ndata: 1\n\n" and flush,
        // which resolves the TestHost response task and unblocks SendAsync.
        using var resp = await sendTask.WaitAsync(cts.Token);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);

        await using var body = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var sr = new StreamReader(body);

        // Expect a "data: 1" line.
        var seen = false;
        string? line;
        while (!seen && (line = await sr.ReadLineAsync(cts.Token)) is not null)
            if (line == "data: 1") seen = true;
        Assert.True(seen);
    }
}

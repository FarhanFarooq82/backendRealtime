using A3ITranslator.API.Hubs;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Channels;

public class RealtimeTestClient
{
    private HubConnection _connection;

    public async Task StartAsync()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5000/audioHub")
            .Build();

        _connection.On<string, string, bool>("ReceiveTranscription", (text, lang, isFinal) =>
        {
            Console.WriteLine($"[Transcription] {text} ({lang}) [Final:{isFinal}]");
        });

        _connection.On<string>("ReceiveAudioChunk", (chunk) =>
        {
            Console.WriteLine($"[Audio] Received Chunk ({chunk.Length} bytes)");
        });

        await _connection.StartAsync();
        Console.WriteLine("Connected to Hub.");
    }

    public async Task SimulateConversationAsync(string wavFilePath)
    {
        var channel = Channel.CreateUnbounded<string>();
        
        // Start streaming
        _ = _connection.SendAsync("UploadAudioStream", channel.Reader);

        byte[] audioBytes = await File.ReadAllBytesAsync(wavFilePath);
        int chunkSize = 3200; // 100ms
        
        for(int i = 0; i < audioBytes.Length; i += chunkSize)
        {
            int size = Math.Min(chunkSize, audioBytes.Length - i);
            byte[] chunk = new byte[size];
            Array.Copy(audioBytes, i, chunk, 0, size);
            
            await channel.Writer.WriteAsync(Convert.ToBase64String(chunk));
            await Task.Delay(100); // Simulate real-time
        }

        channel.Writer.Complete();
        Console.WriteLine("Streaming Complete. Sending Commit.");

        await _connection.InvokeAsync("CommitUtterance");
    }
}

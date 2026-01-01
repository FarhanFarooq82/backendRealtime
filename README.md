# A3ITranslator Realtime Backend

A real-time audio translation backend built with ASP.NET Core 8, SignalR, and advanced speech processing capabilities.

## Features

### 🎵 **Real-time Audio Processing**
- **WebM Direct Streaming**: Native support for WebM/Opus audio format
- **Enhanced Payload Structure**: Rich metadata including sessionId, chunkId, sequence, timestamp, mimeType, checksum, utteranceId, size
- **Multi-format Support**: WebM, PCM, WAV with automatic format detection
- **Live Audio Streaming**: SignalR-based real-time audio transmission

### 🗣️ **Speech-to-Text Integration**
- **Google Cloud Speech-to-Text V2**: Direct WebM/Opus streaming support
- **Azure Speech Services**: PCM format support with advanced configuration
- **Streaming STT**: Real-time transcription with intermediate results
- **Multi-language Support**: Automatic language detection and processing

### 🔄 **SignalR Hub Architecture**
- **AudioConversationHub**: Unified audio chunk processing
- **Enhanced Payload Support**: JsonElement parsing for flexible data structures  
- **Real-time Notifications**: Live transcription and translation updates
- **Connection Management**: Robust session handling and reconnection logic

## Technology Stack

- **Framework**: ASP.NET Core 8
- **Real-time Communication**: SignalR
- **Speech Services**: Azure Cognitive Services, Google Cloud Speech-to-Text V2
- **Audio Processing**: FFmpeg, custom audio converters
- **Language**: C# 12 with nullable reference types
- **Testing**: xUnit with integration test support

## Getting Started

### Prerequisites

- .NET 8 SDK
- Azure Speech Services API key (optional)
- Google Cloud Speech API credentials (optional)
- FFmpeg (for audio conversion debugging)

### Running the Application

```bash
# Build the solution
dotnet build A3ITranslator.sln

# Run the API
dotnet run --project src/A3ITranslator.API

# Run tests
dotnet test
```

The API will be available at `https://localhost:7242` with SignalR hub at `/audioConversationHub`.

## License

This project is part of the A3ITranslator suite - an advanced real-time audio translation system.

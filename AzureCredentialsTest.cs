using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Options;
using A3ITranslator.Infrastructure.Configuration;

Console.WriteLine("🔵 Testing Azure Speech SDK credentials...");

var options = new ServiceOptions
{
    Azure = new AzureOptions
    {
        SpeechKey = "YOUR_AZURE_SPEECH_KEY_HERE",
        SpeechRegion = "northeurope",
        SpeechEndpoint = "https://northeurope.api.cognitive.microsoft.com/"
    }
};

try 
{
    Console.WriteLine($"🔑 Using Speech Key: {options.Azure.SpeechKey[..10]}... (truncated)");
    Console.WriteLine($"🌍 Using Region: {options.Azure.SpeechRegion}");
    
    var speechConfig = SpeechConfig.FromSubscription(options.Azure.SpeechKey, options.Azure.SpeechRegion);
    speechConfig.SpeechRecognitionLanguage = "en-US";
    
    Console.WriteLine("✅ Azure SpeechConfig created successfully!");
    Console.WriteLine($"   Region: {speechConfig.Region}");
    Console.WriteLine($"   Language: {speechConfig.SpeechRecognitionLanguage}");
    
    // Test creating a recognizer
    using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
    using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
    
    Console.WriteLine("✅ Azure SpeechRecognizer created successfully!");
    Console.WriteLine("🎯 Azure credentials are VALID and working!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Azure credentials test FAILED: {ex.Message}");
    Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
}

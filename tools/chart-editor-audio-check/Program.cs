using DynamiteUniverse.ChartEditor.Audio;

try
{
    EditorAudioSelfTest.Run();
    Console.WriteLine("AUDIO CHECK PASS: registry, WAV decoder and writer probe adapter");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("AUDIO CHECK FAIL: " + exception);
    return 1;
}

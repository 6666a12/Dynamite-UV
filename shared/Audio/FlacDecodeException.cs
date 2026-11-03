namespace DynamiteUniverse.Shared.Audio;

/// <summary>FLAC 数据非法、使用了不支持的特性或超出解码预算时抛出。</summary>
public sealed class FlacDecodeException : Exception
{
    public FlacDecodeException(string message) : base(message)
    {
    }

    public FlacDecodeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

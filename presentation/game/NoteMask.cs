using System;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>
/// 音符材质的九宫格遮罩（512x64；R=体面明暗，G=发光核心，B=边缘，A=剪影）。
/// PNG 字节直接内嵌并解码为 ImageTexture，绕开导入管线对 alpha 边界、mipmap 和
/// sRGB 的处理，保证与 design/note-light-v1 的对照实现使用同一份像素数据。
/// </summary>
internal static class NoteMask
{
	private const string PngBase64 =
	"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";

	private static Texture2D? _texture;

	public static Texture2D Texture => _texture ??= Decode();

	private static Texture2D Decode()
	{
		var image = new Image();
		var error = image.LoadPngFromBuffer(Convert.FromBase64String(PngBase64));
		if (error != Error.Ok)
		{
			GD.PushError("NoteMask: PNG decode failed (" + error + ")");
			return ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
		}
		return ImageTexture.CreateFromImage(image);
	}
}

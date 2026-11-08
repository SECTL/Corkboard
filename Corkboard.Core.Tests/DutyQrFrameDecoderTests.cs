using System;
using System.Collections;
using Corkboard.Core.DutyRoster.Import;
using QRCoder;

namespace Corkboard.Core.Tests;

/// <summary>
///     `QrFrameDecoder` 的单测：用 QRCoder 的模块矩阵自己铺像素，不依赖图片库与相机。
///     覆盖相机常见的三种排列（RGB / BGR / BGRA）与画面旋转（手机斜着扫）。
/// </summary>
public sealed class DutyQrFrameDecoderTests
{
    private const string SampleText = "SRQR1M|0123456789abcdef0123456789abcdef|eyJ4IjoxfQ==|123|4|ABCDEF";

    [Fact]
    public void DecodesRgb24Frame()
    {
        var pixels = RenderQr(SampleText, out var width, out var height);

        Assert.True(QrFrameDecoder.TryDecode(pixels, width, height, QrPixelFormat.Rgb24, out var text));
        Assert.Equal(SampleText, text);
    }

    [Fact]
    public void DecodesBgr24Frame()
    {
        var pixels = RenderQr(SampleText, out var width, out var height, QrPixelFormat.Bgr24);

        Assert.True(QrFrameDecoder.TryDecode(pixels, width, height, QrPixelFormat.Bgr24, out var text));
        Assert.Equal(SampleText, text);
    }

    [Fact]
    public void DecodesBgra32Frame()
    {
        var pixels = RenderQr(SampleText, out var width, out var height, QrPixelFormat.Bgra32);

        Assert.True(QrFrameDecoder.TryDecode(pixels, width, height, QrPixelFormat.Bgra32, out var text));
        Assert.Equal(SampleText, text);
    }

    [Fact]
    public void DecodesRotatedFrame()
    {
        // 相机装反、手机横着扫都会把画面转 90°；ZXing 的 AutoRotate 负责兜住。
        var pixels = RenderQr(SampleText, out var width, out var height);
        var rotated = Rotate90(pixels, width, height, QrPixelFormat.Rgb24);

        Assert.True(QrFrameDecoder.TryDecode(rotated, height, width, QrPixelFormat.Rgb24, out var text));
        Assert.Equal(SampleText, text);
    }

    [Fact]
    public void DecodesOfflineQrProtocolFrameFromExporter()
    {
        // 与导出端闭环：真实帧文本 → QRCoder → 像素 → QrFrameDecoder，文本必须原样回来。
        var frames = OfflineQrProtocol.CreateFrames(
            new RosterTransferDocument(
                1,
                RosterTransferKind.DutyRoster,
                "值日名单.secrandom-roster.json",
                [new RosterTransferRow(true, "01", "张三", "男", "第一组", "一组")]),
            "0123456789abcdef0123456789abcdef");
        Assert.NotEmpty(frames);

        foreach (var frame in frames)
        {
            var pixels = RenderQr(frame, out var width, out var height);

            Assert.True(QrFrameDecoder.TryDecode(pixels, width, height, QrPixelFormat.Rgb24, out var text),
                $"帧解码失败：{frame[..Math.Min(24, frame.Length)]}…");
            Assert.Equal(frame, text);
        }
    }

    [Fact]
    public void RejectsNoiseAndBadArguments()
    {
        var noise = new byte[320 * 240 * 3];
        var random = new Random(20261108);
        random.NextBytes(noise);

        Assert.False(QrFrameDecoder.TryDecode(noise, 320, 240, QrPixelFormat.Rgb24, out _));
        Assert.False(QrFrameDecoder.TryDecode(null, 320, 240, QrPixelFormat.Rgb24, out _));
        Assert.False(QrFrameDecoder.TryDecode([], 0, 0, QrPixelFormat.Rgb24, out _));
        Assert.False(QrFrameDecoder.TryDecode([1, 2, 3], 320, 240, QrPixelFormat.Rgb24, out _));
    }

    // ---- 画布 ----

    /// <summary>把 QRCoder 的模块矩阵铺成像素：每模块 scale×scale，四周静区 4 模块。</summary>
    private static byte[] RenderQr(
        string text,
        out int width,
        out int height,
        QrPixelFormat format = QrPixelFormat.Rgb24,
        int scale = 6,
        int quietModules = 4)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        var matrix = data.ModuleMatrix;
        var modules = matrix.Count;
        width = height = (modules + quietModules * 2) * scale;

        var bytesPerPixel = format == QrPixelFormat.Gray8 ? 1
            : format is QrPixelFormat.Rgb24 or QrPixelFormat.Bgr24 ? 3
            : 4;
        var pixels = new byte[width * height * bytesPerPixel];
        Array.Fill(pixels, (byte)0xFF);

        for (var row = 0; row < modules; row++)
        {
            var bits = matrix[row];
            for (var column = 0; column < modules; column++)
            {
                if (!bits[column])
                    continue;

                var startX = (column + quietModules) * scale;
                var startY = (row + quietModules) * scale;
                for (var dy = 0; dy < scale; dy++)
                {
                    var baseIndex = ((startY + dy) * width + startX) * bytesPerPixel;
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var offset = baseIndex + dx * bytesPerPixel;
                        // 只有 RGB 三通道压暗；32 位格式必须保留 Alpha=255，
                        // 透明像素会被 ZXing 当成无效色块。
                        pixels[offset] = 0;
                        pixels[offset + 1] = 0;
                        pixels[offset + 2] = 0;
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>顺时针转 90°：宽高互换，像素排列保持一致。</summary>
    private static byte[] Rotate90(byte[] source, int width, int height, QrPixelFormat format)
    {
        var bytesPerPixel = format == QrPixelFormat.Gray8 ? 1
            : format is QrPixelFormat.Rgb24 or QrPixelFormat.Bgr24 ? 3
            : 4;
        var target = new byte[source.Length];
        var targetWidth = height;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = (y * width + x) * bytesPerPixel;
                var targetX = height - 1 - y;
                var targetY = x;
                var targetOffset = (targetY * targetWidth + targetX) * bytesPerPixel;
                for (var channel = 0; channel < bytesPerPixel; channel++)
                    target[targetOffset + channel] = source[sourceOffset + channel];
            }
        }

        return target;
    }
}
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SkiaSharp;

public class App
{
    private HttpClient client = new HttpClient();
    private static int MAX_SIZE = 192;

    public async Task Run()
    {
        var shapes = await GetShapesAsync();
        if (shapes == null)
        {
            Console.WriteLine("Please check if there is a file named shpes.txt in root dir");
            return;
        }
        if (shapes.Length == 0)
        {
            Console.WriteLine("No shapes found");
            return;
        }

        Console.WriteLine($"about to process {shapes.Length} shapes");

        foreach (var shape in shapes)
        {
            await ProcessAsync(shape);
        }
    }

    private async Task<string[]> GetShapesAsync()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var targetFile = currentDir + "/shapes.txt";
        try
        {
            return await File.ReadAllLinesAsync(targetFile);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        return null;
    }

    private FileStream CreateFileById(string id)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var targetFile = $"{currentDir}/{id}.png";
        return File.Create(targetFile);
    }

    private SKImageInfo GetTargetSize(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var ratio = width * 1f / height;
        if (width > MAX_SIZE || height > MAX_SIZE)
        {
            if (width > height)
            {
                width = MAX_SIZE;
                height = (int)(width / ratio);
            }
            else
            {
                height = MAX_SIZE;
                width = (int)(height * ratio);
            }
        }
        return new SKImageInfo(width, height);
    }

    private async Task<String> ProcessAsync(string url)
    {
        Console.WriteLine($"processing: {url}");

        try
        {
            using (var result = await client.GetAsync(url))
            {
                result.EnsureSuccessStatusCode();
                using (var stream = await result.Content.ReadAsStreamAsync())
                using (var inputStream = new SKManagedStream(stream))
                using (var original = SKBitmap.Decode(inputStream))
                using (var resized = original.Resize(GetTargetSize(original), SKBitmapResizeMethod.Lanczos3))
                {
                    if (resized == null) return null;
                    using (var image = SKImage.FromBitmap(resized))
                    {
                        using (var output = CreateFileById(url.GetHashCode().ToString()))
                        {
                            image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(output);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"failed to process: {url}, error: {e.Message}");
        }

        return null;
    }
}
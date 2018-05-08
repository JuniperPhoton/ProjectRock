using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

public class App
{
    private static int MAX_SIZE = 192;

    private Queue<string> _urlsToDownload = new Queue<string>();
    private Queue<string> _filesToProcess = new Queue<string>();
    private List<string> _failsList = new List<string>();

    private HttpClient _client = new HttpClient();

    public async Task Run(string pathToRead)
    {
        var shapes = await GetShapesAsync(pathToRead);
        if (shapes == null)
        {
            if (pathToRead != null)
            {
                Console.WriteLine($"Failed to read from path: {pathToRead}");
            }
            else
            {
                Console.WriteLine("Please put a file named \"shapes.txt\" in root dir with the dll.");
            }
            return;
        }
        if (shapes.Length == 0)
        {
            Console.WriteLine("No shapes found");
            return;
        }

        var filtered = shapes.Where(s => s != null);

        Console.WriteLine($"about to process {filtered.Count()} shapes");

        foreach (string url in filtered)
        {
            _urlsToDownload.Enqueue(url);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        var t0 = DownloadAllImagesAsync();
        var t1 = ProcessAllImagesAsync();
        var list = new List<Task>();
        list.Add(t0);
        list.Add(t1);
        await Task.WhenAll(list);

        watch.Stop();

        Console.WriteLine($"===completed with {_failsList.Count} errors and elasped time: {watch.ElapsedMilliseconds / 1000f} seconds===");

        if (_failsList.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "log.txt");
            File.WriteAllLines(log, _failsList);
        }
    }

    private async Task<string[]> GetShapesAsync(string pathToRead)
    {
        var targetFile = pathToRead == null ? Directory.GetCurrentDirectory() + "/shapes.txt" : pathToRead;
        try
        {
            return await File.ReadAllLinesAsync(targetFile);
        }
        catch (Exception)
        {
            // ignore
        }
        return null;
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

    private async Task DownloadAllImagesAsync()
    {
        while (_urlsToDownload.Count > 0)
        {
            string url = _urlsToDownload.Dequeue();
            string file = null;

            try
            {
                Console.WriteLine($"about to download: {url}");
                using (var result = await _client.GetAsync(url))
                {
                    result.EnsureSuccessStatusCode();
                    using (var stream = await result.Content.ReadAsStreamAsync())
                    {
                        file = FileUtils.CreateFileToRoot("original", $"{DateTime.Now.Ticks.ToString()}.png");
                        using (var output = File.Open(file, FileMode.Create))
                        {
                            byte[] buffer = new byte[32 * 1024];
                            int read;

                            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, read);
                            }
                        }
                    }
                }

                _filesToProcess.Enqueue(file);
            }
            catch (Exception e)
            {
                Console.WriteLine($"failed to download: {url}, error: {e.Message}");
                _failsList.Add(url);
            }
        }
    }

    private async Task ProcessAllImagesAsync()
    {
        await Task.Run(() =>
        {
            while (_filesToProcess.Count > 0 || _urlsToDownload.Count > 0)
            {
                if (_filesToProcess.Count == 0) continue;

                string path = _filesToProcess.Dequeue();
                try
                {
                    Console.WriteLine($"about to process: {path}");

                    using (var stream = File.OpenRead(path))
                    using (var inputStream = new SKManagedStream(stream))
                    using (var original = SKBitmap.Decode(inputStream))
                    using (var resized = original.Resize(GetTargetSize(original), SKBitmapResizeMethod.Lanczos3))
                    {
                        if (resized == null) return;
                        using (var image = SKImage.FromBitmap(resized))
                        {
                            using (var output = File.Open(FileUtils.CreateFileToRoot("resized", $"{DateTime.Now.Ticks.ToString()}.png"), FileMode.OpenOrCreate))
                            {
                                image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(output);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"failed to process: {path}, error: {e.Message}");
                }
            }
        });
    }
}
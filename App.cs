using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using System.Threading;

public class App
{
    private static int MAX_SIZE = 192;

    private Queue<Shape> _pendingToDownload = new Queue<Shape>();
    private Queue<Shape> _pendingToProcess = new Queue<Shape>();
    private HashSet<Shape> _failsSet = new HashSet<Shape>();
    private HashSet<Shape> _succeededSet = new HashSet<Shape>();

    private HttpClient _client = new HttpClient();

    public async Task Run(string pathToRead)
    {
        _client.Timeout = TimeSpan.FromMilliseconds(20000);

        await GetShapesAsync(pathToRead);
        if (_pendingToDownload.Count == 0)
        {
            Console.WriteLine("No shapes found");
            return;
        }

        Console.WriteLine($"about to process {_pendingToDownload.Count} shapes");

        var watch = System.Diagnostics.Stopwatch.StartNew();

        var t0 = DownloadAllImagesAsync();
        var t1 = ProcessAllImagesAsync();
        var list = new List<Task>();
        list.Add(t0);
        list.Add(t1);
        await Task.WhenAll(list);

        watch.Stop();

        Console.WriteLine($"===completed with {_failsSet.Count} errors and elasped time: {watch.ElapsedMilliseconds / 1000f} seconds===");

        if (_failsSet.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "error.txt");
            var fails = new List<string>();
            foreach (var s in _failsSet)
            {
                fails.Add(s.ToString());
            }
            File.WriteAllLines(log, fails);
        }

        if (_succeededSet.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "succeeded.txt");
            var successes = new List<string>();
            foreach (var s in _succeededSet)
            {
                successes.Add(s.CreateLog());
            }
            File.WriteAllLines(log, successes);
        }
    }

    private async Task GetShapesAsync(string pathToRead)
    {
        var targetFile = pathToRead == null ? Directory.GetCurrentDirectory() + "/shapes.txt" : pathToRead;
        try
        {
            var lines = await File.ReadAllLinesAsync(targetFile);
            foreach (var line in lines)
            {
                var split = line.Split(',');
                if (split.Count() == 2)
                {
                    var shape = new Shape();
                    shape.Id = split[0].Replace("\"", "");
                    shape.Url = split[1].Replace("\"", "");
                    _pendingToDownload.Enqueue(shape);
                }
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    private SKImageInfo GetTargetInfo(SKBitmap bitmap)
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
        var info = bitmap.Info;
        info.Width = width;
        info.Height = height;
        info.ColorType = SKImageInfo.PlatformColorType;
        return info;
    }

    private async Task DownloadAllImagesAsync()
    {
        while (_pendingToDownload.Count > 0)
        {
            Shape shape = _pendingToDownload.Dequeue();
            var url = shape.Url;
            var id = shape.Id;
            string file = null;

            try
            {
                file = FileUtils.CreateFileToRoot("original", $"{id}.png");

                if (!File.Exists(file))
                {
                    Console.WriteLine($"about to download: {url}");

                    using (var result = await _client.GetAsync(url))
                    {
                        result.EnsureSuccessStatusCode();
                        using (var stream = await result.Content.ReadAsStreamAsync())
                        {
                            using (var memoryStream = new MemoryStream())
                            {
                                stream.CopyTo(memoryStream);
                                var array = memoryStream.ToArray();
                                File.WriteAllBytes(file, array);
                            }
                        }
                    }
                }

                shape.OutputPath = file;
                _pendingToProcess.Enqueue(shape);
            }
            catch (Exception e)
            {
                Console.WriteLine($"failed to download: {url}, error: {e.Message}");
                _failsSet.Add(shape);
            }
        }
    }

    private async Task ProcessAllImagesAsync()
    {
        var wait = new SpinWait();

        await Task.Run(() =>
           {
               while (_pendingToProcess.Count > 0 || _pendingToDownload.Count > 0)
               {
                   if (_pendingToProcess.Count == 0)
                   {
                       wait.SpinOnce();
                       continue;
                   }

                   Shape shape = _pendingToProcess.Dequeue();
                   var path = shape.OutputPath;
                   var id = shape.Id;
                   try
                   {
                       Console.WriteLine($"about to process: {path}");

                       using (var stream = File.OpenRead(path))
                       using (var inputStream = new SKManagedStream(stream))
                       {
                           var original = SKBitmap.Decode(inputStream);
                           var info = GetTargetInfo(original);

                           SKBitmap toResize;
                           if (original.ColorType != SKImageInfo.PlatformColorType)
                           {
                               toResize = new SKBitmap(info);
                               original.CopyTo(toResize, SKImageInfo.PlatformColorType);
                           }
                           else
                           {
                               toResize = original;
                           }

                           using (var resized = toResize.Resize(info, SKBitmapResizeMethod.Box))
                           {
                               using (var image = SKImage.FromBitmap(resized))
                               {
                                   using (var output = File.Open(FileUtils.CreateFileToRoot("resized", $"{id}.png"), FileMode.OpenOrCreate))
                                   {
                                       image.Encode(SKEncodedImageFormat.Png, 90).SaveTo(output);
                                   }
                               }
                           }
                           toResize.Dispose();
                           try
                           {
                               original.Dispose();
                           }
                           catch (Exception)
                           {
                               // ignore
                           }
                       }

                       _succeededSet.Add(shape);
                   }
                   catch (Exception e)
                   {
                       Console.WriteLine($"failed to process: {path}, error: {e.Message}");
                       _failsSet.Add(shape);
                   }
               }
           });
    }
}
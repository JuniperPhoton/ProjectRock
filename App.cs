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
    private static int TIMEOUT_MILLIS = 20000;

    private Queue<Shape> _pendingToDownload = new Queue<Shape>();
    private Queue<Shape> _pendingToProcess = new Queue<Shape>();
    private HashSet<Shape> _failsSet = new HashSet<Shape>();
    private HashSet<Shape> _succeededSet = new HashSet<Shape>();

    private HttpClient _client = new HttpClient();
    private SKPaint _skPaint = new SKPaint();

    public async Task Run(string pathToRead)
    {
        _client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MILLIS);
        _skPaint.Color = new SKColor(0, 0, 0, 255);

        await GetShapesAsync(pathToRead);
        if (_pendingToDownload.Count == 0)
        {
            Console.WriteLine("No shapes found");
            return;
        }

        Console.WriteLine($"about to process {_pendingToDownload.Count} shapes");

        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Task.WhenAll(new Task[] { DownloadAllImagesAsync(), ProcessAllImagesAsync() });

        watch.Stop();

        Console.WriteLine($"===completed with {_failsSet.Count} errors and elasped time: {watch.ElapsedMilliseconds / 1000f} seconds===");

        HandleFails();
        HandleSuccesses();
    }

    private void HandleFails()
    {
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
    }

    private void HandleSuccesses()
    {
        if (_succeededSet.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "succeeded.txt");
            var successes = new List<string>();
            foreach (var s in _succeededSet)
            {
                successes.Add(s.CreateUpdateStatement());
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
                var shape = Shape.CreateFromLine(line);
                if (shape != null)
                {
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
        var (width, height) = CalculateResized(bitmap.Width, bitmap.Height);
        var info = bitmap.Info;
        info.Width = width;
        info.Height = height;
        info.ColorType = SKImageInfo.PlatformColorType;
        return info;
    }

    private Tuple<int, int> CalculateResized(int w, int h)
    {
        var width = w;
        var height = h;
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
        return new Tuple<int, int>(width, height);
    }

    private async Task DownloadAllImagesAsync()
    {
        var total = _pendingToDownload.Count;
        var handling = 0f;
        while (_pendingToDownload.Count > 0)
        {
            Shape shape = _pendingToDownload.Dequeue();
            var url = shape.Url;
            var ext = shape.Extension;
            var id = shape.Id;
            string file = null;

            try
            {
                file = FileUtils.CreateFileToRoot("original", $"{id}.{ext}");
                handling++;

                //Console.Clear();
                //Console.WriteLine($"==download progress=={((handling / total) * 100):F}%");

                if (!File.Exists(file))
                {
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
               var total = _pendingToDownload.Count;
               var handling = 0d;
               while (_pendingToProcess.Count > 0 || _pendingToDownload.Count > 0)
               {
                   if (_pendingToProcess.Count == 0)
                   {
                       wait.SpinOnce();
                       continue;
                   }

                   handling++;

                   Console.Clear();
                   Console.WriteLine($"==process progress=={((handling / total) * 100):F}%");

                   Shape shape = _pendingToProcess.Dequeue();
                   var path = shape.OutputPath;
                   try
                   {
                       if (shape.IsRaster)
                       {
                           SaveRaster(shape);
                       }
                       else
                       {
                           SaveVector(shape);
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

    private void SaveVector(Shape shape)
    {
        var svg = new SkiaSharp.Extended.Svg.SKSvg();

        svg.Load(shape.OutputPath);

        var svgRect = svg.Picture.CullRect;
        var (w, h) = CalculateResized((int)svgRect.Width, (int)svgRect.Height);
        float svgMax = Math.Max(w, h);

        float scale = w / svgRect.Width;
        var matrix = SKMatrix.MakeScale(scale, scale);

        var target = new SKBitmap(w, h,
                     SKImageInfo.PlatformColorType, SKAlphaType.Premul);

        using (target)
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear();
            canvas.DrawPicture(svg.Picture, ref matrix, _skPaint);
            SaveToFile(shape.Id, target);
        }
    }

    private void SaveRaster(Shape shape)
    {
        using (var stream = File.OpenRead(shape.OutputPath))
        using (var inputStream = new SKManagedStream(stream))
        {
            var original = SKBitmap.Decode(inputStream);
            var info = GetTargetInfo(original);

            SKBitmap target;
            if (original.ColorType != SKImageInfo.PlatformColorType)
            {
                target = new SKBitmap(info);
                original.CopyTo(target, SKImageInfo.PlatformColorType);
            }
            else
            {
                target = original;
            }

            using (var resized = target.Resize(info, SKBitmapResizeMethod.Box))
            {
                SaveToFile(shape.Id, resized);
            }

            try
            {
                target.Dispose();
                original.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void SaveToFile(string id, SKBitmap bitmap)
    {
        using (var image = SKImage.FromBitmap(bitmap))
        {
            using (var output = File.Open(FileUtils.CreateFileToRoot("resized", $"{id}.png"), FileMode.OpenOrCreate))
            {
                image.Encode(SKEncodedImageFormat.Png, 90).SaveTo(output);
            }
        }
    }
}
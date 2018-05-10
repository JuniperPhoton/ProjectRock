using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using System.Threading;
using System.Collections.Concurrent;

public class App
{
    private static int MAX_SIZE = 192;
    private static int TIMEOUT_MILLIS = 20000;

    private ConcurrentQueue<Shape> _pendingToDownload = new ConcurrentQueue<Shape>();
    private ConcurrentQueue<Shape> _pendingToProcess = new ConcurrentQueue<Shape>();
    private ConcurrentBag<Shape> _failureList = new ConcurrentBag<Shape>();
    private ConcurrentBag<Shape> _successList = new ConcurrentBag<Shape>();

    private HttpClient _client = new HttpClient();
    private SKPaint _skPaint = new SKPaint();

    private int _totalCount;

    public async Task RunAsync(string pathToRead)
    {
        _client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MILLIS);
        _skPaint.Color = new SKColor(118, 118, 118, 255);

        await GetShapesAsync(pathToRead);
        if (_pendingToDownload.Count == 0)
        {
            Console.WriteLine("No shapes found");
            return;
        }

        _totalCount = _pendingToDownload.Count;

        Console.WriteLine($"about to process {_pendingToDownload.Count} shapes");

        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Task.WhenAll(new Task[] { DownloadAsync(), ProcessAsync() });

        watch.Stop();

        Console.WriteLine($"===completed with {_failureList.Count} errors and elapsed time: {watch.ElapsedMilliseconds / 1000f} seconds===");

        _client.Dispose();
        _skPaint.Dispose();

        HandleFailure();
        HandleSuccess();
    }

    private void HandleFailure()
    {
        if (_failureList.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "error.txt");
            var fails = new List<string>();
            foreach (var s in _failureList)
            {
                fails.Add(s.ToString());
            }
            File.WriteAllLines(log, fails);
        }
    }

    private void HandleSuccess()
    {
        if (_successList.Count > 0)
        {
            var log = FileUtils.CreateFileToRoot("", "succeeded.txt");
            var successes = new List<string>();
            foreach (var s in _successList)
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
        return new Tuple<int, int>(width, height);
    }

    private async Task DownloadAsync()
    {
        var handling = 0f;
        while (_pendingToDownload.Count > 0)
        {
            if (!_pendingToDownload.TryDequeue(out var shape)) continue;

            var url = shape.Url;
            var ext = shape.Extension;
            var id = shape.Id;
            string file = null;

            try
            {
                file = FileUtils.CreateFileToRoot("original", $"{id}.{ext}");
                handling++;

                //Console.Clear();
                //Console.WriteLine($"==download progress=={((handling / _totalCount) * 100):F}%");

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
                _failureList.Add(shape);
            }
        }
    }

    private async Task ProcessAsync()
    {
        var wait = new SpinWait();

        await Task.Run(() =>
           {
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
                   Console.WriteLine($"==process progress=={((handling / _totalCount) * 100):F}%");

                   if (!_pendingToProcess.TryDequeue(out var shape)) continue;

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

                       _successList.Add(shape);
                   }
                   catch (Exception e)
                   {
                       Console.WriteLine($"failed to process: {path}, error: {e.Message}");
                       _failureList.Add(shape);
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
        var matrixS = SKMatrix.MakeScale(scale, scale);
        var matrixT = SKMatrix.MakeTranslation((MAX_SIZE - w) / 2, (MAX_SIZE - h) / 2);
        var matrix = SKMatrix.MakeIdentity();

        SKMatrix.Concat(ref matrix, matrixT, matrixS);

        var target = new SKBitmap(MAX_SIZE, MAX_SIZE,
                     SKImageInfo.PlatformColorType, SKAlphaType.Premul);

        using (target)
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear();
            canvas.DrawPicture(svg.Picture, ref matrix, _skPaint);
            SaveToFile(shape.Id, "svg", target);
        }
    }

    private void SaveRaster(Shape shape)
    {
        using (var stream = File.OpenRead(shape.OutputPath))
        using (var inputStream = new SKManagedStream(stream))
        {
            var original = SKBitmap.Decode(inputStream);
            var info = GetTargetInfo(original);

            SKBitmap pendingResize;
            if (original.ColorType != SKImageInfo.PlatformColorType)
            {
                pendingResize = new SKBitmap(info);
                original.CopyTo(pendingResize, SKImageInfo.PlatformColorType);
            }
            else
            {
                pendingResize = original;
            }

            var target = new SKBitmap(MAX_SIZE, MAX_SIZE,
                     SKImageInfo.PlatformColorType, SKAlphaType.Premul);

            using (target)
            using (var resized = pendingResize.Resize(info, SKBitmapResizeMethod.Box))
            {
                using (var canvas = new SKCanvas(target))
                {
                    var l = (MAX_SIZE - info.Width) / 2;
                    var t = (MAX_SIZE - info.Height) / 2;
                    var r = l + info.Width;
                    var b = t + info.Height;
                    var rect = new SKRect(l, t, r, b);
                    canvas.Clear();
                    canvas.DrawBitmap(resized, rect, null);
                    SaveToFile(shape.Id, "png", target);
                }
            }

            try
            {
                pendingResize.Dispose();
                original.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void SaveToFile(string id, string dir, SKBitmap bitmap)
    {
        using (var image = SKImage.FromBitmap(bitmap))
        {
            using (var output = File.Open(FileUtils.CreateFileToRoot(dir, $"{id}.png"), FileMode.OpenOrCreate))
            {
                image.Encode(SKEncodedImageFormat.Png, 90).SaveTo(output);
            }
        }
    }
}